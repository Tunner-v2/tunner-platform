import {useEffect,useId,useRef,useState} from 'react';
import {ContentProvider,ContentText,ModalDialog,PrimaryButton} from '@tunner/ui';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import {prepareSessionRevocation,readSessionRevocationContext,readSessionRevocationResult,SessionHttpError} from './sessions.api';
import type {SessionItem,SessionOptions,SessionRevocationContext,SessionRevocationRequest,SessionRevocationResult} from './sessions.api';
import {formatSessionTime} from './sessions.ui';

type Phase='review'|'preparing'|'proof'|'checking'|'success'|'unknown'|'changed';
interface Props{
 options:SessionOptions;csrfToken:string;revision:number;initial:SessionRevocationContext|undefined;selection:SessionItem|undefined;
 onSelectionHandled:()=>void;onBlockedChange:(blocked:boolean)=>void;onAcknowledged:(requestId:string)=>void;onRefresh:()=>void;
}
const key=(suffix:string)=>'account.sessions.revoke.'+suffix;
/** One retained owner intent. Proof completion only reads its causal result; it never posts another mutation. */
export function SessionRevocation({options,csrfToken,revision,initial,selection,onSelectionHandled,onBlockedChange,onAcknowledged,onRefresh}:Props){
 const id=useId(),control=useRef(new AbortController()),generation=useRef(0),flight=useRef(false),seen=useRef(new Set<string>());
 const original=useRef<SessionRevocationContext|undefined>(undefined),candidate=useRef<(SessionRevocationRequest&{lastSeenAt:string})|undefined>(undefined),receipt=useRef<SessionRevocationResult|undefined>(undefined),trigger=useRef<HTMLElement|null>(null);
 const [phase,setPhase]=useState<Phase>('review'),[open,setOpen]=useState(false),[busy,setBusy]=useState(false),[proof,setProof]=useState<StepUpBootstrap>(),[target,setTarget]=useState<{reference:string;lastSeenAt:string}>();
 const work=()=>({signal:control.current.signal,generation:generation.current});
 const current=(value:ReturnType<typeof work>)=>!value.signal.aborted&&value.generation===generation.current;
 function own(value:SessionRevocationContext){original.current=value;setTarget(value.target);seen.current.add(value.requestId);}
 function authenticate(error:unknown){if(error instanceof SessionHttpError&&error.kind==='authentication'){window.location.replace('/login');return true;}return false;}
 function running(value:boolean){flight.current=value;setBusy(value);}
 useEffect(()=>{generation.current++;control.current.abort();control.current=new AbortController();flight.current=false;setBusy(false);setProof(undefined);setPhase(value=>['preparing','proof','checking'].includes(value)?'unknown':value);return()=>{generation.current++;control.current.abort();};},[revision]);
 useEffect(()=>{onBlockedChange(open||!!proof||!!original.current||!!candidate.current);},[open,proof,busy,phase,onBlockedChange]);
 useEffect(()=>{if(open&&!proof)document.getElementById(id+'-title')?.focus();},[open,phase,proof,id]);
 useEffect(()=>{
  if(!selection||flight.current||original.current||candidate.current)return;
  trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;
  candidate.current={requestId:crypto.randomUUID(),targetReference:selection.reference,expectedVersion:selection.version,lastSeenAt:selection.lastSeenAt};
  receipt.current=undefined;setTarget(selection);setPhase('review');setOpen(true);onSelectionHandled();
 },[selection,onSelectionHandled]);
 useEffect(()=>{
  if(!initial||seen.current.has(initial.requestId)||flight.current)return;
  own(initial);setOpen(true);const value=work();running(true);
  void (async()=>{try{await resume(initial,value);}catch(error){if(current(value)&&!authenticate(error))setPhase('unknown');}finally{if(current(value))running(false);}})();
 },[initial,revision]);
 async function result(value:SessionRevocationContext,scope:ReturnType<typeof work>):Promise<SessionRevocationResult>{
  const found=receipt.current?.requestId===value.requestId?receipt.current:await readSessionRevocationResult(csrfToken,value,scope.signal);
  if(current(scope)){receipt.current=found;setProof(undefined);setPhase('success');setOpen(true);}return found;
 }
 async function resume(value:SessionRevocationContext,scope:ReturnType<typeof work>){
  if(value.status==='CHANGED'){setPhase('changed');return;}
  if(value.status==='COMPLETED'){setPhase('checking');await result(value,scope);return;}
  setPhase('checking');const bootstrap=await loadStepUpOptions(scope.signal);if(!current(scope))return;
  if(bootstrap.operationReference!==value.operationReference)throw new SessionHttpError('unavailable');
  if(bootstrap.status==='COMPLETED'){await result(value,scope);return;}
  setPhase('proof');setProof(bootstrap);setOpen(false);
 }
 async function confirm(){
  const selected=candidate.current;if(!selected||flight.current)return;const scope=work();running(true);setPhase('preparing');seen.current.add(selected.requestId);
  try{const value=await prepareSessionRevocation(csrfToken,{requestId:selected.requestId,targetReference:selected.targetReference,expectedVersion:selected.expectedVersion},scope.signal);if(!current(scope))return;own(value);candidate.current=undefined;await resume(value,scope);}
  catch(error){if(current(scope)&&!authenticate(error))setPhase(!original.current&&error instanceof SessionHttpError&&error.kind==='changed'?'changed':'unknown');}finally{if(current(scope))running(false);}
 }
 async function check(){
  if(flight.current)return;const scope=work();running(true);setPhase('checking');setOpen(true);
  try{
   let value=original.current;
   if(!value){const recovered=await readSessionRevocationContext(scope.signal);if(!current(scope))return;const pending=candidate.current;
    if(!recovered||!pending||recovered.requestId!==pending.requestId||recovered.target.reference!==pending.targetReference||recovered.target.version!==pending.expectedVersion)throw new SessionHttpError('unavailable');
    own(recovered);candidate.current=undefined;value=recovered;
   }
   // No receipt is not a final failure: another native verification/commit may still finish.
   if(value.status==='CHANGED'){setPhase('changed');return;}await result(value,scope);
  }catch(error){if(current(scope)&&!authenticate(error))setPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function afterProof(outcome:StepUpCompletion){
  const value=original.current;if(!value||outcome.operationReference!==value.operationReference||outcome.signInRequired){setProof(undefined);setOpen(true);setPhase('unknown');return;}
  const scope=work();setProof(undefined);setOpen(true);running(true);setPhase('checking');
  try{await result(value,scope);}catch(error){if(current(scope)&&!authenticate(error))setPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function reconcile(signal:AbortSignal):Promise<StepUpCompletion|null>{
  const value=original.current;if(!value||signal.aborted)return null;const scope=work();
  try{const found=await readSessionRevocationResult(csrfToken,value,signal);if(signal.aborted||!current(scope))return null;receipt.current=found;return {status:'COMPLETED',signInRequired:false,operationReference:value.operationReference};}
  catch(error){if(!signal.aborted&&current(scope))authenticate(error);return null;}
 }
 function clear(){if(original.current)onAcknowledged(original.current.requestId);candidate.current=undefined;original.current=undefined;receipt.current=undefined;setOpen(false);setProof(undefined);setTarget(undefined);setPhase('review');}
 function close(){if(busy)return;if(phase==='review'){clear();return;}setOpen(false);if(phase==='success'){clear();onRefresh();}}
 function refresh(){if(busy)return;clear();onRefresh();}
 const feedback=phase==='success'?'success':phase==='changed'?'changed':phase==='review'||phase==='preparing'?'confirm':'unknown';
 return <ContentProvider locale={options.locale} audience="account-sessions" resolve={name=>options.content[name]}>
  {!open&&!proof&&(original.current||candidate.current)&&<div className="sessions-notice sessions-revocation-recovery" role="status"><p><ContentText contentKey={key(phase==='changed'?'changed.description':'unknown.description')}/></p><PrimaryButton appearance="outline" labelKey={key(phase==='changed'?'refresh':'check')} disabled={busy} onClick={()=>phase==='changed'?refresh():void check()}/></div>}
  {open&&!proof&&<ModalDialog labelledBy={id+'-title'} describedBy={id+'-description'} busy={busy} returnFocusTo={trigger.current} onRequestClose={close} className="passkeys-dialog sessions-revocation-dialog"><div className="sessions-revocation-content" data-tunner-component="COMP-SESSION-REVOKE">
   {phase==='success'&&<span className="passkeys-toast-mark" aria-hidden="true">✓</span>}<h2 id={id+'-title'} tabIndex={-1}><ContentText contentKey={key(feedback+'.title')}/></h2><p id={id+'-description'}><ContentText contentKey={key(feedback+'.description')}/></p>
   {target&&<div className="sessions-revocation-selected"><strong><ContentText contentKey="account.sessions.label" values={{reference:target.reference.slice(-8)}}/></strong><p><ContentText contentKey="account.sessions.lastActivity"/> {formatSessionTime(target.lastSeenAt,options.locale)}</p></div>}
   <p className="sessions-revocation-note"><ContentText contentKey="account.sessions.productBoundary"/></p>
   {phase==='unknown'&&<p className="sessions-revocation-note"><ContentText contentKey={key('unknown.help')}/></p>}
   {busy&&<p className="sessions-revocation-note" role="status"><ContentText contentKey={key(phase==='preparing'?'preparing':'checking')}/></p>}
   <div className="passkeys-dialog-actions"><PrimaryButton appearance="outline" labelKey={phase==='review'?key('cancel'):'account.sessions.back'} disabled={busy} onClick={close}/>{phase==='review'?<span className="passkeys-danger-wrap"><PrimaryButton labelKey={key('confirm.action')} disabled={busy} onClick={()=>void confirm()}/></span>:phase==='unknown'?<PrimaryButton labelKey={key('check')} disabled={busy} onClick={()=>void check()}/>:phase==='changed'?<PrimaryButton labelKey={key('refresh')} disabled={busy} onClick={refresh}/>:null}</div>
  </div></ModalDialog>}
  {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={reconcile} onComplete={outcome=>void afterProof(outcome)} onCancel={()=>{setProof(undefined);setPhase('unknown');setOpen(true);}}/>}
 </ContentProvider>;
}
