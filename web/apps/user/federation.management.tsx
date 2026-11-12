import {useEffect,useId,useLayoutEffect,useRef,useState} from 'react';
import {ContentText,FormErrorSummary,ModalDialog,PrimaryButton} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import {changeFederationManagement,federationUnlinkPublication,loadFederationManagement,managementChallengePath,managementErrorKey,parseManagementOperation,readFederationManagement,startFederationManagement,FederationManagementError} from './federation.management.api';
import type {FederationManagedLink,FederationManagementOperation,FederationManagementSettings,FederationManagementStart} from './federation.management.api';

export const federationManagementKeys=['heading','description','google','microsoft','linked','unlinked','added','linkGoogle','linkMicrosoft','relink','unlink','unlinkBlocked','linkHeading','linkDescription','unlinkHeading','unlinkDescription','confirmLink','confirmUnlink','cancel','close','check','review','continue','pending','unknown','notCompleted','notCompletedDescription','completed','completedDescription','detailsUnavailable','changed','limited','unavailable'].map(key=>'federation.management.'+key);
type Publications={content:Record<string,PublishedContent>;locale:string};
interface Props extends Publications{disabled:boolean;standalone:boolean;isCurrent:()=>boolean;onBlockedChange:(blocked:boolean)=>void;fallbackFocus:()=>HTMLElement|null;refreshPublications:(signal:AbortSignal)=>Promise<Publications>}
const terminal=(op:FederationManagementOperation)=>['COMPLETED','REJECTED','EXPIRED'].includes(op.status);
const reference=(op:FederationManagementOperation)=>op.operationId.replaceAll('-','');
function publicationIdentity(value:PublishedContent|null){return value?JSON.stringify([value.key,value.locale,value.variantRef,value.versionRef,value.publicationRef,value.text]):null;}

/** The protected settings selector restores one exact operation. Local state supplies no eligibility or target authority. */
export function FederationManagement(props:Props){
 const latest=useRef(props);latest.current=props;
 const alive=useRef(true),departed=useRef(false),control=useRef(new AbortController()),flight=useRef(false);
 const original=useRef<FederationManagementStart|FederationManagementOperation|undefined>(undefined),acknowledged=useRef(new Set<string>());
 const trigger=useRef<HTMLElement|null>(null),heading=useRef<HTMLHeadingElement>(null),panel=useRef<HTMLElement>(null),restore=useRef(false);const dialogId=useId();
 const [settings,setSettings]=useState<FederationManagementSettings>(),[operation,setOperation]=useState<FederationManagementOperation>(),[busy,setBusy]=useState(true),[modal,setModal]=useState(false),[uncertain,setUncertain]=useState(false),[proof,setProof]=useState<StepUpBootstrap>(),[error,setError]=useState<string>();
 const current=(signal=control.current.signal)=>alive.current&&!departed.current&&!signal.aborted&&control.current.signal===signal&&latest.current.isCurrent();
 function running(value:boolean){flight.current=value;setBusy(value);if(value)latest.current.onBlockedChange(true);}
 function own(op:FederationManagementOperation|undefined){original.current=op;setOperation(op);}
 function failed(reason:unknown,signal=control.current.signal){if(!current(signal))return;if(reason instanceof FederationManagementError&&reason.status===401){window.location.replace('/login');return;}setUncertain(true);setError(managementErrorKey(reason));setProof(undefined);}
 async function refresh(signal:AbortSignal){
  const value=await loadFederationManagement(signal);if(!current(signal))return;setSettings(value);
  if(value.operation&&!acknowledged.current.has(value.operation.operationId)){
   const selected=original.current?parseManagementOperation(value.operation,original.current):value.operation;
   own(selected);setUncertain(selected.status==='UNKNOWN'||selected.errorCode!==null);setModal(!latest.current.disabled);
  }else if(original.current){setUncertain(true);}
 }
 useEffect(()=>{
  alive.current=true;departed.current=false;control.current=new AbortController();const signal=control.current.signal;running(true);
  void refresh(signal).catch(reason=>failed(reason,signal)).finally(()=>{if(current(signal))running(false);});
  const leave=()=>{departed.current=true;control.current.abort();flight.current=false;setProof(undefined);setModal(false);};
  const returned=(event:PageTransitionEvent)=>{if(!event.persisted||!alive.current)return;departed.current=false;control.current=new AbortController();const signal=control.current.signal;running(true);void refresh(signal).catch(reason=>failed(reason,signal)).finally(()=>{if(current(signal))running(false);});};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);
  return()=>{alive.current=false;control.current.abort();window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);latest.current.onBlockedChange(false);};
 },[]);
 const blocked=busy||!!original.current||modal||!!proof;
 useLayoutEffect(()=>{latest.current.onBlockedChange(blocked);},[blocked]);
 useEffect(()=>{if(modal&&!proof)heading.current?.focus();},[modal,proof,operation?.status]);
 useLayoutEffect(()=>{if(!restore.current||busy||modal||proof||props.disabled)return;restore.current=false;const target=trigger.current;((target?.isConnected&&!target.matches(':disabled'))?target:panel.current?.querySelector<HTMLElement>('button:not(:disabled)')??latest.current.fallbackFocus())?.focus();},[busy,modal,proof,props.disabled]);
 async function accept(op:FederationManagementOperation,signal:AbortSignal){
  if(!current(signal))return;own(op);setUncertain(op.status==='UNKNOWN'||op.errorCode!==null);setProof(undefined);setModal(true);
  if(op.status==='COMPLETED'){const value=await loadFederationManagement(signal);if(current(signal))setSettings(value);}
 }
 async function check(){
  const held=original.current;if(!held||flight.current||props.disabled)return;const signal=control.current.signal;running(true);setError(undefined);
  try{const op=await readFederationManagement(held,signal);if(current(signal)&&original.current===held)await accept(op,signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 async function reload(){if(flight.current||props.disabled)return;const signal=control.current.signal;running(true);setError(undefined);try{await refresh(signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}}
 async function start(kind:'LINK'|'UNLINK',provider:'Google'|'Microsoft',link?:FederationManagedLink){
  if(!settings||flight.current||original.current||props.disabled||!current())return;
  if(kind==='LINK'&&(!settings.canStartLink||!(provider==='Google'?settings.googleAvailable:settings.microsoftAvailable)||link&&link.state!=='UNLINKED')||kind==='UNLINK'&&(!link||link.state!=='ACTIVE'||!link.canStartUnlink))return;
  trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;
  const held:FederationManagementStart=Object.freeze({requestId:crypto.randomUUID(),kind,provider,targetLinkReference:link?.linkReference??null,expectedLinkVersion:link?.version??0,expectedAccountVersion:settings.accountVersion});
  original.current=held;setOperation(undefined);setModal(true);setUncertain(true);setError(undefined);running(true);const signal=control.current.signal;
  try{const result=await startFederationManagement(held,signal);if(!current(signal)||original.current!==held)return;own(result.operation);
   if(kind==='LINK'){const path=managementChallengePath(held,result.operation,result.challengePath);window.location.assign(path);}
   else{if(result.challengePath!==undefined)throw new FederationManagementError();await accept(result.operation,signal);}
  }catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 async function reconcile(signal:AbortSignal):Promise<StepUpCompletion|null>{
  const held=original.current;if(!held||!('operationId'in held)||signal.aborted)return null;const combined=AbortSignal.any([signal,control.current.signal]);
  const op=await readFederationManagement(held,combined);if(signal.aborted||!current()||original.current!==held)return null;
  return op.status==='COMPLETED'&&op.errorCode===null?{status:'COMPLETED',signInRequired:false,operationReference:reference(op)}:null;
 }
 async function launchProof(op:FederationManagementOperation,signal:AbortSignal){
  if(!op.canRequestProof||op.errorCode!==null||op.status!=='PENDING_PROOF')throw new FederationManagementError(409,'CHANGED');
  const context=await loadStepUpOptions(signal);if(!current(signal))return;
  if(context.operationReference!==reference(op))throw new FederationManagementError(409,'CHANGED');
  if(context.status==='COMPLETED'){await accept(await readFederationManagement(op,signal),signal);return;}
  setModal(false);setProof(context);
 }
 async function confirm(){
  const held=original.current;if(!held||!('operationId'in held)||flight.current||props.disabled||uncertain||!held.canConfirm)return;
  const oldImpact=federationUnlinkPublication(held,props.content,props.locale);if(held.kind==='UNLINK'&&!oldImpact)return;
  if(held.kind==='LINK'&&!held.providerVerified)return;
  const signal=control.current.signal;running(true);setError(undefined);
  try{
   const value=await loadFederationManagement(signal);if(!current(signal)||original.current!==held)return;
   if(!value.operation)throw new FederationManagementError(409,'CHANGED');const admitted=parseManagementOperation(value.operation,held);setSettings(value);
   const publications=await latest.current.refreshPublications(signal);if(!current(signal)||original.current!==held)return;
   const impact=federationUnlinkPublication(admitted,publications.content,publications.locale);
   if(!admitted.canConfirm||admitted.errorCode!==null||admitted.kind==='LINK'&&!admitted.providerVerified||admitted.kind==='UNLINK'&&(!impact||publicationIdentity(oldImpact)!==publicationIdentity(impact))){own(admitted);setError('federation.management.changed');return;}
   const result=await changeFederationManagement('confirm',admitted,signal);if(!current(signal)||original.current!==held)return;
   await accept(result,signal);if(current(signal)&&result.canRequestProof)await launchProof(result,signal);
  }catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 async function resumeProof(){const held=original.current;if(!held||!('operationId'in held)||flight.current||props.disabled||uncertain)return;const signal=control.current.signal;running(true);setError(undefined);try{const op=await readFederationManagement(held,signal);if(!current(signal)||original.current!==held)return;own(op);await launchProof(op,signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}}
 async function finish(){
  const held=original.current;if(!held||!('operationId'in held)||!terminal(held)||flight.current)return;acknowledged.current.add(held.operationId);own(undefined);setModal(false);setProof(undefined);setError(undefined);setUncertain(false);restore.current=true;const signal=control.current.signal;running(true);try{await refresh(signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 async function cancel(){
  const held=original.current;if(flight.current)return;if(held&&'operationId'in held&&terminal(held)){await finish();return;}
  if(!held||!('operationId'in held)||!held.canCancel||props.disabled){setModal(false);setProof(undefined);restore.current=true;return;}
  const signal=control.current.signal;running(true);setError(undefined);setProof(undefined);
  try{const op=await changeFederationManagement('cancel',held,signal);if(!current(signal)||original.current!==held)return;await accept(op,signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 async function proofCompleted(outcome:StepUpCompletion,signal:AbortSignal){
  if(!current(signal))return;const held=original.current;if(!held||!('operationId'in held)||outcome.operationReference!==reference(held))throw new FederationManagementError();
  setProof(undefined);if(outcome.signInRequired){window.location.replace('/login');return;}running(true);setError(undefined);try{await accept(await readFederationManagement(held,signal),signal);}catch(reason){failed(reason,signal);}finally{if(current(signal))running(false);}
 }
 const held=operation??original.current,providerKey=held?.provider==='Microsoft'?'federation.management.microsoft':'federation.management.google';
 const providerText=props.content[providerKey]?.text;
 const impact=operation?federationUnlinkPublication(operation,props.content,props.locale):null;
 const selected=operation?.targetLinkReference?settings?.links.find(link=>link.linkReference===operation.targetLinkReference):undefined;
 const canConfirm=!!operation&&!uncertain&&operation.errorCode===null&&operation.canConfirm&&(operation.kind==='UNLINK'?!!impact:operation.providerVerified);
 const done=!!operation&&terminal(operation),success=operation?.status==='COMPLETED'&&operation.errorCode===null;
 const proofSignal=control.current.signal;
 return <section ref={panel} className={props.standalone?'passkeys-card federation-management':'federation-management'} data-tunner-component="COMP-FEDERATION-MANAGEMENT">
  <h2><ContentText contentKey="federation.management.heading"/></h2><p className="passkeys-card-intro"><ContentText contentKey="federation.management.description"/></p>
  {settings?.links.map(link=><div key={link.linkReference} className="passkeys-method-row" role="group" aria-label={props.content[link.provider==='Google'?'federation.management.google':'federation.management.microsoft']?.text}>
   <div><p className="passkeys-method-title"><ContentText contentKey={link.provider==='Google'?'federation.management.google':'federation.management.microsoft'}/></p>{link.linkedAt&&<p className="passkeys-method-detail"><ContentText contentKey="federation.management.added"/> <time dateTime={link.linkedAt}>{new Intl.DateTimeFormat(props.locale,{year:'numeric',month:'short',day:'2-digit',timeZone:'UTC'}).format(new Date(link.linkedAt))}</time></p>}{link.state==='ACTIVE'&&!link.canStartUnlink&&<p className="passkeys-method-detail"><ContentText contentKey="federation.management.unlinkBlocked"/></p>}</div>
   <span className={'passkeys-pill'+(link.state==='UNLINKED'?' revoked':'')}><ContentText contentKey={link.state==='ACTIVE'?'federation.management.linked':'federation.management.unlinked'}/></span>
   <PrimaryButton labelKey={link.state==='ACTIVE'?'federation.management.unlink':'federation.management.relink'} appearance="outline" disabled={blocked||props.disabled||(link.state==='ACTIVE'?!link.canStartUnlink:!settings?.canStartLink||!(link.provider==='Google'?settings?.googleAvailable:settings?.microsoftAvailable))} onClick={()=>void start(link.state==='ACTIVE'?'UNLINK':'LINK',link.provider,link)}/>
  </div>)}
  <div className="passkeys-page-actions">{settings&&(['Google','Microsoft'] as const).filter(provider=>provider==='Google'?settings.googleAvailable:settings.microsoftAvailable).map(provider=><PrimaryButton key={provider} labelKey={provider==='Google'?'federation.management.linkGoogle':'federation.management.linkMicrosoft'} appearance="outline" disabled={blocked||props.disabled||!settings.canStartLink} onClick={()=>void start('LINK',provider)}/>)}
   {original.current&&<PrimaryButton labelKey="federation.management.check" appearance="outline" disabled={busy||props.disabled} onClick={()=>void check()}/>}{operation&&<PrimaryButton labelKey="federation.management.review" appearance="outline" disabled={busy||props.disabled} onClick={()=>setModal(true)}/>}{!settings&&<PrimaryButton labelKey="passkeys.operation.refresh" appearance="outline" disabled={busy||props.disabled} onClick={()=>void reload()}/>}
  </div>
  {!settings&&<p role="status"><ContentText contentKey={busy?'federation.management.pending':'federation.management.unavailable'}/></p>}{!modal&&!proof&&error&&<FormErrorSummary contentKey={error}/>}
  {modal&&!proof&&<ModalDialog labelledBy={dialogId+'-heading'} describedBy={dialogId+'-description'} busy={busy} returnFocusTo={trigger.current} onRequestClose={()=>void cancel()} className="passkeys-dialog federation-management-dialog"><div className="passkeys-dialog-content">
   <h2 id={dialogId+'-heading'} ref={heading} tabIndex={-1}><ContentText contentKey={success?'federation.management.completed':done?'federation.management.notCompleted':held?.kind==='UNLINK'?'federation.management.unlinkHeading':'federation.management.linkHeading'}/></h2>
   <p id={dialogId+'-description'} className="passkeys-dialog-description"><ContentText contentKey={success?'federation.management.completedDescription':done?'federation.management.notCompletedDescription':held?.kind==='UNLINK'?'federation.management.unlinkDescription':'federation.management.linkDescription'}/></p>
   {providerText&&<div className="federation-management-provider"><ContentText contentKey={providerKey}/>{selected?.linkedAt&&<p className="passkeys-method-detail"><ContentText contentKey="federation.management.added"/> <time dateTime={selected.linkedAt}>{new Intl.DateTimeFormat(props.locale,{year:'numeric',month:'short',day:'2-digit',timeZone:'UTC'}).format(new Date(selected.linkedAt))}</time></p>}</div>}
   {operation?.kind==='UNLINK'&&!done&&<p className="passkeys-impact"><ContentText contentKey={impact?.key??'federation.management.detailsUnavailable'}/></p>}
   {!busy&&(uncertain||!operation)&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey="federation.management.unknown"/></p>}{busy&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey="federation.management.pending"/></p>}{error&&<FormErrorSummary contentKey={error}/>}
   <div className="passkeys-dialog-actions"><PrimaryButton labelKey={!done&&operation?.canCancel?'federation.management.cancel':'federation.management.close'} appearance="outline" disabled={busy} onClick={()=>void cancel()}/>
    {!done&&canConfirm&&<PrimaryButton labelKey={operation?.kind==='UNLINK'?'federation.management.confirmUnlink':'federation.management.confirmLink'} disabled={busy||props.disabled} onClick={()=>void confirm()}/>}
    {!done&&operation?.canConfirm&&!canConfirm&&<PrimaryButton labelKey={operation.kind==='UNLINK'?'federation.management.confirmUnlink':'federation.management.confirmLink'} disabled/>}
    {!done&&operation?.canRequestProof&&!uncertain&&<PrimaryButton labelKey="federation.management.continue" disabled={busy||props.disabled} onClick={()=>void resumeProof()}/>}
    {!done&&(!canConfirm||uncertain)&&<PrimaryButton labelKey="federation.management.check" appearance="outline" disabled={busy||props.disabled} onClick={()=>void check()}/>}
   </div>
  </div></ModalDialog>}
  {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={reconcile} onComplete={outcome=>{void proofCompleted(outcome,proofSignal).catch(reason=>failed(reason,proofSignal));}} onCancel={outcome=>{if(!current(proofSignal))return;setProof(undefined);setModal(true);if(outcome?.unresolved)setUncertain(true);else void cancel();}}/>}
 </section>;
}
