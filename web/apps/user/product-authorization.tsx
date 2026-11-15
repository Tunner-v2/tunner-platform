import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FormErrorSummary,PrimaryButton,ThemeProvider,TunnerBrand,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './product-authorization.css';
import {followProductAuthorizationPath} from './product-authorization.continuation';
import {decideProductAuthorization,loadProductAuthorizationOptions,productAuthorizationContentValues,productAuthorizationErrorKey,productAuthorizationRoute} from './product-authorization.api';
import type {ProductAuthorizationDecision,ProductAuthorizationOptions} from './product-authorization.api';

export function ProductAuthorizationApp(){return <ThemeProvider surface="user" mode="light"><GuardedReview/></ThemeProvider>;}
function GuardedReview(){
  const location=useRouteLocation();const currentRevision=useRef(location.revision);currentRevision.current=location.revision;
  return <Review key={location.revision} pathname={location.pathname} isCurrent={()=>currentRevision.current===location.revision&&window.location.pathname===location.pathname}/>;
}
function Review({pathname,isCurrent}:{pathname:string;isCurrent:()=>boolean}){
  const [options,setOptions]=useState<ProductAuthorizationOptions>();const [unavailable,setUnavailable]=useState(false);
  const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();const [blocked,setBlocked]=useState(false);
  const lifecycle=useRef<AbortController|undefined>(undefined);const inFlight=useRef(false);const heading=useRef<HTMLHeadingElement>(null);const errorSummary=useRef<HTMLDivElement>(null);
  useEffect(()=>{const controller=new AbortController();lifecycle.current=controller;
    if(pathname!==productAuthorizationRoute.pathname){setUnavailable(true);return()=>controller.abort();}
    if(window.location.search||window.location.hash)navigatePath(productAuthorizationRoute.pathname,true);
    void loadProductAuthorizationOptions(controller.signal).then(value=>{if(!controller.signal.aborted&&isCurrent())setOptions(value);}).catch(()=>{if(!controller.signal.aborted&&isCurrent())setUnavailable(true);});
    return()=>{controller.abort();lifecycle.current=undefined;inFlight.current=false;};
  },[pathname]);
  useEffect(()=>{if(options)heading.current?.focus();},[options]);
  useEffect(()=>{if(error)errorSummary.current?.focus();},[error]);
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
      <div className="product-authorization-decisions"><PrimaryButton labelKey="authorization.deny" appearance="outline" disabled={busy||blocked} onClick={()=>void decide('DENY')}/><PrimaryButton labelKey="authorization.allow" disabled={busy||blocked||!options.canAllow} aria-busy={busy} onClick={()=>void decide('ALLOW')}/></div>
      <button type="button" className="product-authorization-switch" disabled={busy||blocked} onClick={()=>void decide('SWITCH_ACCOUNT')}><ContentText contentKey="authorization.switchAccount"/></button>
      <p className="product-authorization-boundary">{variableText('authorization.boundary')}</p>
      {busy&&<p className="product-authorization-status" role="status"><ContentText contentKey="authorization.working"/></p>}
      {error&&<div ref={errorSummary} tabIndex={-1}><FormErrorSummary contentKey={error}/></div>}
    </section></main>
    <footer className="product-authorization-footer"><a href={options.privacyUri}><ContentText contentKey="authorization.footer.privacy"/></a><img src="/registration/assets/product-origin-footer-dot.svg" alt=""/><a href={options.termsUri}><ContentText contentKey="authorization.footer.terms"/></a>{options.supportUri&&<><img src="/registration/assets/product-origin-footer-dot.svg" alt=""/><a href={options.supportUri}><ContentText contentKey="authorization.footer.support"/></a></>}</footer>
  </div></ContentProvider>;
}
createRoot(document.getElementById('root')!).render(<ProductAuthorizationApp/>);
