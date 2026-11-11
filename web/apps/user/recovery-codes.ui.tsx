import {useId,useLayoutEffect,useRef,useState} from 'react';
import {ContentText,ModalDialog,PrimaryButton} from '@tunner/ui';
import './recovery-codes.css';

/** Presentation only. Current facts, eligibility and published content belong to the owner. */
export function RecoveryCodesCard({available,action,busy,onStart}:{available:boolean;action:'generate'|'replace'|undefined;busy:boolean;onStart:()=>void}){
 return <section className="passkeys-card recovery-codes-card" data-tunner-component="U-AUTH-06">
  <h2><ContentText contentKey="recoveryCodes.heading"/></h2>
  <p><ContentText contentKey={available?'recoveryCodes.card.description':'recoveryCodes.card.unavailable'}/></p>
  {action&&<div className="passkeys-page-actions"><PrimaryButton labelKey={'recoveryCodes.action.'+action} appearance="outline" disabled={busy||!available} onClick={onStart}/></div>}
 </section>;
}

export type RecoveryCodesView=
 | {kind:'review';impactKey:string|undefined;canContinue:boolean}
 | {kind:'reveal';codes:readonly [string,...string[]];download:'available'|'requested'|'unavailable'}
 | {kind:'unknown';canCheck:boolean}
 | {kind:'completed-without-payload'}
 | {kind:'already-presented'};

interface RecoveryCodesDialogProps {
 view:RecoveryCodesView;busy:boolean;returnFocusTo:HTMLElement|null;
 onClose:()=>void;onContinue:()=>void;onCheck:()=>void;onDownload:()=>void;
}

/** No HTTP, proof parsing, persistence, download transport or session/policy selection. */
export function RecoveryCodesDialog({view,busy,returnFocusTo,onClose,onContinue,onCheck,onDownload}:RecoveryCodesDialogProps){
 const titleId=useId(),descriptionId=useId(),heading=useRef<HTMLHeadingElement>(null);
 useLayoutEffect(()=>{heading.current?.focus({preventScroll:true});},[view.kind]);
 const stateKey=view.kind==='review'?'review':view.kind==='reveal'?'reveal':view.kind==='completed-without-payload'?'unavailable':view.kind==='already-presented'?'presented':'unknown';
 return <ModalDialog labelledBy={titleId} describedBy={descriptionId} busy={busy} returnFocusTo={returnFocusTo} onRequestClose={onClose} className="passkeys-dialog feedback recovery-codes-dialog">
  <div className="passkeys-dialog-content" data-tunner-component="U-AUTH-06">
   <h2 id={titleId} tabIndex={-1} ref={heading}><ContentText contentKey={'recoveryCodes.'+stateKey+'.heading'}/></h2>
   <p id={descriptionId} className="passkeys-dialog-description"><ContentText contentKey={'recoveryCodes.'+stateKey+'.description'}/></p>
   {view.kind==='review'?<>
    {view.impactKey?<section className="recovery-codes-impact"><h3><ContentText contentKey="recoveryCodes.review.details"/></h3><p><ContentText contentKey={view.impactKey}/></p></section>:<p className="passkeys-dialog-feedback"><ContentText contentKey="recoveryCodes.review.unavailable"/></p>}
    <div className="passkeys-dialog-actions"><PrimaryButton labelKey="recoveryCodes.action.cancel" appearance="outline" disabled={busy} onClick={onClose}/>{(!view.impactKey||!view.canContinue)&&<PrimaryButton labelKey="recoveryCodes.action.check" appearance="outline" disabled={busy} onClick={onCheck}/>}<PrimaryButton labelKey="recoveryCodes.action.continue" disabled={busy||!view.impactKey||!view.canContinue} aria-busy={busy} onClick={onContinue}/></div>
   </>:view.kind==='reveal'?<RecoveryCodesReveal view={view} busy={busy} onClose={onClose} onDownload={onDownload}/>:<div className="passkeys-dialog-actions">
    <PrimaryButton labelKey="recoveryCodes.action.close" appearance={view.kind==='unknown'?'outline':'filled'} disabled={busy} onClick={onClose}/>
    {view.kind==='unknown'&&<PrimaryButton labelKey="recoveryCodes.action.check" disabled={busy||!view.canCheck} aria-busy={busy} onClick={onCheck}/>}
   </div>}
  </div>
 </ModalDialog>;
}

function RecoveryCodesReveal({view,busy,onClose,onDownload}:{view:Extract<RecoveryCodesView,{kind:'reveal'}>;busy:boolean;onClose:()=>void;onDownload:()=>void}){
 const requested=useRef(false),[invoked,setInvoked]=useState(false),doneId=useId();
 function requestDownload(){
  if(busy||view.download!=='available'||requested.current)return;
  requested.current=true;setInvoked(true);onDownload();
  // The explicitly invoked control becomes disabled; retain useful keyboard focus.
  document.getElementById(doneId)?.focus({preventScroll:true});
 }
 return <>
  <p className="recovery-codes-once"><ContentText contentKey="recoveryCodes.reveal.once"/></p>
  <ul className="recovery-codes-grid">{view.codes.map((code,index)=><li key={index}><code>{code}</code></li>)}</ul>
  <p className="recovery-codes-warning"><ContentText contentKey="recoveryCodes.reveal.warning"/></p>
  <div role="status" aria-live="polite" aria-atomic="true" className="recovery-codes-download-status">{view.download==='requested'&&<><strong><ContentText contentKey="recoveryCodes.download.requested"/></strong><p><ContentText contentKey="recoveryCodes.download.description"/></p></>}</div>
  <div className="passkeys-dialog-actions">
   {view.download!=='unavailable'&&<PrimaryButton labelKey="recoveryCodes.action.download" appearance="outline" disabled={busy||invoked||view.download==='requested'} onClick={requestDownload}/>}
   <PrimaryButton id={doneId} labelKey="recoveryCodes.action.done" disabled={busy} onClick={onClose}/>
  </div>
 </>;
}
