import {useEffect,useId,useLayoutEffect,useRef,useState} from 'react';
import type {FormEvent} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FieldError,FormErrorSummary,PrimaryButton,TextField,ThemeProvider,TunnerBrand,useTunnerForm,z} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';
import {requirePublished} from './passkeys.api';
import {plausibleEmail} from './validation';
import '@tunner/ui/styles.css';
import './authentication.css';
import './step-up.css';
import './recovery.css';

/** Presentation references only; these types do not define an HTTP or recovery-admission contract. */
export interface RecoveryGoalOption {reference:string;labelKey:string;descriptionKey?:string|undefined}
export interface RecoveryProofChoice extends RecoveryGoalOption {canSelect:boolean}
export interface RecoveryDetail {reference:string;labelKey:string;valueKey:string;values?:Readonly<Record<string,string>>|undefined}
interface RecoveryCaseView {goal:RecoveryGoalOption|null;canCheck:boolean;canCancel:boolean}
export type RecoveryViewModel=
 | {kind:'entry';goals:readonly RecoveryGoalOption[];canStart:boolean;canBackToSignIn:boolean}
 | (RecoveryCaseView&{kind:'proof-choice';choices:readonly RecoveryProofChoice[]})
 | (RecoveryCaseView&{kind:'recovery-code';canVerify:boolean;submitLabelKey?:string;purpose?:'recovery'|'contact';proofDigits?:number})
 | (RecoveryCaseView&{kind:'password-repair';canRepair:boolean;passwordMinimumLength:number;passwordMaximumLength:number;canBackToSignIn:boolean})
 | (RecoveryCaseView&{kind:'password-repair-status';state:'pending'|'unknown'|'updated';canViewResult:boolean;canBackToSignIn:boolean})
 | (RecoveryCaseView&{kind:'expired'})
 | (RecoveryCaseView&{kind:'unknown'|'pending-review'|'denied'|'cancelled';canBackToSignIn:boolean})
 | (RecoveryCaseView&{kind:'result';effects:readonly RecoveryDetail[]|null;followUps:readonly RecoveryDetail[]|null;detailsAvailable:boolean;canFinish:boolean;canBackToSignIn:boolean});
export interface RecoveryEntry {email:string;goalReference:string}
export interface RecoveryPasswordEntry {password:string;passwordConfirmation:string}
export type RecoveryFieldErrors=Partial<Record<'email'|'goalReference'|'recoveryCode'|'password'|'passwordConfirmation',string>>;
type RecoveryAction=()=>void|Promise<void>;
export interface RecoveryPresentationProps {
 /** Change when original case, stage, goal, proof source or owner/publication version is replaced. Never displayed or sent as authority. */
 viewKey:string;model:RecoveryViewModel;locale:string;content:Record<string,PublishedContent>;
 links:{privacyUri:string;termsUri:string};busy:boolean;errorKey?:string|undefined;fieldErrors?:RecoveryFieldErrors|undefined;
 callbacks:{onStart:(entry:RecoveryEntry)=>void|Promise<void>;onChooseProof:(reference:string)=>void|Promise<void>;onSubmitCode:(code:string)=>void|Promise<void>;onRepair?:(entry:RecoveryPasswordEntry)=>void|Promise<void>;onViewResult?:RecoveryAction;onCheck:RecoveryAction;onCancel:RecoveryAction;onFinish:RecoveryAction;onBackToSignIn:RecoveryAction;onEdit?:()=>void};
}

const commonKeys=['brand.tunner','recovery.footer.privacy','recovery.footer.terms','recovery.error.unavailable','recovery.working'];
const caseKeys=['recovery.goal.requested','recovery.goal.unavailable','recovery.action.check','recovery.action.cancel'];
const statusKeys={unknown:'unknown','pending-review':'pendingReview',denied:'denied',cancelled:'cancelled'} as const;
function optionKeys(option:RecoveryGoalOption){return [option.labelKey,...(option.descriptionKey?[option.descriptionKey]:[])];}
function uniqueReferences(values:readonly {reference:string}[]){return values.every(value=>value.reference.trim().length>0)&&new Set(values.map(value=>value.reference)).size===values.length;}
function validPasswordBounds(minimum:number,maximum:number){return Number.isSafeInteger(minimum)&&Number.isSafeInteger(maximum)&&minimum>0&&maximum>=minimum;}

function validContactDigits(proofDigits:number|undefined){return Number.isInteger(proofDigits)&&proofDigits!>=6&&proofDigits!<=9;}

/** Mirrors the current contact owner format only; codes remain opaque strings with leading zeros. */
export function recoveryContactSchema(proofDigits:number){
 if(!validContactDigits(proofDigits))throw new Error('Missing recovery contact requirements');
 return z.object({recoveryCode:z.string().min(1,'recovery.contact.error').refine(value=>value.length===proofDigits&&/^[0-9]+$/.test(value),'recovery.contact.format')});
}

/** Advisory recovery validation; requirements come from the original request, independently checked by the server. */
export function recoveryPasswordSchema(minimum:number,maximum:number){
 if(!validPasswordBounds(minimum,maximum))throw new Error('Missing recovery password requirements');
 return z.object({password:z.string().min(1,'recovery.error.passwordRequired').refine(value=>{const length=Array.from(value).length;return length>=minimum&&length<=maximum;},'recovery.error.passwordLength'),passwordConfirmation:z.string().min(1,'recovery.error.passwordConfirmationRequired')})
  .refine(value=>!value.passwordConfirmation||value.password===value.passwordConfirmation,{path:['passwordConfirmation'],message:'recovery.error.passwordConfirmation'});
}

/** Only a mounted recovery view requires these publications; existing/default apps acquire no new required keys. */
export function recoveryPresentationKeys(model:RecoveryViewModel):string[]{
 const keys=[...commonKeys];
 if(model.kind==='entry'){
  keys.push(...['heading','description','email','emailPlaceholder','goal','neutral','optionsUnavailable'].map(key=>'recovery.entry.'+key),'recovery.action.continue','recovery.action.back','recovery.error.email','recovery.error.goal','recovery.error.highlighted');
  for(const goal of model.goals)keys.push(...optionKeys(goal));
 }else{
  keys.push(...caseKeys,...(model.goal?optionKeys(model.goal):[]));
  if(model.kind==='proof-choice'){
   keys.push('recovery.proof.heading','recovery.proof.description','recovery.proof.permitted',...(model.choices.length===0?['recovery.entry.optionsUnavailable']:[]));
   for(const choice of model.choices)keys.push(...optionKeys(choice));
  }else if(model.kind==='recovery-code'){
   const scope=model.purpose==='contact'?'recovery.contact.':'recovery.code.';
   keys.push(...['heading','description','label','show','hide'].map(key=>scope+key),model.submitLabelKey??'recovery.code.verify',model.purpose==='contact'?'recovery.contact.error':'recovery.error.code',...(model.purpose==='contact'?['recovery.contact.format']:[]),'recovery.error.highlighted');
  }
  else if(model.kind==='password-repair')keys.push(...['heading','description','password','confirmation','requirements','submit','boundary'].map(key=>'recovery.repair.'+key),'recovery.action.back',...['passwordRequired','passwordLength','passwordConfirmationRequired','passwordConfirmation','highlighted'].map(key=>'recovery.error.'+key));
  else if(model.kind==='password-repair-status')keys.push(...['heading','description','noticeHeading','noticeDescription'].map(key=>'recovery.repair.'+model.state+'.'+key),...(model.state==='pending'?['recovery.repair.submit']:model.state==='unknown'?['recovery.action.back']:['recovery.repair.updated.boundary','recovery.repair.updated.viewResult']));
  else if(model.kind==='expired')keys.push(...['heading','description','noticeHeading','noticeDescription'].map(key=>'recovery.expired.'+key),'recovery.action.continue');
  else if(model.kind==='result'){
   keys.push(...['heading','description','unavailable','unavailableDescription','sessions','method','verification','restrictions','followUps','none','noFollowUps','boundary'].map(key=>'recovery.result.'+key),'recovery.action.finish','recovery.action.back');
   if(model.detailsAvailable&&Array.isArray(model.effects)&&Array.isArray(model.followUps))for(const detail of [...model.effects,...model.followUps])keys.push(detail.labelKey,detail.valueKey);
  }else keys.push(...['heading','description','noticeHeading','noticeDescription'].map(key=>'recovery.'+statusKeys[model.kind]+'.'+key),'recovery.action.back',...(model.kind==='pending-review'?['recovery.pendingReview.boundary']:[]));
 }
 return [...new Set(keys)];
}

/** No fetch, route integration, provider client, proof engine, persistence or session-success inference. */
export function RecoveryPresentation(props:RecoveryPresentationProps){
 try{
  if(!props.viewKey.trim())throw new Error('Missing recovery view identity');
  if(props.model.kind==='entry'&&!uniqueReferences(props.model.goals)||props.model.kind==='proof-choice'&&!uniqueReferences(props.model.choices))throw new Error('Invalid offered references');
  if(props.model.kind==='result'&&(props.model.effects&&!uniqueReferences(props.model.effects)||props.model.followUps&&!uniqueReferences(props.model.followUps)))throw new Error('Invalid result references');
  if(props.model.kind==='recovery-code'&&props.model.purpose==='contact'&&!validContactDigits(props.model.proofDigits))throw new Error('Recovery contact is unavailable');
  if(props.model.kind==='password-repair'&&(!validPasswordBounds(props.model.passwordMinimumLength,props.model.passwordMaximumLength)||typeof props.callbacks.onRepair!=='function'))throw new Error('Recovery repair is unavailable');
  if(props.model.kind==='password-repair-status'&&props.model.state==='updated'&&props.model.canViewResult===true&&typeof props.callbacks.onViewResult!=='function')throw new Error('Recovery result handoff is unavailable');
  requirePublished(props.content,props.locale,[...recoveryPresentationKeys(props.model),...(props.errorKey?[props.errorKey]:[]),...Object.values(props.fieldErrors??{}).filter((key):key is string=>!!key)]);
  if(props.model.kind==='password-repair'){
   const variables=[...props.content['recovery.repair.requirements']!.text.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g)].map(match=>match[1]);
   if(!variables.includes('minimum')||!variables.includes('maximum')||variables.some(value=>value!=='minimum'&&value!=='maximum'))throw new Error('Missing recovery requirement publication variables');
  }
 }catch{return <ContentUnavailable/>;}
 return <ThemeProvider surface="user" mode="light"><ContentProvider audience="recovery" locale={props.locale} resolve={key=>props.content[key]}><RecoveryBody key={props.viewKey} {...props}/></ContentProvider></ThemeProvider>;
}

function useRecoveryAction(externalBusy:boolean){
 const live=useRef(true),held=useRef(false);const [pending,setPending]=useState(false),[error,setError]=useState<string>();
 useEffect(()=>{
  live.current=true;
  const leave=()=>{live.current=false;},returned=(event:PageTransitionEvent)=>{if(event.persisted){live.current=true;setPending(held.current);setError(undefined);}};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);
  return()=>{live.current=false;window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);};
 },[]);
 async function run(action:RecoveryAction){
  if(held.current||externalBusy||!live.current)return;
  held.current=true;setPending(true);setError(undefined);
  try{await action();}catch{if(live.current)setError('recovery.error.unavailable');}
  finally{held.current=false;if(live.current)setPending(false);}
 }
 return {pending,error,run,clearError:()=>setError(undefined)};
}

function RecoveryBody(props:RecoveryPresentationProps){
 const {model,callbacks}=props,action=useRecoveryAction(props.busy),disabled=props.busy||action.pending;
 const heading=useRef<HTMLHeadingElement>(null);
 const focusState=model.kind==='password-repair-status'?model.state:model.kind;
 useLayoutEffect(()=>{heading.current?.focus({preventScroll:true});},[focusState]);
 function edited(){action.clearError();callbacks.onEdit?.();}
 const errorKey=props.errorKey??action.error;
 const stateKey=model.kind==='entry'?'entry':model.kind==='proof-choice'?'proof':model.kind==='recovery-code'?model.purpose==='contact'?'contact':'code':model.kind==='password-repair'?'repair':model.kind==='password-repair-status'?'repair.'+model.state:model.kind==='expired'?'expired':model.kind==='result'?'result':statusKeys[model.kind];
 const details=model.kind==='result'&&model.detailsAvailable===true&&Array.isArray(model.effects)&&Array.isArray(model.followUps);
 return <div className="authentication-page recovery-page">
  <img className="authentication-glow indigo" src="/registration/assets/login-bg-indigo.svg" alt="" aria-hidden="true"/>
  <img className="authentication-glow violet" src="/registration/assets/login-bg-violet.svg" alt="" aria-hidden="true"/>
  <header className="authentication-header"><TunnerBrand contentKey="brand.tunner"/></header>
  <main className="authentication-stage"><section className={'authentication-card recovery-card '+(model.kind==='entry'?'entry':model.kind==='proof-choice'||model.kind==='recovery-code'||model.kind==='password-repair'||model.kind==='password-repair-status'||model.kind==='expired'?'proof':'status')} aria-busy={disabled}>
   <div className="authentication-intro"><h1 tabIndex={-1} ref={heading}><ContentText contentKey={'recovery.'+stateKey+'.heading'}/></h1><p><ContentText contentKey={model.kind==='result'&&!details?'recovery.result.unavailableDescription':'recovery.'+stateKey+'.description'}/></p></div>
   {model.kind!=='entry'&&<GoalContext goal={model.goal}/>}
   {model.kind==='entry'?<EntryForm model={model} disabled={disabled} fieldErrors={props.fieldErrors} onEdit={edited} onSubmit={entry=>action.run(()=>callbacks.onStart(entry))}/>:
    model.kind==='proof-choice'?<><div className="step-up-choices">{model.choices.map(choice=><button className="step-up-choice" key={choice.reference} type="button" disabled={disabled||!model.goal||choice.canSelect!==true} onClick={()=>void action.run(()=>callbacks.onChooseProof(choice.reference))}><strong><ContentText contentKey={choice.labelKey}/></strong>{choice.descriptionKey&&<small><ContentText contentKey={choice.descriptionKey}/></small>}<span aria-hidden="true">›</span></button>)}</div><p className="recovery-explanation"><ContentText contentKey={model.choices.length===0?'recovery.entry.optionsUnavailable':'recovery.proof.permitted'}/></p></>:
    model.kind==='recovery-code'?<CodeForm key={(model.goal?.reference??'unavailable')+':'+(model.purpose??'recovery')+':'+(model.proofDigits??'')} disabled={disabled||model.canVerify!==true||!model.goal} purpose={model.purpose} proofDigits={model.proofDigits} submitLabelKey={model.submitLabelKey} fieldErrors={props.fieldErrors} onEdit={edited} onSubmit={code=>action.run(()=>callbacks.onSubmitCode(code))}/>:
    model.kind==='password-repair'?<><PasswordRepairForm key={(model.goal?.reference??'unavailable')+':'+model.passwordMinimumLength+':'+model.passwordMaximumLength} minimum={model.passwordMinimumLength} maximum={model.passwordMaximumLength} disabled={disabled||model.canRepair!==true||!model.goal} fieldErrors={props.fieldErrors} onEdit={edited} onSubmit={entry=>action.run(()=>callbacks.onRepair!(entry))}/><p className="recovery-explanation"><ContentText contentKey="recovery.repair.boundary"/></p></>:
    model.kind==='password-repair-status'?<><Notice state={'repair.'+model.state}/>{model.state==='updated'&&<p className="recovery-explanation"><ContentText contentKey="recovery.repair.updated.boundary"/></p>}</>:
    model.kind==='expired'?<Notice state="expired" error/>:
    model.kind==='result'?<RecoveryResult model={model} detailsAvailable={details}/>:<><Notice state={statusKeys[model.kind]}/>{model.kind==='pending-review'&&<p className="recovery-explanation"><ContentText contentKey="recovery.pendingReview.boundary"/></p>}</>}
   {model.kind==='password-repair-status'?<div className="recovery-actions">
    {model.state!=='updated'&&model.canCheck&&<PrimaryButton labelKey="recovery.action.check" disabled={disabled} onClick={()=>void action.run(callbacks.onCheck)}/>}
    {model.state==='pending'&&<PrimaryButton labelKey="recovery.repair.submit" disabled/>}
    {model.state==='updated'&&<PrimaryButton labelKey="recovery.repair.updated.viewResult" disabled={disabled||!model.goal||model.canViewResult!==true} onClick={()=>void action.run(callbacks.onViewResult!)}/>}
   </div>:model.kind!=='entry'&&<div className="recovery-actions">
    {model.canCheck&&<PrimaryButton labelKey="recovery.action.check" appearance={model.kind==='proof-choice'||model.kind==='recovery-code'||model.kind==='password-repair'?'outline':'filled'} disabled={disabled} onClick={()=>void action.run(callbacks.onCheck)}/>}
    {model.kind==='expired'&&<PrimaryButton labelKey="recovery.action.continue" disabled/>}
    {model.kind==='result'&&<PrimaryButton labelKey="recovery.action.finish" disabled={disabled||!details||!model.goal||model.canFinish!==true} onClick={()=>void action.run(callbacks.onFinish)}/>}
    {model.canCancel&&<PrimaryButton labelKey="recovery.action.cancel" appearance="outline" disabled={disabled} onClick={()=>void action.run(callbacks.onCancel)}/>}
   </div>}
   {errorKey&&<FormErrorSummary contentKey={errorKey}/>}
   {disabled&&model.kind!=='password-repair-status'&&<p className="recovery-explanation" role="status"><ContentText contentKey="recovery.working"/></p>}
   {'canBackToSignIn' in model&&model.canBackToSignIn&&(model.kind!=='password-repair-status'||model.state==='unknown')&&<button className="recovery-text-link" type="button" disabled={disabled} onClick={()=>void action.run(callbacks.onBackToSignIn)}><ContentText contentKey="recovery.action.back"/></button>}
  </section></main>
  <footer className="authentication-footer"><a href={props.links.privacyUri}><ContentText contentKey="recovery.footer.privacy"/></a><span aria-hidden="true">•</span><a href={props.links.termsUri}><ContentText contentKey="recovery.footer.terms"/></a></footer>
 </div>;
}

function GoalContext({goal}:{goal:RecoveryGoalOption|null}){
 return <aside className="recovery-context"><p className="recovery-context-label"><ContentText contentKey="recovery.goal.requested"/></p><strong><ContentText contentKey={goal?.labelKey??'recovery.goal.unavailable'}/></strong></aside>;
}

function EntryForm({model,disabled,fieldErrors,onEdit,onSubmit}:{model:Extract<RecoveryViewModel,{kind:'entry'}>;disabled:boolean;fieldErrors:RecoveryFieldErrors|undefined;onEdit:()=>void;onSubmit:(entry:RecoveryEntry)=>Promise<void>}){
 const [email,setEmail]=useState(''),[goalReference,setGoal]=useState('');const id=useId();
 const schema=z.object({email:z.string().refine(plausibleEmail,'recovery.error.email'),goalReference:z.string().refine(value=>model.goals.some(goal=>goal.reference===value),'recovery.error.goal')});
 const form=useTunnerForm(schema,{email,goalReference}),emailError=form.errors.email??fieldErrors?.email,goalError=form.errors.goalReference??fieldErrors?.goalReference;
 const blocked=disabled||form.submitPending||model.canStart!==true||model.goals.length===0;
 function choose(reference:string){if(blocked)return;setGoal(reference);onEdit();}
 function submit(event:FormEvent<HTMLFormElement>){void form.handleSubmit(event,async value=>{if(!blocked)await onSubmit(value);});}
 return <form noValidate data-tunner-form="FORM-RECOVERY-ENTRY" onSubmit={submit}>
  <div className="authentication-fields">
   <TextField labelKey="recovery.entry.email" placeholderKey="recovery.entry.emailPlaceholder" name="email" type="email" autoComplete="email" value={email} required disabled={blocked} errorKey={emailError} onBlur={event=>form.onBlur('email',event)} onChange={event=>{setEmail(event.target.value);onEdit();}}/>
   <div className="recovery-goal-field"><span className="recovery-goal-label" id={id+'-label'}><ContentText contentKey="recovery.entry.goal"/></span>
    <div role="radiogroup" aria-labelledby={id+'-label'} aria-required="true" aria-invalid={goalError?true:undefined} aria-describedby={goalError?id+'-error':undefined}>
     {model.goals.map((goal,index)=><button className="step-up-choice" key={goal.reference} name="goalReference" type="button" role="radio" aria-checked={goal.reference===goalReference} aria-invalid={goalError?true:undefined} aria-describedby={goalError?id+'-error':undefined} tabIndex={goalReference?goal.reference===goalReference?0:-1:index===0?0:-1} disabled={blocked} onClick={()=>choose(goal.reference)} onBlur={event=>form.onBlur('goalReference',event)} onKeyDown={event=>{if(!['ArrowDown','ArrowUp','ArrowLeft','ArrowRight'].includes(event.key)||blocked)return;event.preventDefault();const offset=event.key==='ArrowDown'||event.key==='ArrowRight'?1:-1,next=(index+offset+model.goals.length)%model.goals.length,option=model.goals[next];if(option){choose(option.reference);event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role=radio]')[next]?.focus();}}}><strong><ContentText contentKey={goal.labelKey}/></strong>{goal.descriptionKey&&<small><ContentText contentKey={goal.descriptionKey}/></small>}<span aria-hidden="true">›</span></button>)}
    </div>{goalError&&<FieldError id={id+'-error'} contentKey={goalError}/>}</div>
  </div>
  {model.goals.length===0&&<p className="recovery-explanation" role="status"><ContentText contentKey="recovery.entry.optionsUnavailable"/></p>}
  <p className="recovery-explanation"><ContentText contentKey="recovery.entry.neutral"/></p>
  <PrimaryButton type="submit" labelKey="recovery.action.continue" disabled={blocked} aria-busy={form.submitPending}/>
  {form.hasErrors&&<FormErrorSummary contentKey="recovery.error.highlighted"/>}
 </form>;
}

function CodeForm({disabled,purpose,proofDigits,submitLabelKey,fieldErrors,onEdit,onSubmit}:{disabled:boolean;purpose:'recovery'|'contact'|undefined;proofDigits:number|undefined;submitLabelKey:string|undefined;fieldErrors:RecoveryFieldErrors|undefined;onEdit:()=>void;onSubmit:(code:string)=>Promise<void>}){
 const [code,setCode]=useState(''),[visible,setVisible]=useState(false);const element=useRef<HTMLFormElement>(null);
 const scope=purpose==='contact'?'recovery.contact.':'recovery.code.';
 const schema=purpose==='contact'?recoveryContactSchema(proofDigits!):z.object({recoveryCode:z.string().min(1,'recovery.error.code')});
 const form=useTunnerForm(schema,{recoveryCode:code});
 function clearInput(target:HTMLFormElement|null){const input=target?.elements.namedItem('recoveryCode');if(input instanceof HTMLInputElement)input.value='';}
 useEffect(()=>{if(disabled){clearInput(element.current);setCode('');setVisible(false);form.reset();}},[disabled]);
 useEffect(()=>{const current=element.current;const clear=()=>{clearInput(current);setCode('');setVisible(false);form.reset();};window.addEventListener('pagehide',clear);return()=>{clearInput(current);window.removeEventListener('pagehide',clear);};},[]);
 function submit(event:FormEvent<HTMLFormElement>){void form.handleSubmit(event,async value=>{if(disabled)return;clearInput(element.current);form.reset();setCode('');setVisible(false);await onSubmit(value.recoveryCode);});}
 return <form ref={element} noValidate data-tunner-form="FORM-RECOVERY-CODE" onSubmit={submit}>
  <div className="recovery-code-field"><TextField labelKey={scope+'label'} name="recoveryCode" type={visible?'text':'password'} autoComplete="off" value={code} required disabled={disabled||form.submitPending} errorKey={form.errors.recoveryCode??fieldErrors?.recoveryCode} onBlur={event=>form.onBlur('recoveryCode',event)} onChange={event=>{setCode(event.target.value);onEdit();}}/><button className="recovery-code-reveal" type="button" disabled={disabled||form.submitPending} onClick={()=>setVisible(value=>!value)}><span className="recovery-sr-only"><ContentText contentKey={scope+(visible?'hide':'show')}/></span><img src="/registration/assets/login-eye-off.svg" alt=""/></button></div>
  <PrimaryButton type="submit" labelKey={submitLabelKey??'recovery.code.verify'} disabled={disabled||form.submitPending} aria-busy={form.submitPending}/>
  {form.hasErrors&&<FormErrorSummary contentKey="recovery.error.highlighted"/>}
 </form>;
}

function PasswordRepairForm({minimum,maximum,disabled,fieldErrors,onEdit,onSubmit}:{minimum:number;maximum:number;disabled:boolean;fieldErrors:RecoveryFieldErrors|undefined;onEdit:()=>void;onSubmit:(entry:RecoveryPasswordEntry)=>Promise<void>}){
 const [password,setPassword]=useState(''),[passwordConfirmation,setConfirmation]=useState('');const element=useRef<HTMLFormElement>(null);
 const form=useTunnerForm(recoveryPasswordSchema(minimum,maximum),{password,passwordConfirmation});
 function clearInputs(target:HTMLFormElement|null){for(const name of ['password','passwordConfirmation']){const input=target?.elements.namedItem(name);if(input instanceof HTMLInputElement)input.value='';}}
 function clear(){clearInputs(element.current);setPassword('');setConfirmation('');form.reset();}
 useLayoutEffect(()=>{if(disabled)clear();},[disabled]);
 useLayoutEffect(()=>{const current=element.current;window.addEventListener('pagehide',clear);return()=>{clearInputs(current);window.removeEventListener('pagehide',clear);};},[]);
 function submit(event:FormEvent<HTMLFormElement>){void form.handleSubmit(event,async value=>{if(disabled)return;clear();await onSubmit(value);});}
 return <form ref={element} noValidate data-tunner-form="FORM-RECOVERY-PASSWORD-REPAIR" onSubmit={submit}>
  <div className="authentication-fields">
   <TextField labelKey="recovery.repair.password" helpKey="recovery.repair.requirements" helpValues={{minimum:String(minimum),maximum:String(maximum)}} name="password" type="password" autoComplete="new-password" value={password} required disabled={disabled||form.submitPending} errorKey={form.errors.password??fieldErrors?.password} onBlur={event=>form.onBlur('password',event)} onChange={event=>{setPassword(event.target.value);onEdit();}}/>
   <TextField labelKey="recovery.repair.confirmation" name="passwordConfirmation" type="password" autoComplete="new-password" value={passwordConfirmation} required disabled={disabled||form.submitPending} errorKey={form.errors.passwordConfirmation??fieldErrors?.passwordConfirmation} onBlur={event=>form.onBlur('passwordConfirmation',event)} onChange={event=>{setConfirmation(event.target.value);onEdit();}}/>
  </div>
  <PrimaryButton type="submit" labelKey="recovery.repair.submit" disabled={disabled||form.submitPending} aria-busy={form.submitPending}/>
  {form.hasErrors&&<FormErrorSummary contentKey="recovery.error.highlighted"/>}
 </form>;
}

function Notice({state,error=false}:{state:string;error?:boolean}){
 return <div className={'recovery-notice'+(error?' error':'')} role="status"><strong><ContentText contentKey={'recovery.'+state+'.noticeHeading'}/></strong><p><ContentText contentKey={'recovery.'+state+'.noticeDescription'}/></p></div>;
}

function RecoveryResult({model,detailsAvailable}:{model:Extract<RecoveryViewModel,{kind:'result'}>;detailsAvailable:boolean}){
 return <>{detailsAvailable?<>
  {model.effects!.length>0?<dl className="recovery-effect-facts">{model.effects!.map(detail=><Detail key={detail.reference} detail={detail}/>)}</dl>:<p className="recovery-explanation"><ContentText contentKey="recovery.result.none"/></p>}
  <div className="recovery-notice"><strong><ContentText contentKey="recovery.result.followUps"/></strong>{model.followUps!.length>0?<dl className="recovery-effect-facts">{model.followUps!.map(detail=><Detail key={detail.reference} detail={detail}/>)}</dl>:<p><ContentText contentKey="recovery.result.noFollowUps"/></p>}</div>
 </>:<dl className="recovery-effect-facts">{['sessions','method','verification','restrictions','followUps'].map(key=><div key={key}><dt><ContentText contentKey={'recovery.result.'+key}/></dt><dd><ContentText contentKey="recovery.result.unavailable"/></dd></div>)}</dl>}<p className="recovery-explanation"><ContentText contentKey="recovery.result.boundary"/></p></>;
}
function Detail({detail}:{detail:RecoveryDetail}){return <div><dt><ContentText contentKey={detail.labelKey}/></dt><dd><ContentText contentKey={detail.valueKey} {...(detail.values?{values:detail.values}:{})}/></dd></div>;}
