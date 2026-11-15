import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FormErrorSummary,PrimaryButton,ThemeProvider,TunnerBrand,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './product-authorization.css';
import {followProductAuthorizationPath} from './product-authorization.continuation';
import {decideProductAuthorization,loadProductAuthorizationOptions,ProductAuthorizationHttpError,productAuthorizationContentValues,productAuthorizationErrorKey,productAuthorizationRoute} from './product-authorization.api';
import type {ProductAuthorizationDecision,ProductAuthorizationOptions} from './product-authorization.api';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpDismissal} from './step-up';
import type {StepUpProofReady} from './step-up.completion';

type ProductProofOriginal={operationReference:string;controller:AbortController;phase:'opening'|'dialog'|'refreshing'};

export function ProductAuthorizationApp(){return <ThemeProvider surface="user" mode="light"><GuardedReview/></ThemeProvider>;}
function GuardedReview(){
  const location=useRouteLocation();const currentRevision=useRef(location.revision);currentRevision.current=location.revision;
  return <Review key={location.revision} pathname={location.pathname} isCurrent={()=>currentRevision.current===location.revision&&window.location.pathname===location.pathname}/>;
}
function Review({pathname,isCurrent}:{pathname:string;isCurrent:()=>boolean}){
  const [options,setOptions]=useState<ProductAuthorizationOptions>();const [unavailable,setUnavailable]=useState(false);
  const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();const [blocked,setBlocked]=useState(false);
  const [proof,setProof]=useState<StepUpBootstrap>();const proofOriginal=useRef<ProductProofOriginal|undefined>(undefined);const proofTrigger=useRef<HTMLElement|null>(null);const restoreProofFocus=useRef(false);
  const lifecycle=useRef<AbortController|undefined>(undefined);const inFlight=useRef(false);const heading=useRef<HTMLHeadingElement>(null);const errorSummary=useRef<HTMLDivElement>(null);
  useEffect(()=>{const controller=new AbortController();lifecycle.current=controller;
    if(pathname!==productAuthorizationRoute.pathname){setUnavailable(true);return()=>controller.abort();}
    if(window.location.search||window.location.hash)navigatePath(productAuthorizationRoute.pathname,true);
    void loadProductAuthorizationOptions(controller.signal).then(value=>{if(!controller.signal.aborted&&isCurrent())setOptions(value);}).catch(()=>{if(!controller.signal.aborted&&isCurrent())setUnavailable(true);});
    return()=>{controller.abort();lifecycle.current=undefined;inFlight.current=false;proofOriginal.current=undefined;};
  },[pathname]);
  useEffect(()=>{if(options)heading.current?.focus();},[options]);
  useEffect(()=>{if(error)errorSummary.current?.focus();},[error]);
  useEffect(()=>{if(restoreProofFocus.current&&!proof&&!busy){restoreProofFocus.current=false;if(proofTrigger.current?.isConnected)proofTrigger.current.focus({preventScroll:true});}},[proof,busy]);
  function currentProof(original:ProductProofOriginal){return proofOriginal.current===original&&lifecycle.current===original.controller&&!original.controller.signal.aborted&&isCurrent();}
  async function readOriginalProof(original:ProductProofOriginal,signal:AbortSignal){
    const combined=AbortSignal.any([signal,original.controller.signal]);
    const fresh=await loadProductAuthorizationOptions(combined);
    if(combined.aborted||!currentProof(original))throw new Error('The original Product proof view is no longer current.');
    if(fresh.operationReference!==original.operationReference)throw new ProductAuthorizationHttpError(409,'VERSION_CONFLICT');
    return fresh;
  }
  async function beginProof(){
    const controller=lifecycle.current;
    if(!options||options.stepUpAvailable!==true||!options.operationReference||!controller||controller.signal.aborted||!isCurrent()||inFlight.current||blocked)return;
    const original:ProductProofOriginal={operationReference:options.operationReference,controller,phase:'opening'};proofOriginal.current=original;
    restoreProofFocus.current=false;
    proofTrigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;
    inFlight.current=true;setBusy(true);setError(undefined);let opened=false;
    try{
      const bootstrap=await loadStepUpOptions(controller.signal,original.operationReference);if(!currentProof(original))return;
      if(bootstrap.operationReference!==original.operationReference)throw new ProductAuthorizationHttpError(409,'VERSION_CONFLICT');
      if(bootstrap.status==='PROOF_READY'){
        // Shared proof readiness is never Product authorization. Read the original owner again and retain explicit Allow.
        original.phase='refreshing';
        const fresh=await readOriginalProof(original,controller.signal);if(currentProof(original))setOptions(fresh);
      }else if(bootstrap.status==='PENDING'){original.phase='dialog';setProof(bootstrap);opened=true;}
      else throw new ProductAuthorizationHttpError(409,'VERSION_CONFLICT');
    }catch(reason){if(currentProof(original)){setError(productAuthorizationErrorKey(reason));if(reason instanceof ProductAuthorizationHttpError&&reason.status===409)setBlocked(true);}}
    finally{if(currentProof(original)){if(!opened){proofOriginal.current=undefined;inFlight.current=false;}setBusy(false);}}
  }
  async function afterProof(outcome:StepUpProofReady){
    const original=proofOriginal.current;if(!original||!currentProof(original)||original.phase!=='dialog')return;
    original.phase='refreshing';
    setProof(undefined);setBusy(true);setError(undefined);
    try{
      if(outcome.status!=='PROOF_READY'||outcome.operationReference!==original.operationReference)throw new ProductAuthorizationHttpError(409,'VERSION_CONFLICT');
      const fresh=await readOriginalProof(original,original.controller.signal);if(currentProof(original))setOptions(fresh);
    }catch(reason){if(currentProof(original)){setError(productAuthorizationErrorKey(reason));setBlocked(true);}}
    finally{if(currentProof(original)){proofOriginal.current=undefined;inFlight.current=false;setBusy(false);}}
  }
  async function reconcileProof(signal:AbortSignal):Promise<StepUpProofReady|null>{
    const original=proofOriginal.current;if(!original||signal.aborted||!currentProof(original))return null;
    const combined=AbortSignal.any([signal,original.controller.signal]);const context=await loadStepUpOptions(combined,original.operationReference);
    if(combined.aborted||!currentProof(original))return null;
    if(context.operationReference!==original.operationReference||context.status==='COMPLETED')throw new ProductAuthorizationHttpError(409,'VERSION_CONFLICT');
    if(context.status!=='PROOF_READY')return null;
    const fresh=await readOriginalProof(original,combined);if(combined.aborted||!currentProof(original))return null;
    // Preserve an actual exact-owner proof-ready receipt; canAllow alone never establishes proof readiness or authorization.
    return fresh.canAllow?context:null;
  }
  function dismissProof(outcome?:StepUpDismissal){
    const original=proofOriginal.current;if(!original||!currentProof(original)||original.phase!=='dialog')return;
    // The shared dialog closes while this owner trigger is disabled; restore after the owner has reenabled it.
    restoreProofFocus.current=!outcome?.unresolved;
    setProof(undefined);proofOriginal.current=undefined;inFlight.current=false;setBusy(false);
    if(outcome?.unresolved)setError('authorization.unavailable');
  }
  function rejectCompletedProof(){
    const original=proofOriginal.current;if(!original||!currentProof(original)||original.phase!=='dialog')return;
    setProof(undefined);proofOriginal.current=undefined;inFlight.current=false;setBusy(false);setBlocked(true);setError('authorization.changed');
  }
  async function decide(decision:ProductAuthorizationDecision){
    const controller=lifecycle.current;if(!options||!controller||controller.signal.aborted||!isCurrent()||inFlight.current||blocked||decision==='ALLOW'&&!options.canAllow)return;
    const original=options;inFlight.current=true;setBusy(true);setError(undefined);let navigating=false;
    try{const response=await decideProductAuthorization(original,decision,controller.signal);if(controller.signal.aborted||lifecycle.current!==controller||!isCurrent())return;await followProductAuthorizationPath(response.resumePath,controller.signal);navigating=true;setBlocked(true);}
    catch(reason){if(controller.signal.aborted||lifecycle.current!==controller||!isCurrent())return;setError(productAuthorizationErrorKey(reason));setBlocked(true);}
    finally{if(!navigating&&!controller.signal.aborted&&lifecycle.current===controller){inFlight.current=false;setBusy(false);}}
  }
  if(unavailable)return <ContentUnavailable/>;
  if(!options)return <main aria-busy="true"/>;
  const variableText=(key:string)=>{const values=productAuthorizationContentValues(options.content,key,options.productName);return values?<ContentText contentKey={key} values={values}/>:<ContentText contentKey={key}/>;};
  const interactionKey=options.interactionStatus==='RequiresFreshProof'?'authorization.proofRequired':options.interactionStatus==='RequiresSignIn'?'authorization.loginRequired':options.interactionStatus==='Denied'?'authorization.unavailable':undefined;
  return <ContentProvider locale={options.locale} audience="product-authorization" resolve={key=>options.content[key]}><div className="product-authorization-page">
    <img className="product-authorization-glow indigo" src="/registration/assets/product-origin-indigo.svg" alt=""/><img className="product-authorization-glow violet" src="/registration/assets/product-origin-violet.svg" alt=""/>
    <header className="product-authorization-header"><TunnerBrand contentKey="brand.tunner"/></header>
    <main className="product-authorization-stage"><section className="product-authorization-card" data-tunner-screen="U-AUTH-08" aria-labelledby="product-authorization-heading" aria-busy={busy}>
      <div className="product-authorization-intro"><h1 id="product-authorization-heading" ref={heading} tabIndex={-1}>{variableText('authorization.heading')}</h1><p><ContentText contentKey="authorization.description"/></p></div>
      <div className="product-authorization-account"><span className="product-authorization-avatar" aria-hidden="true">{Array.from(options.accountLabel)[0]}</span><div><strong>{options.accountLabel}</strong><p><ContentText contentKey="authorization.account"/></p></div></div>
      <aside className="product-authorization-sharing"><strong>{variableText('authorization.permissions')}</strong><ContentProvider locale={options.locale} audience="product-authorization-scopes" resolve={key=>options.scopeDescriptions[key]}><ul>{Object.keys(options.scopeDescriptions).map(key=><li key={key}><ContentText contentKey={key}/></li>)}</ul></ContentProvider></aside>
      <dl className="product-authorization-details"><div><dt><ContentText contentKey="authorization.callback"/></dt><dd>{options.registeredReturnOrigin}</dd></div>{options.requestedAssuranceLabel&&<div><dt><ContentText contentKey="authorization.assurance"/></dt><dd>{options.requestedAssuranceLabel}</dd></div>}</dl>
      {interactionKey&&<p className="product-authorization-status" role="status"><ContentText contentKey={interactionKey}/></p>}
      <div className="product-authorization-decisions"><PrimaryButton labelKey="authorization.deny" appearance="outline" disabled={busy||blocked||!!proof} onClick={()=>void decide('DENY')}/><PrimaryButton labelKey="authorization.allow" disabled={busy||blocked||!!proof||!options.canAllow} aria-busy={busy} onClick={()=>void decide('ALLOW')}/></div>
      <button type="button" className="product-authorization-switch" disabled={busy||blocked||!!proof} onClick={()=>void decide('SWITCH_ACCOUNT')}><ContentText contentKey="authorization.switchAccount"/></button>
      <p className="product-authorization-boundary">{variableText('authorization.boundary')}</p>
      {options.stepUpAvailable===true&&<PrimaryButton labelKey="authorization.verify" disabled={busy||blocked||!!proof} aria-busy={busy} onClick={()=>void beginProof()}/>}
      {busy&&<p className="product-authorization-status" role="status"><ContentText contentKey="authorization.working"/></p>}
      {error&&<div ref={errorSummary} tabIndex={-1}><FormErrorSummary contentKey={error}/></div>}
    </section></main>
    <footer className="product-authorization-footer"><a href={options.privacyUri}><ContentText contentKey="authorization.footer.privacy"/></a><img src="/registration/assets/product-origin-footer-dot.svg" alt=""/><a href={options.termsUri}><ContentText contentKey="authorization.footer.terms"/></a>{options.supportUri&&<><img src="/registration/assets/product-origin-footer-dot.svg" alt=""/><a href={options.supportUri}><ContentText contentKey="authorization.footer.support"/></a></>}</footer>
    {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={proofTrigger.current} reconcile={reconcileProof} onProofReady={outcome=>void afterProof(outcome)} onComplete={rejectCompletedProof} onCancel={dismissProof}/>}
  </div></ContentProvider>;
}
createRoot(document.getElementById('root')!).render(<ProductAuthorizationApp/>);
