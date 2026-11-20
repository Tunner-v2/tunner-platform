import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {flushSync} from 'react-dom';
import {ContentProvider,ContentText,PrimaryButton} from '@tunner/ui';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion,StepUpDismissal} from './step-up';
import {loadRecoveryCodeOptions,readRecoveryCodeOperation,readRecoveryCodeStart,recoveryCodeDeliveryReceiver,requireRecoveryImpact,startRecoveryCodeChange,RecoveryCodeHttpError} from './recovery-codes.api';
import type {RecoveryCodeDeliveryReceiver,RecoveryCodeKind,RecoveryCodeOperation,RecoveryCodeOptions,RecoveryCodeStart,RecoveryCodeValues} from './recovery-codes.api';
import {RecoveryCodesCard,RecoveryCodesDialog} from './recovery-codes.ui';
import type {RecoveryCodesView} from './recovery-codes.ui';

type Work={signal:AbortSignal;revision:number};
interface Props {disabled:boolean;onBlockedChange:(blocked:boolean)=>void;fallbackFocus:()=>HTMLElement|null}

/** One owner controller within the existing Security slot. It creates no route or proof engine. */
export function RecoveryCodesSection({disabled,onBlockedChange,fallbackFocus}:Props){
 const control=useRef(new AbortController()),revision=useRef(0),alive=useRef(true),departed=useRef(false),flight=useRef(false);
 const original=useRef<RecoveryCodeOperation|undefined>(undefined),starting=useRef<RecoveryCodeStart|undefined>(undefined),acknowledged=useRef(new Set<string>());
 const values=useRef<RecoveryCodeValues|undefined>(undefined),downloadAttempted=useRef(false),downloadUrl=useRef<string|undefined>(undefined);
 const trigger=useRef<HTMLElement|null>(null),restore=useRef(false),card=useRef<HTMLDivElement>(null),blockedCallback=useRef(onBlockedChange);blockedCallback.current=onBlockedChange;
 const [boot,setBoot]=useState<RecoveryCodeOptions>(),[operation,setOperation]=useState<RecoveryCodeOperation>(),[busy,setBusy]=useState(true),[unavailable,setUnavailable]=useState(false);
 const [view,setView]=useState<RecoveryCodesView>(),[proof,setProof]=useState<StepUpBootstrap>(),[receiver,setReceiver]=useState<RecoveryCodeDeliveryReceiver>(),[error,setError]=useState<string>();
 const current=(work:Work)=>alive.current&&!departed.current&&!work.signal.aborted&&control.current.signal===work.signal&&revision.current===work.revision;
 const scope=():Work=>({signal:control.current.signal,revision:revision.current});
 function running(value:boolean){flight.current=value;if(value)blockedCallback.current(true);setBusy(value);}
 function own(value:RecoveryCodeOperation|undefined){original.current=value;setOperation(value);}
 function dispose(){values.current=undefined;if(downloadUrl.current){URL.revokeObjectURL(downloadUrl.current);downloadUrl.current=undefined;}}
 function rememberTrigger(){trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;}
 function notice(){setError('recoveryCodes.error.unavailable');}
 function failed(reason:unknown,work:Work){if(!current(work))return;if(reason instanceof RecoveryCodeHttpError&&reason.status===401){dispose();window.location.replace('/login');return;}setUnavailable(true);notice();}
 async function refresh(work:Work){
  const fresh=await loadRecoveryCodeOptions(work.signal);if(!current(work))return;setBoot(fresh);setUnavailable(false);
  if(!original.current&&!starting.current){const found=fresh.recovery;own(found&&!acknowledged.current.has(found.operationReference)?found:undefined);}
 }
 async function reload(){if(flight.current)return;const work=scope();running(true);setError(undefined);try{await refresh(work);}catch(reason){failed(reason,work);}finally{if(current(work))running(false);}}

 useEffect(()=>{
  alive.current=true;departed.current=false;control.current=new AbortController();revision.current++;const work=scope();running(true);
  void refresh(work).catch(reason=>failed(reason,work)).finally(()=>{if(current(work))running(false);});
  function leave(){
   departed.current=true;control.current.abort();revision.current++;dispose();flight.current=false;
   // BFCache may retain the component without React unmounting. Detach its volatile DOM now.
   flushSync(()=>{setView(undefined);setProof(undefined);setReceiver(undefined);setBusy(false);});
  }
  function returnFromCache(event:PageTransitionEvent){if(!event.persisted||!alive.current)return;departed.current=false;control.current=new AbortController();revision.current++;void reload();}
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returnFromCache);
  return()=>{alive.current=false;control.current.abort();dispose();window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returnFromCache);blockedCallback.current(false);};
 },[]);
 const pending=!!operation||!!starting.current,blocked=busy||pending||!!proof||!!view;
 useLayoutEffect(()=>{blockedCallback.current(blocked);},[blocked]);
 useLayoutEffect(()=>{
  if(!restore.current||busy||view||proof||disabled)return;
  const frame=requestAnimationFrame(()=>{if(!restore.current||flight.current||departed.current||!alive.current)return;restore.current=false;const target=trigger.current;
   if(target?.isConnected&&!target.matches(':disabled'))target.focus();else(card.current?.querySelector<HTMLElement>('button:not(:disabled)')??fallbackFocus())?.focus();});
  return()=>cancelAnimationFrame(frame);
 },[busy,view,proof,disabled,fallbackFocus]);

 async function freshFor(originalOperation:RecoveryCodeOperation,work:Work){
  const fresh=await loadRecoveryCodeOptions(work.signal);if(!current(work)||original.current!==originalOperation)throw new RecoveryCodeHttpError(0);
  requireRecoveryImpact(originalOperation,fresh);const admitted=fresh.recovery!;own(admitted);setBoot(fresh);setUnavailable(false);return{fresh,admitted};
 }
 function showSafe(result:RecoveryCodeOperation){
  own(result);setProof(undefined);setReceiver(undefined);
  if(result.status==='COMPLETED')setView(values.current?{kind:'reveal',codes:values.current,download:downloadAttempted.current?'unavailable':'available'}:{kind:'completed-without-payload'});
  else setView({kind:'unknown',canCheck:true});
 }
 async function launch(originalOperation:RecoveryCodeOperation,fresh:RecoveryCodeOptions,work:Work){
  requireRecoveryImpact(originalOperation,fresh);const context=await loadStepUpOptions(work.signal);if(!current(work)||original.current!==originalOperation)return;
  if(context.operationReference!==originalOperation.operationReference)throw new RecoveryCodeHttpError(0);
  if(context.status==='COMPLETED'){const result=await readRecoveryCodeOperation(fresh,originalOperation,work.signal);if(current(work)&&original.current===originalOperation)showSafe(result);return;}
  const receive=recoveryCodeDeliveryReceiver(originalOperation,context.requestId,()=>current(work)&&original.current===originalOperation,received=>{
   if(values.current!==undefined||!current(work)||original.current!==originalOperation)throw new RecoveryCodeHttpError(0);
   values.current=received;downloadAttempted.current=false;
  });
  setView(undefined);setReceiver(receive);setProof(context);
 }
 async function start(action:'generate'|'replace'){
  if(!boot||disabled||flight.current||original.current||starting.current)return;const kind:RecoveryCodeKind=action==='generate'?'GENERATE':'REPLACE';
  if(kind==='GENERATE'?!boot.canGenerate:!boot.canReplace)return;
  rememberTrigger();const work=scope();starting.current={ownerRequestId:boot.ownerRequestId,expectedVersion:boot.expectedVersion,kind};running(true);setError(undefined);dispose();
  try{const result=await startRecoveryCodeChange(boot,kind,work.signal);if(!current(work))return;own(result);starting.current=undefined;
   if(result.status!=='PENDING_PROOF'){showSafe(result);return;}
   const {fresh,admitted}=await freshFor(result,work);if(!current(work))return;
   if(kind==='REPLACE')setView({kind:'review',impactKey:admitted.impact.impactKey,canContinue:admitted.impact.canContinue});else await launch(admitted,fresh,work);
  }catch(reason){if(current(work)){setView({kind:'unknown',canCheck:true});failed(reason,work);}}finally{if(current(work))running(false);}
 }
 async function continueReview(){
  const retained=original.current;if(!retained||!boot||disabled||flight.current||retained.status!=='PENDING_PROOF')return;const work=scope();running(true);setError(undefined);
  try{const {fresh,admitted}=await freshFor(retained,work);await launch(admitted,fresh,work);}catch(reason){if(current(work)){notice();setView({kind:'review',impactKey:undefined,canContinue:false});failed(reason,work);}}finally{if(current(work))running(false);}
 }
 async function check(){
  if(!boot||flight.current||disabled)return;if(!view&&!proof)rememberTrigger();const work=scope();running(true);setError(undefined);
  try{const fresh=await loadRecoveryCodeOptions(work.signal);if(!current(work))return;setBoot(fresh);setUnavailable(false);
   let retained=original.current;
   if(!retained&&starting.current){retained=await readRecoveryCodeStart(fresh,starting.current,work.signal);if(!current(work))return;own(retained);starting.current=undefined;}
   if(!retained){if(fresh.recovery){retained=fresh.recovery;own(retained);}else{setView(undefined);return;}}
   const result=await readRecoveryCodeOperation(fresh,retained,work.signal);if(!current(work))return;own(result);
   if(result.status==='PENDING_PROOF'){const {fresh:published,admitted}=await freshFor(result,work);if(result.kind==='GENERATE')await launch(admitted,published,work);else if(current(work))setView({kind:'review',impactKey:admitted.impact.impactKey,canContinue:admitted.impact.canContinue});}else showSafe(result);
  }catch(reason){if(current(work)){setView({kind:'unknown',canCheck:true});failed(reason,work);}}finally{if(current(work))running(false);}
 }
 async function reconcile(signal:AbortSignal):Promise<StepUpCompletion|null>{
  const retained=original.current;if(!boot||!retained||signal.aborted)return null;const work=scope(),combined=AbortSignal.any([signal,work.signal]);
  const result=await readRecoveryCodeOperation(boot,retained,combined);if(!current(work)||combined.aborted)return null;
  return result.status==='COMPLETED'?{status:'COMPLETED',signInRequired:false,operationReference:retained.operationReference}:null;
 }
 function completed(outcome:StepUpCompletion){
  const retained=original.current;if(!retained||departed.current||!alive.current||outcome.operationReference!==retained.operationReference){dispose();throw new RecoveryCodeHttpError(0);}
  setProof(undefined);setReceiver(undefined);setError(undefined);
  if(outcome.signInRequired){dispose();window.location.replace('/login');return;}
  setView(values.current?{kind:'reveal',codes:values.current,download:'available'}:{kind:'completed-without-payload'});
 }
 async function finishPresentation(){
  const retained=original.current;if(retained)acknowledged.current.add(retained.operationReference);dispose();own(undefined);starting.current=undefined;setProof(undefined);setReceiver(undefined);setView(undefined);setError(undefined);restore.current=true;
  const work=scope();running(true);try{await refresh(work);}catch(reason){failed(reason,work);}finally{if(current(work))running(false);}
 }
 async function close(){
  if(flight.current)return;const retained=original.current;
  if(view?.kind==='reveal'||view?.kind==='completed-without-payload'||view?.kind==='already-presented'||retained&&retained.status!=='PENDING_PROOF'){await finishPresentation();return;}
  if(!retained||!boot){dispose();setView(undefined);setProof(undefined);setReceiver(undefined);restore.current=true;return;}
  const work=scope();running(true);setError(undefined);dispose();
  try{const result=await readRecoveryCodeOperation(boot,retained,work.signal,'cancel');if(!current(work))return;
   if(result.status==='REJECTED'||result.status==='EXPIRED'){await finishPresentation();return;}showSafe(result);
  }catch(reason){if(current(work)){setProof(undefined);setReceiver(undefined);setView({kind:'unknown',canCheck:true});failed(reason,work);}}finally{if(current(work))running(false);}
 }
 function dismissProof(outcome?:StepUpDismissal){
  dispose();setProof(undefined);setReceiver(undefined);
  if(outcome?.unresolved){setView({kind:'unknown',canCheck:true});return;}void close();
 }
 function download(){
  const held=values.current;if(!held||view?.kind!=='reveal'||view.download!=='available'||downloadAttempted.current||flight.current||departed.current)return;downloadAttempted.current=true;
  let anchor:HTMLAnchorElement|undefined;
  try{const blob=new Blob([held.join('\n')+'\n'],{type:'text/plain;charset=utf-8'}),url=URL.createObjectURL(blob);downloadUrl.current=url;
   anchor=document.createElement('a');anchor.href=url;anchor.download='tunner-backup-codes.txt';anchor.rel='noreferrer';anchor.hidden=true;document.body.append(anchor);anchor.click();setView({kind:'reveal',codes:held,download:'requested'});
  }catch{if(downloadUrl.current){URL.revokeObjectURL(downloadUrl.current);downloadUrl.current=undefined;}setView({kind:'reveal',codes:held,download:'unavailable'});}finally{anchor?.remove();}
 }

 if(!boot)return <section className="passkeys-card" aria-busy={busy}><h2><ContentText contentKey="passkeys.other.heading"/></h2><p><ContentText contentKey="passkeys.other.unavailable"/></p><div className="passkeys-page-actions"><PrimaryButton labelKey="passkeys.operation.refresh" appearance="outline" disabled={busy||disabled} onClick={()=>void reload()}/></div></section>;
 const actions:('generate'|'replace')[]=[...(boot.canGenerate?['generate' as const]:[]),...(boot.canReplace?['replace' as const]:[])];
 return <ContentProvider locale={boot.locale} audience="recovery-codes" resolve={key=>boot.content[key]}>
  <div ref={card}><RecoveryCodesCard available={!unavailable} actions={actions} busy={busy||disabled} pending={pending} onStart={action=>void start(action)} onCheck={()=>void check()} errorKey={view||proof?undefined:error}/></div>
  {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={reconcile} onCancel={dismissProof} onComplete={completed} {...(receiver?{recoveryCodeDelivery:receiver}:{})}/>}
  {view&&!proof&&<RecoveryCodesDialog view={view} busy={busy} returnFocusTo={trigger.current} errorKey={error} onClose={()=>void close()} onContinue={()=>void continueReview()} onCheck={()=>void check()} onDownload={download}/>}
 </ContentProvider>;
}
