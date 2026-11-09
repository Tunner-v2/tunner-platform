import { useEffect, useId, useRef, useState } from 'react';
import { z, ContentProvider, ContentText, FormErrorSummary, ModalDialog, PrimaryButton, TextField, useTunnerForm } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import './step-up.css';

const keys=['heading','description','passkey','passkey.description','authenticator','authenticator.description','other','unavailable','password.description',
  'password.label','password.placeholder','back','cancel','confirm','working','error.password','error.highlighted','error.credentials','error.limited','error.unavailable','error.changed'] as const;
export interface StepUpBootstrap {status:'PENDING';requestId:string;csrfToken:string;passwordMaximumLength:number;passwordAvailable:boolean;locale:string;content:Record<string,PublishedContent>}
const pending=z.object({status:z.literal('PENDING'),requestId:z.uuid(),csrfToken:z.string().min(1),passwordMaximumLength:z.number().int().positive(),
  passwordAvailable:z.boolean(),locale:z.string().min(1),content:z.record(z.string(),z.object({key:z.string(),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1)}))});
export function stepUpPasswordSchema(maximum:number){return z.object({password:z.string().refine(value=>Array.from(value).length>0&&Array.from(value).length<=maximum,'stepUp.error.password')});}
export async function loadStepUpOptions(signal?:AbortSignal):Promise<StepUpBootstrap|{status:'COMPLETED'}>{
  const response=await fetch('/authentication/step-up/options',{credentials:'same-origin',cache:'no-store',redirect:'error',...(signal?{signal}:{})});
  if(!response.ok)throw new Error('Step-up unavailable');
  const raw:unknown=await response.json();if(z.object({status:z.literal('COMPLETED')}).safeParse(raw).success)return {status:'COMPLETED'};
  const result=pending.parse(raw);
  if(keys.some(key=>{const item=result.content['stepUp.'+key];return !item||item.key!=='stepUp.'+key||item.locale!==result.locale;}))throw new Error('Step-up publication unavailable');
  return result;
}
export interface StepUpDialogProps{bootstrap:StepUpBootstrap;onComplete:()=>void;onCancel:()=>void;returnFocusTo?:HTMLElement|null}
export function StepUpDialog({bootstrap,onComplete,onCancel,returnFocusTo}:StepUpDialogProps){
  return <ContentProvider locale={bootstrap.locale} audience="step-up" resolve={key=>bootstrap.content[key]}><Dialog bootstrap={bootstrap} onComplete={onComplete} onCancel={onCancel} {...(returnFocusTo?{returnFocusTo}:{})}/></ContentProvider>;
}
function Dialog({bootstrap,onComplete,onCancel,returnFocusTo}:StepUpDialogProps){
  const [view,setView]=useState<'chooser'|'password'|'changed'>('chooser');const [password,setPassword]=useState('');
  const [error,setError]=useState<string>();const [serverField,setServerField]=useState<string>();
  const form=useTunnerForm(stepUpPasswordSchema(bootstrap.passwordMaximumLength),{password});
  const id=useId();const controller=useRef(new AbortController());const closed=useRef(false);
  useEffect(()=>()=>{closed.current=true;controller.current.abort();},[]);
  useEffect(()=>{document.getElementById(view==='password'?id+'-password':id+'-heading')?.focus();},[view,id]);
  useEffect(()=>{if(serverField&&!form.submitPending)document.getElementById(id+'-password')?.focus();},[serverField,id,form.submitPending]);
  function cancel(){if(form.submitPending)return;closed.current=true;setPassword('');onCancel();}
  function complete(){if(closed.current)return;closed.current=true;setPassword('');onComplete();}
  function changed(){setPassword('');setError(undefined);setServerField(undefined);setView('changed');}
  async function submit(){
    setError(undefined);setServerField(undefined);
    try{
      const response=await fetch('/authentication/step-up/password',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.current.signal,
        headers:{'Content-Type':'application/json','X-Tunner-CSRF':bootstrap.csrfToken},body:JSON.stringify({requestId:bootstrap.requestId,password})});
      if(closed.current)return;
      if(response.ok){const result:unknown=await response.json();if(!z.object({status:z.literal('COMPLETED')}).safeParse(result).success)throw new Error('Uncertain');complete();return;}
      const problem=await response.json() as {error_code?:string};
      if(response.status===401&&problem.error_code==='AUTHENTICATION_FAILED'){
        const session=await fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.current.signal});
        if(!session.ok){changed();return;}setServerField('stepUp.error.credentials');
      }else if(response.status===429)setError('stepUp.error.limited');
      else if(response.status===400&&problem.error_code==='VALIDATION_ERROR')setServerField('stepUp.error.password');
      else if([401,403,404,409].includes(response.status))changed();
      else setError('stepUp.error.unavailable');
    }catch{
      if(closed.current)return;
      try{
        const current=await loadStepUpOptions(controller.current.signal);
        if(closed.current)return;
        if(current.status==='COMPLETED'){complete();return;}
        if(current.requestId!==bootstrap.requestId){changed();return;}
      }catch{/* Keep the original owner/view binding and allow only an explicit safe retry. */}
      if(!closed.current)setError('stepUp.error.unavailable');
    }finally{if(!closed.current){setPassword('');form.reset();}}
  }
  return <ModalDialog labelledBy={id+'-heading'} describedBy={id+'-description'} busy={form.submitPending} {...(returnFocusTo?{returnFocusTo}:{})} onRequestClose={cancel} className="step-up-dialog">
    <div data-tunner-component="COMP-STEP-UP" className={'step-up-card '+view}>
      <h1 id={id+'-heading'} tabIndex={-1}><ContentText contentKey="stepUp.heading"/></h1>
      <p id={id+'-description'} className="step-up-intro"><ContentText contentKey={view==='chooser'?'stepUp.description':'stepUp.password.description'}/></p>
      {view==='chooser'?<><div className="step-up-choices">
        <button className="step-up-choice" type="button" disabled title={bootstrap.content['stepUp.unavailable']!.text}><strong><ContentText contentKey="stepUp.passkey"/></strong><small><ContentText contentKey="stepUp.passkey.description"/></small><span aria-hidden="true">›</span></button>
        <button className="step-up-choice" type="button" disabled title={bootstrap.content['stepUp.unavailable']!.text}><strong><ContentText contentKey="stepUp.authenticator"/></strong><small><ContentText contentKey="stepUp.authenticator.description"/></small><span aria-hidden="true">›</span></button>
        <button className="step-up-choice other" type="button" disabled={!bootstrap.passwordAvailable} title={bootstrap.passwordAvailable?undefined:bootstrap.content['stepUp.unavailable']!.text} onClick={()=>setView('password')}><strong><ContentText contentKey="stepUp.other"/></strong><span aria-hidden="true">›</span></button>
      </div><div className="step-up-actions chooser-actions"><PrimaryButton labelKey="stepUp.cancel" appearance="outline" onClick={cancel}/></div></>:
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
