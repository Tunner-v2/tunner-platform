import { useEffect, useId, useRef, useState } from 'react';
import { z, ContentProvider, ContentText, FormErrorSummary, ModalDialog, PrimaryButton, TextField, useTunnerForm } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import './step-up.css';
import {requirePublished} from './passkeys.api';
import {stepUpPasskeyKeys,stepUpPasskeyRecoverySchema,useStepUpPasskey} from './step-up.passkey';

const keys=['heading','description','passkey','passkey.description','authenticator','authenticator.description','other','unavailable','password.description',
  'password.label','password.placeholder','back','cancel','confirm','working','error.password','error.highlighted','error.credentials','error.limited','error.unavailable','error.changed'] as const;
export interface StepUpBootstrap {status:'PENDING';requestId:string;csrfToken:string;passwordMaximumLength:number;passwordAvailable:boolean;locale:string;content:Record<string,PublishedContent>;operationReference?:string|undefined;passkeyAvailable?:boolean|undefined;maximumCredentialJsonBytes?:number|null|undefined;passkeyRecovery?:z.infer<typeof stepUpPasskeyRecoverySchema>|null|undefined}
const pending=z.object({status:z.literal('PENDING'),requestId:z.uuid(),csrfToken:z.string().min(1),passwordMaximumLength:z.number().int().positive(),
  passwordAvailable:z.boolean(),operationReference:z.string().min(1).optional(),passkeyAvailable:z.boolean().optional(),maximumCredentialJsonBytes:z.number().int().min(1024).max(1048576).nullable().optional(),passkeyRecovery:stepUpPasskeyRecoverySchema.nullable().optional(),locale:z.string().min(1),content:z.record(z.string(),z.object({key:z.string(),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1)}))});
export function stepUpPasswordSchema(maximum:number){return z.object({password:z.string().refine(value=>Array.from(value).length>0&&Array.from(value).length<=maximum,'stepUp.error.password')});}
const completed=z.object({status:z.literal('COMPLETED'),requestId:z.uuid().nullable(),operationReference:z.string().min(1).optional()});
export async function loadStepUpOptions(signal?:AbortSignal):Promise<StepUpBootstrap|{status:'COMPLETED';requestId:string|null;operationReference?:string|undefined}>{
  const response=await fetch('/authentication/step-up/options',{credentials:'same-origin',cache:'no-store',redirect:'error',...(signal?{signal}:{})});
  if(!response.ok)throw new Error('Step-up unavailable');
  const raw:unknown=await response.json();const done=completed.safeParse(raw);if(done.success)return done.data;
  const result=pending.parse(raw);
  const native=result.passkeyAvailable===true||!!result.passkeyRecovery;
  if(native&&(!result.operationReference||result.passkeyAvailable===true&&!result.maximumCredentialJsonBytes))throw new Error('Explicit native proof context is required.');
  if(result.passkeyRecovery&&(result.passkeyRecovery.requestId!==result.requestId||result.passkeyRecovery.operationReference!==result.operationReference))throw new Error('The original proof context changed.');
  requirePublished(result.content,result.locale,[...keys.map(key=>'stepUp.'+key),...(native?stepUpPasskeyKeys:[])]);
  return result;
}
export interface StepUpCompletion{status:'COMPLETED';signInRequired:boolean;operationReference?:string}
export interface StepUpDismissal{unresolved:boolean}
export interface StepUpDialogProps{bootstrap:StepUpBootstrap;onComplete:(outcome:StepUpCompletion)=>void;onCancel:(outcome?:StepUpDismissal)=>void;returnFocusTo?:HTMLElement|null;reconcile?:(signal:AbortSignal)=>Promise<StepUpCompletion|null>}
export function StepUpDialog({bootstrap,onComplete,onCancel,returnFocusTo,reconcile}:StepUpDialogProps){
  return <ContentProvider locale={bootstrap.locale} audience="step-up" resolve={key=>bootstrap.content[key]}><Dialog bootstrap={bootstrap} onComplete={onComplete} onCancel={onCancel} {...(returnFocusTo?{returnFocusTo}:{})} {...(reconcile?{reconcile}:{})}/></ContentProvider>;
}
function Dialog({bootstrap,onComplete,onCancel,returnFocusTo,reconcile}:StepUpDialogProps){
  const [view,setView]=useState<'chooser'|'password'|'changed'>('chooser');const [password,setPassword]=useState('');
  const [error,setError]=useState<string>();const [serverField,setServerField]=useState<string>();
  const form=useTunnerForm(stepUpPasswordSchema(bootstrap.passwordMaximumLength),{password});
  const id=useId();const controller=useRef(new AbortController());const closed=useRef(false);
  useEffect(()=>{closed.current=false;controller.current=new AbortController();return()=>{closed.current=true;controller.current.abort();};},[]);
  const native=useStepUpPasskey({bootstrap,load:loadStepUpOptions,complete,reconcile});
  useEffect(()=>{document.getElementById(!native.active&&view==='password'?id+'-password':id+'-heading')?.focus();},[view,id,native.mode,native.active]);
  useEffect(()=>{if(serverField&&!form.submitPending)document.getElementById(id+'-password')?.focus();},[serverField,id,form.submitPending]);
  function cancel(){if(form.submitPending||native.busy)return;closed.current=true;setPassword('');onCancel(native.unresolved?{unresolved:true}:undefined);}
  function complete(outcome:StepUpCompletion={status:'COMPLETED',signInRequired:false}){if(closed.current)return;closed.current=true;setPassword('');onComplete(outcome);}
  async function reconcileOwner(){if(!reconcile)return false;const outcome=await reconcile(controller.current.signal);if(closed.current)return false;if(outcome?.status==='COMPLETED'){complete(outcome);return true;}return false;}
  function changed(){setPassword('');setError(undefined);setServerField(undefined);setView('changed');}
  async function submit(){
    setError(undefined);setServerField(undefined);
    try{
      const response=await fetch('/authentication/step-up/password',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.current.signal,
        headers:{'Content-Type':'application/json','X-Tunner-CSRF':bootstrap.csrfToken},body:JSON.stringify({requestId:bootstrap.requestId,password})});
      if(closed.current)return;
      if(response.ok){const result=z.object({status:z.literal('COMPLETED'),signInRequired:z.boolean().optional(),operationReference:z.string().min(1).optional()}).parse(await response.json());if(bootstrap.operationReference!==undefined&&result.operationReference!==bootstrap.operationReference)throw new Error('Uncertain');complete({status:'COMPLETED',signInRequired:result.signInRequired===true,...(result.operationReference?{operationReference:result.operationReference}:{})});return;}
      const problem=await response.json() as {error_code?:string};
      if(response.status===401&&problem.error_code==='AUTHENTICATION_FAILED'){
        const session=await fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.current.signal});
        if(!session.ok){if(await reconcileOwner())return;changed();return;}setServerField('stepUp.error.credentials');
      }else if(response.status===429)setError('stepUp.error.limited');
      else if(response.status===400&&problem.error_code==='VALIDATION_ERROR')setServerField('stepUp.error.password');
      else if([401,403,404,409].includes(response.status)){if(await reconcileOwner())return;changed();}
      else setError('stepUp.error.unavailable');
    }catch{
      if(closed.current)return;
      try{
        if(await reconcileOwner())return;
        const current=await loadStepUpOptions(controller.current.signal);
        if(closed.current)return;
        if(current.requestId!==bootstrap.requestId||bootstrap.operationReference!==undefined&&current.operationReference!==bootstrap.operationReference){changed();return;}
        if(current.status==='COMPLETED'){complete();return;}
      }catch{/* Keep the original owner/view binding and allow only an explicit safe retry. */}
      if(!closed.current)setError('stepUp.error.unavailable');
    }finally{if(!closed.current){setPassword('');form.reset();}}
  }
  return <ModalDialog labelledBy={id+'-heading'} describedBy={id+'-description'} busy={form.submitPending||native.busy} {...(returnFocusTo?{returnFocusTo}:{})} onRequestClose={cancel} className="step-up-dialog">
    <div data-tunner-component="COMP-STEP-UP" className={'step-up-card '+view}>
      <h1 id={id+'-heading'} tabIndex={-1}><ContentText contentKey="stepUp.heading"/></h1>
      <p id={id+'-description'} className="step-up-intro"><ContentText contentKey={native.active||view==='chooser'?'stepUp.description':'stepUp.password.description'}/></p>
      {native.active?<div className="step-up-passkey" data-tunner-component="COMP-STEP-UP-PASSKEY"><p className="step-up-status" role="status"><ContentText contentKey={native.mode==='pending'?'stepUp.passkey.pending':native.mode==='not-completed'?'stepUp.passkey.notCompleted':native.mode==='changed'?'stepUp.error.changed':'stepUp.passkey.unknown'}/></p>{native.error&&<FormErrorSummary contentKey={native.error}/>}<div className="step-up-actions">{native.mode==='not-completed'&&<PrimaryButton labelKey="stepUp.back" appearance="outline" disabled={native.busy} onClick={native.back}/>}<PrimaryButton labelKey={native.unresolved?'stepUp.passkey.close':'stepUp.cancel'} appearance="outline" disabled={native.busy} onClick={cancel}/>{(native.mode==='unknown'||native.mode==='issued')&&<><PrimaryButton labelKey="stepUp.passkey.check" appearance="outline" disabled={native.busy} onClick={()=>void native.check()}/><PrimaryButton labelKey="stepUp.passkey.resume" disabled={native.busy||!native.canResume} onClick={()=>void native.resume()}/></>}</div></div>:view==='chooser'?<><div className="step-up-choices">
        <button className="step-up-choice" type="button" disabled={!native.canBegin} title={native.canBegin?undefined:bootstrap.content[bootstrap.passkeyAvailable===true?'stepUp.passkey.unsupported':'stepUp.unavailable']!.text} aria-label={bootstrap.content['stepUp.passkey']!.text} aria-describedby={id+'-passkey-description'} onClick={()=>void native.begin()}><strong><ContentText contentKey="stepUp.passkey"/></strong><small id={id+'-passkey-description'}><ContentText contentKey="stepUp.passkey.description"/></small><span aria-hidden="true">›</span></button>
        <button className="step-up-choice" type="button" disabled title={bootstrap.content['stepUp.unavailable']!.text}><strong><ContentText contentKey="stepUp.authenticator"/></strong><small><ContentText contentKey="stepUp.authenticator.description"/></small><span aria-hidden="true">›</span></button>
        <button className="step-up-choice other" type="button" disabled={!bootstrap.passwordAvailable} title={bootstrap.passwordAvailable?undefined:bootstrap.content['stepUp.unavailable']!.text} onClick={()=>setView('password')}><strong><ContentText contentKey="stepUp.other"/></strong><span aria-hidden="true">›</span></button>
      </div>{bootstrap.passkeyAvailable===true&&!native.supported&&<p className="step-up-status" role="status"><ContentText contentKey="stepUp.passkey.unsupported"/></p>}<div className="step-up-actions chooser-actions"><PrimaryButton labelKey="stepUp.cancel" appearance="outline" onClick={cancel}/></div></>:
      view==='changed'?<><p className="step-up-notice" role="alert"><ContentText contentKey="stepUp.error.changed"/></p><div className="step-up-actions"><PrimaryButton labelKey="stepUp.cancel" appearance="outline" onClick={cancel}/></div></>:
      <form noValidate data-tunner-form="FORM-STEP-UP" onSubmit={event=>void form.handleSubmit(event,submit)}>
        <TextField id={id+'-password'} name="password" labelKey="stepUp.password.label" placeholderKey="stepUp.password.placeholder" type="password" autoComplete="current-password" required
          value={password} disabled={form.submitPending} errorKey={serverField??form.errors.password} onBlur={event=>form.onBlur('password',event)} onChange={event=>{setPassword(event.target.value);setError(undefined);setServerField(undefined);}}/>
        {form.hasErrors&&<FormErrorSummary contentKey="stepUp.error.highlighted"/>}
        {error&&<FormErrorSummary contentKey={error}/>}
        <div className="step-up-actions"><PrimaryButton labelKey="stepUp.back" appearance="outline" disabled={form.submitPending} onClick={()=>{setPassword('');form.reset();setError(undefined);setServerField(undefined);setView('chooser');}}/>
          <PrimaryButton labelKey="stepUp.cancel" appearance="outline" disabled={form.submitPending} onClick={cancel}/><PrimaryButton labelKey="stepUp.confirm" type="submit" disabled={form.submitPending} aria-busy={form.submitPending}/></div>
        {form.submitPending&&<p className="step-up-status" role="status"><ContentText contentKey="stepUp.working"/></p>}
      </form>}
    </div>
  </ModalDialog>;
}
