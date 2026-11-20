import {useEffect,useId,useRef,useState} from 'react';
import {ContentProvider,ContentText,ModalDialog,PrimaryButton} from '@tunner/ui';
import {requirePublished} from './passkeys.api';
import {formatAccountTime} from './account-format';
import {SessionHttpError} from './sessions.api';
import type {SessionOptions} from './sessions.api';
import {activateSessionBulkRevocation,cancelSessionBulkRevocation,parseSessionBulkRevocationContext,previewSessionBulkRevocation,readSessionBulkRevocationContext,readSessionBulkRevocationResult} from './sessions.bulk.api';
import type {SessionBulkRevocationContext,SessionBulkRevocationOptions,SessionBulkRevocationResult} from './sessions.bulk.api';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import './sessions.bulk.css';

const key=(suffix:string)=>'account.sessions.bulk.'+suffix;
/** UI qualification is separate from the unchanged twelve-key transport publication. */
export const sessionBulkUiContentKeys=['description','keepCurrent','originalOnly','completed.description','unknown.description','unknown.help','back'].map(key);
export function qualifySessionBulkUi(value:SessionBulkRevocationOptions|undefined):SessionBulkRevocationOptions|undefined{
 if(!value)return undefined;try{requirePublished(value.content,value.locale,sessionBulkUiContentKeys);return value;}catch{return undefined;}
}
type Phase='idle'|'previewing'|'review'|'cancelling'|'activating'|'proof'|'checking'|'success'|'unknown'|'changed';
interface Props{options:SessionOptions;csrfToken:string;revision:number;active:boolean;renderAction:boolean;blocked:boolean;initial:SessionBulkRevocationContext|undefined;recoveryUnavailable:boolean;onBlockedChange:(value:boolean)=>void;onAcknowledged:(requestId:string)=>void;onRefresh:()=>void}
/** One complete server-owned original. Loaded/paginated browser rows never enter this controller. */
export function SessionBulkRevocation({options,csrfToken,revision,active,renderAction,blocked,initial,recoveryUnavailable,onBlockedChange,onAcknowledged,onRefresh}:Props){
 const id=useId(),control=useRef(new AbortController()),generation=useRef(0),flight=useRef(false),attempted=useRef(false),activationAttempted=useRef(false),trigger=useRef<HTMLElement|null>(null);
 const original=useRef<SessionBulkRevocationContext|undefined>(undefined),receipt=useRef<SessionBulkRevocationResult|undefined>(undefined);
 const [value,setValue]=useState<SessionBulkRevocationContext>(),[phase,setPhase]=useState<Phase>('idle'),[open,setOpen]=useState(false),[busy,setBusy]=useState(false),[proof,setProof]=useState<StepUpBootstrap>();
 const work=()=>({signal:control.current.signal,generation:generation.current});
 const current=(scope:ReturnType<typeof work>)=>!scope.signal.aborted&&scope.generation===generation.current;
 function running(next:boolean){flight.current=next;setBusy(next);}
 function authenticate(error:unknown){if(error instanceof SessionHttpError&&error.kind==='authentication'){window.location.replace('/login');return true;}return false;}
 function own(found:SessionBulkRevocationContext){original.current=found;setValue(found);attempted.current=true;}
 useEffect(()=>{generation.current++;control.current.abort();control.current=new AbortController();running(false);setProof(undefined);setOpen(false);setPhase(previous=>['previewing','activating','proof','cancelling','checking'].includes(previous)?'unknown':previous);return()=>{generation.current++;control.current.abort();};},[revision,active]);
 useEffect(()=>{
  if(initial){try{const found=parseSessionBulkRevocationContext(initial,original.current);own(found);activationAttempted.current=found.activated;setPhase(found.status==='CHANGED'?'changed':'unknown');}catch{attempted.current=true;setPhase('unknown');}}
  else if(recoveryUnavailable){attempted.current=true;setPhase('unknown');}
 },[initial,recoveryUnavailable]);
 useEffect(()=>{onBlockedChange(attempted.current||!!original.current||busy||!!proof);},[phase,busy,value,proof,onBlockedChange]);
 useEffect(()=>{if(open&&!proof)document.getElementById(id+'-title')?.focus();},[id,open,phase,proof]);
 useEffect(()=>{const leave=()=>{generation.current++;control.current.abort();setProof(undefined);setOpen(false);if(attempted.current)setPhase('unknown');};window.addEventListener('pagehide',leave);return()=>window.removeEventListener('pagehide',leave);},[]);
 async function result(found:SessionBulkRevocationContext,scope:ReturnType<typeof work>){
  const causal=receipt.current?.requestId===found.requestId?receipt.current:await readSessionBulkRevocationResult(csrfToken,found,scope.signal);if(!current(scope))return;
  receipt.current=causal;setProof(undefined);setPhase('success');setOpen(true);
 }
 async function preview(){
  if(!active||blocked||flight.current||attempted.current||original.current)return;const scope=work();trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;attempted.current=true;running(true);setPhase('previewing');
  try{const found=await previewSessionBulkRevocation(csrfToken,scope.signal);if(!current(scope))return;own(found);receipt.current=undefined;activationAttempted.current=found.activated;setPhase(found.status==='DRAFT'&&!found.activated?'review':found.status==='CHANGED'?'changed':'unknown');setOpen(true);}
  catch(error){if(current(scope)&&!authenticate(error)){setPhase('unknown');setOpen(true);}}finally{if(current(scope))running(false);}
 }
 function clear(){original.current=undefined;receipt.current=undefined;attempted.current=false;activationAttempted.current=false;setValue(undefined);setProof(undefined);setOpen(false);setPhase('idle');onBlockedChange(false);}
 async function cancel(){
  const found=original.current;if(flight.current||phase!=='review'||!found||found.activated||activationAttempted.current)return;const scope=work();running(true);setPhase('cancelling');
  try{await cancelSessionBulkRevocation(csrfToken,{requestId:found.requestId},scope.signal);if(!current(scope))return;onAcknowledged(found.requestId);clear();}
  catch(error){if(current(scope)&&!authenticate(error))setPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function confirm(){
  const found=original.current;if(flight.current||phase!=='review'||!found||found.activated||activationAttempted.current)return;const scope=work();activationAttempted.current=true;running(true);setPhase('activating');
  try{
   const activated=await activateSessionBulkRevocation(csrfToken,found,scope.signal);if(!current(scope))return;own(activated);
   if(activated.status==='COMPLETED'){setPhase('checking');await result(activated,scope);return;}
   if(activated.status!=='PENDING'||!activated.activated){setPhase(activated.status==='CHANGED'?'changed':'unknown');return;}
   const bootstrap=await loadStepUpOptions(scope.signal);if(!current(scope))return;if(bootstrap.operationReference!==activated.operationReference)throw new SessionHttpError('unavailable');
   if(bootstrap.status==='COMPLETED'){setPhase('checking');await result(activated,scope);return;}setPhase('proof');setOpen(false);setProof(bootstrap);
  }catch(error){if(current(scope)&&!authenticate(error)){setProof(undefined);setOpen(true);setPhase('unknown');}}finally{if(current(scope))running(false);}
 }
 async function check(){
  if(!active||flight.current||!attempted.current)return;const scope=work();running(true);setPhase('checking');setOpen(true);
  try{let found=original.current;if(!found){found=await readSessionBulkRevocationContext(scope.signal);if(!current(scope))return;if(!found)throw new SessionHttpError('unavailable');own(found);}await result(found,scope);}
  catch(error){if(current(scope)&&!authenticate(error))setPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function afterProof(outcome:StepUpCompletion){
  const found=original.current;setProof(undefined);setOpen(true);if(!found||outcome.operationReference!==found.operationReference||outcome.signInRequired){setPhase('unknown');return;}
  const scope=work();running(true);setPhase('checking');try{await result(found,scope);}catch(error){if(current(scope)&&!authenticate(error))setPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function reconcile(signal:AbortSignal):Promise<StepUpCompletion|null>{
  const found=original.current,scope=work();if(!found||signal.aborted)return null;
  try{const causal=await readSessionBulkRevocationResult(csrfToken,found,signal);if(signal.aborted||!current(scope))return null;receipt.current=causal;return {status:'COMPLETED',signInRequired:false,operationReference:found.operationReference};}catch(error){if(!signal.aborted&&current(scope))authenticate(error);return null;}
 }
 function close(){if(flight.current)return;if(phase==='review'){void cancel();return;}setOpen(false);if(phase==='success'&&receipt.current){onAcknowledged(receipt.current.requestId);clear();onRefresh();}}
 if(!active)return null;const review=phase==='review'||phase==='activating'||phase==='cancelling',success=phase==='success';
 return <ContentProvider locale={options.locale} audience="account-session-bulk" resolve={name=>options.content[name]}>
  {renderAction&&<div className="sessions-grid"><section className="passkeys-card sessions-bulk-controls" data-tunner-component="COMP-SESSION-BULK-REVOKE"><h2><ContentText contentKey={key('action')}/></h2><p className="sessions-muted"><ContentText contentKey={key('description')}/></p><div className="sessions-bulk-control-row"><strong><ContentText contentKey={key('keepCurrent')}/></strong><PrimaryButton appearance="outline" labelKey={key('action')} disabled={blocked||busy||attempted.current} onClick={()=>void preview()}/></div></section></div>}
  {busy&&phase==='previewing'&&<p className="sessions-notice" role="status"><ContentText contentKey={key('loading')}/></p>}
  {!open&&!proof&&attempted.current&&!busy&&<section className="sessions-notice sessions-bulk-recovery" role="status"><p><ContentText contentKey={key(phase==='changed'?'changed':'unknown.description')}/></p><PrimaryButton appearance="outline" labelKey={key('check')} disabled={busy} onClick={()=>void check()}/></section>}
  {open&&!proof&&<ModalDialog labelledBy={id+'-title'} describedBy={id+'-description'} busy={busy} returnFocusTo={trigger.current} onRequestClose={close} className="passkeys-dialog sessions-bulk-dialog"><div className="sessions-bulk-content" data-tunner-component="COMP-SESSION-BULK-REVOKE">
   {success&&<span className="sessions-bulk-mark" aria-hidden="true">✓</span>}<h2 id={id+'-title'} tabIndex={-1}><ContentText contentKey={key(review?'review.title':success?'completed':'unknown')}/></h2><p id={id+'-description'}>{review&&value?<ContentText contentKey={key('review.description')} values={{count:String(value.targets.length)}}/>:success&&value?<ContentText contentKey={key('completed.description')} values={{count:String(value.targets.length)}}/>:<ContentText contentKey={key(phase==='changed'?'changed':'unknown.description')}/>}</p>
   <p className="sessions-bulk-note"><ContentText contentKey={key('keepCurrent')}/></p>
   {value&&<><p className="sessions-bulk-set-label"><ContentText contentKey={key('review.originalSet')} values={{count:String(value.targets.length)}}/>{' '}{value.preparedAt?<time dateTime={value.preparedAt}>{formatAccountTime(value.preparedAt,options.locale)}</time>:<ContentText contentKey="account.sessions.unavailable"/>}</p><ul className="sessions-bulk-set">{value.targets.map(target=><li key={target.reference} data-session-reference={target.reference}><strong><ContentText contentKey="account.sessions.label" values={{reference:target.reference.slice(-8)}}/></strong><p><ContentText contentKey="account.sessions.lastActivity"/>{': '}<ContentText contentKey="account.sessions.unavailable"/></p></li>)}</ul><p className="sessions-bulk-note"><ContentText contentKey={key('originalOnly')}/></p></>}
   <p className="sessions-bulk-note"><ContentText contentKey="account.sessions.productBoundary"/></p>{!review&&!success&&<p className="sessions-bulk-note"><ContentText contentKey={key('unknown.help')}/></p>}{busy&&<p className="sessions-bulk-note" role="status"><ContentText contentKey={key('loading')}/></p>}
   <div className="passkeys-dialog-actions"><PrimaryButton appearance="outline" labelKey={key(phase==='review'?'cancel':'back')} disabled={busy} onClick={close}/>{phase==='review'?<span className="passkeys-danger-wrap"><PrimaryButton labelKey={key('confirm')} disabled={busy} onClick={()=>void confirm()}/></span>:!success&&!busy?<PrimaryButton labelKey={key('check')} onClick={()=>void check()}/>:null}</div>
  </div></ModalDialog>}
  {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={reconcile} onComplete={outcome=>void afterProof(outcome)} onCancel={()=>{setProof(undefined);setPhase('unknown');setOpen(true);}}/>}
 </ContentProvider>;
}
