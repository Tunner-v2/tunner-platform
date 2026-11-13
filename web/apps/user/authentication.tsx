import { createRoot } from 'react-dom/client';
import { useEffect, useRef, useState } from 'react';
import { ContentProvider, ContentText, ContentUnavailable, FormErrorSummary, PrimaryButton, TextField, ThemeProvider, TunnerBrand, navigatePath, useRouteLocation, useTunnerForm } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import '@tunner/ui/styles.css';
import './authentication.css';
import { passwordSignInSchema } from './authentication.validation';
import { authenticationRoutes } from './authentication.routes';
import {totpRoutes} from './totp.routes';
import {TotpMfaScreen} from './totp.mfa';
import {loadTotpMfaOptions,parseMfaRequired} from './totp.api';
import { loginPasskeyKeys, PasskeySignIn } from './passkeys.sign-in';
import { parseAuthenticationPasskeyRecovery, requirePublished } from './passkeys.api';
import {FederationSignIn,federationSignInKeys} from './federation.sign-in';
import {parseFederationBootstrap} from './federation.api';
import {recoveryRoutes} from './recovery.routes';
interface Bootstrap {csrfToken:string;idempotencyKey:string;passwordMaximumLength:number;locale:string;content:Record<string,PublishedContent>;termsUri:string;privacyUri:string;supportUri:string|null;passkeyAvailable?:boolean;maximumCredentialJsonBytes?:number|null;passkeyRecovery?:unknown;federation?:unknown;recoveryAvailable?:boolean}
interface Session {authenticated:true;csrfToken:string}
const errors:Readonly<Record<string,string>>={AUTHENTICATION_FAILED:'authentication.error.credentials',RATE_OR_ABUSE_LIMITED:'authentication.error.limited'};
async function options(signal?:AbortSignal):Promise<Bootstrap>{const response=await fetch('/authentication/options',{credentials:'same-origin',cache:'no-store',redirect:'error',...(signal?{signal}:{})});if(!response.ok)throw new Error('Unavailable');return response.json() as Promise<Bootstrap>;}
function App(){const location=useRouteLocation();return location.pathname===totpRoutes.mfa.pathname?<ThemeProvider surface="user" mode="light"><TotpMfaScreen key={location.revision}/></ThemeProvider>:<AuthenticationApp/>;}
function AuthenticationApp(){
  const [boot,setBoot]=useState<Bootstrap>();const [unavailable,setUnavailable]=useState(false);
  useEffect(()=>{const controller=new AbortController();void options(controller.signal).then(setBoot).catch(()=>{if(!controller.signal.aborted)setUnavailable(true);});return()=>controller.abort();},[]);
  if(unavailable)return <ContentUnavailable/>;if(!boot)return <main aria-busy="true"/>;
  return <ThemeProvider surface="user" mode="light"><Screens initial={boot}/></ThemeProvider>;
}
function Screens({initial}:{initial:Bootstrap}){
  const location=useRouteLocation();const [boot,setBoot]=useState(initial);const [session,setSession]=useState<Session>();const [guard,setGuard]=useState(true);
  const [readyRevision,setReadyRevision]=useState(-1);const [entryUnavailable,setEntryUnavailable]=useState(false);
  const routeRevision=useRef(location.revision);routeRevision.current=location.revision;
  const attemptRef=useRef<symbol|undefined>(undefined);const attemptController=useRef<AbortController|undefined>(undefined);
  const [email,setEmail]=useState('');const [password,setPassword]=useState('');const [remember,setRemember]=useState(false);const [visible,setVisible]=useState(false);
  const [error,setError]=useState<string>();const [logoutBusy,setLogoutBusy]=useState(false);const logoutPending=useRef(false);const heading=useRef<HTMLHeadingElement>(null);
  const [passkeyBusy,setPasskeyBusy]=useState(false);const passkeyPending=useRef(false);
  const [federationBusy,setFederationBusy]=useState(false);const federationPending=useRef(false);
  const form=useTunnerForm(passwordSignInSchema(boot.passwordMaximumLength),{email,password,remember});
  const show=boot.content[visible?'login.hide':'login.show']!;const unavailable=boot.content['login.unavailable']!;
  const signedIn=location.pathname===authenticationRoutes.signedIn.pathname;const guarded=guard||readyRevision!==location.revision;
  useEffect(()=>{
    const controller=new AbortController();let active=true;const finish=()=>{if(active){setReadyRevision(location.revision);setGuard(false);}};
    attemptRef.current=undefined;attemptController.current?.abort();attemptController.current=undefined;
    setPassword('');setVisible(false);setError(undefined);setSession(undefined);setGuard(true);setEntryUnavailable(false);passkeyPending.current=false;setPasskeyBusy(false);federationPending.current=false;setFederationBusy(false);form.reset();
    const cleanup=()=>{active=false;controller.abort();};
    if(location.pathname!==authenticationRoutes.login.pathname&&location.pathname!==authenticationRoutes.signedIn.pathname){navigatePath(authenticationRoutes.login.pathname,true);return cleanup;}
    if(window.location.search||window.location.hash)navigatePath(location.pathname,true);
    if(!signedIn){
      void options(controller.signal).then(value=>{if(active){setBoot(value);finish();}}).catch(()=>{if(active){setEntryUnavailable(true);finish();}});
      return cleanup;
    }
    void fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal}).then(async response=>{
      if(response.status===401){if(active)navigatePath(authenticationRoutes.login.pathname,true);return;}
      if(!response.ok)throw new Error('Unavailable');const result=await response.json() as Session;
      if(!result.authenticated||!result.csrfToken)throw new Error('Unavailable');if(active){setSession(result);finish();}
    }).catch(()=>{if(active){setError('authentication.error.unavailable');finish();}});
    return cleanup;
  },[location.revision]);
  useEffect(()=>{if(!guarded)heading.current?.focus();},[guarded,signedIn]);
  async function signIn(){
    if(passkeyPending.current||federationPending.current)return;
    const attempt={idempotencyKey:boot.idempotencyKey,email,rememberMe:remember};const revision=location.revision;const token=Symbol();const controller=new AbortController();
    attemptRef.current=token;attemptController.current=controller;
    const current=()=>attemptRef.current===token&&routeRevision.current===revision&&!controller.signal.aborted;
    setError(undefined);
    try{
      const response=await fetch('/authentication/sign-in',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal,headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify({...attempt,password})});
      if(!current())return;
      if(response.ok){const result:unknown=await response.json();if(parseMfaRequired(result,attempt.idempotencyKey)){if(current())navigatePath(totpRoutes.mfa.pathname);return;}if(!result||typeof result!=='object'||!('authenticated' in result)||result.authenticated!==true)throw new Error('Unavailable');if(current())navigatePath(authenticationRoutes.signedIn.pathname);return;}
      const problem=await response.json() as {error_code?:string};if(!current())return;setError(errors[problem.error_code??'']??'authentication.error.unavailable');
      if(response.status===403||response.status===409){const next=await options(controller.signal);if(current())setBoot(next);}
    }catch{
      if(!current())return;
      try{const pending=await loadTotpMfaOptions(controller.signal);if(current()&&pending.requestId===attempt.idempotencyKey&&pending.status!=='EXPIRED'){navigatePath(totpRoutes.mfa.pathname);return;}}catch{/* Pending MFA is not final authentication, and another original request is never adopted. */}
      // Current-session existence supplies CSRF only; exact attempted-operation evidence owns recovered success.
      try{
        const sessionResponse=await fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal});
        if(sessionResponse.ok){
          const present=await sessionResponse.json() as Session;
          if(current()&&present.authenticated===true&&present.csrfToken){
            const response=await fetch('/authentication/result',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal,headers:{'Content-Type':'application/json','X-Tunner-CSRF':present.csrfToken},body:JSON.stringify(attempt)});
            if(response.ok){const result=await response.json() as {status?:string;idempotencyKey?:string};if(current()&&result.status==='COMPLETED'&&result.idempotencyKey===attempt.idempotencyKey){navigatePath(authenticationRoutes.signedIn.pathname);return;}}
          }
        }
      }catch{/* Preserve the original attempt; never infer completion or replay credentials. */}
      if(current())setError('authentication.error.unavailable');
    }finally{
      if(current()){setPassword('');setVisible(false);form.reset();}
      if(attemptController.current===controller)attemptController.current=undefined;
    }
  }
  async function signOut(){
    if(logoutPending.current||!session)return;logoutPending.current=true;setLogoutBusy(true);setError(undefined);
    try{
      const response=await fetch('/authentication/sign-out',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json','X-Tunner-CSRF':session.csrfToken},body:'{}'});
      if(!response.ok&&response.status!==401)throw new Error('Unavailable');setSession(undefined);navigatePath(authenticationRoutes.login.pathname);
    }catch{setError('authentication.error.unavailable');}
    finally{logoutPending.current=false;setLogoutBusy(false);}
  }
  function changed(){setError(undefined);}
  function recover(){if(boot.recoveryAvailable!==true||form.submitPending||passkeyPending.current||federationPending.current)return;setPassword('');setVisible(false);form.reset();window.location.assign(recoveryRoutes.entry.pathname);}
  async function refreshFederation(signal:AbortSignal){const revision=location.revision;const value=await options(signal);if(!signal.aborted&&routeRevision.current===revision)setBoot(value);}
  if(entryUnavailable)return <ContentUnavailable/>;
  let federation;try{federation=parseFederationBootstrap(boot.federation);if(!signedIn&&(federation?.googleAvailable||federation?.microsoftAvailable||federation?.pending))requirePublished(boot.content,boot.locale,federationSignInKeys);}catch{return <ContentUnavailable/>;}
  if(signedIn){try{requirePublished(boot.content,boot.locale,['signedIn.security']);}catch{return <ContentUnavailable/>;}}
  if(boot.passkeyAvailable===true||boot.passkeyRecovery){try{requirePublished(boot.content,boot.locale,loginPasskeyKeys);parseAuthenticationPasskeyRecovery(boot.passkeyRecovery);if(boot.passkeyAvailable===true&&(!Number.isInteger(boot.maximumCredentialJsonBytes)||boot.maximumCredentialJsonBytes!<1024||boot.maximumCredentialJsonBytes!>1048576))throw new Error('Explicit passkey bounds required.');}catch{return <ContentUnavailable/>;}}
  return <ContentProvider locale={boot.locale} audience="authentication" resolve={key=>boot.content[key]}><div className="authentication-page">
    <img className="authentication-glow indigo" src="/registration/assets/login-bg-indigo.svg" alt=""/><img className="authentication-glow violet" src="/registration/assets/login-bg-violet.svg" alt=""/>
    <header className="authentication-header"><TunnerBrand contentKey="brand.tunner"/></header>
    <main className="authentication-stage" aria-busy={guarded}>
      {!guarded&&<section className={'authentication-card'+(signedIn?' signed-in':'')} data-tunner-screen={signedIn?authenticationRoutes.signedIn.id:authenticationRoutes.login.id}>
        {signedIn?<>{session&&<>
          <div className="authentication-check" aria-hidden="true"><img src="/registration/assets/ready-imgEllipse1.svg" alt=""/><img className="approved-check" src="/registration/assets/ready-approved-check.svg" alt=""/></div>
          <div className="authentication-intro"><h1 ref={heading} tabIndex={-1}><ContentText contentKey="signedIn.heading"/></h1><p><ContentText contentKey="signedIn.description"/></p></div>
          <aside className="authentication-note"><strong><ContentText contentKey="signedIn.note.heading"/></strong><p><ContentText contentKey="signedIn.note.description"/></p></aside>
          <PrimaryButton labelKey="signedIn.signOut" disabled={logoutBusy} aria-busy={logoutBusy} onClick={()=>void signOut()}/>
          <a className="tn-primary-button authentication-security" data-presentation="standard" data-appearance="outline" href="/account/security"><ContentText contentKey="signedIn.security"/></a>
          <p className="authentication-caption"><ContentText contentKey="signedIn.caption"/></p>
        </>}</>:<>
          <div className="authentication-intro"><h1 ref={heading} tabIndex={-1}><ContentText contentKey="login.heading"/></h1><p><ContentText contentKey="login.description"/></p></div>
          <div><FederationSignIn key={location.revision} configuration={federation} rememberMe={remember} disabled={form.submitPending||passkeyBusy} unavailableTitle={unavailable.text} canStart={()=>!passkeyPending.current&&!attemptController.current&&!logoutPending.current} isCurrent={()=>routeRevision.current===location.revision} onBusyChange={value=>{if(routeRevision.current!==location.revision)return;federationPending.current=value;setFederationBusy(value);if(value){setPassword('');setVisible(false);setError(undefined);form.reset();}}} onAuthenticated={()=>navigatePath(authenticationRoutes.signedIn.pathname)} onMfaRequired={()=>navigatePath(totpRoutes.mfa.pathname)} onReset={refreshFederation}/><div className="authentication-separator"><ContentText contentKey="login.separator"/></div></div>
          <form noValidate data-tunner-form="FORM-LOGIN" onSubmit={event=>void form.handleSubmit(event,signIn)}>
            <div className="authentication-fields">
              <TextField labelKey="login.email.label" placeholderKey="login.email.placeholder" name="email" type="email" required autoComplete="username" value={email} disabled={form.submitPending||passkeyBusy||federationBusy} errorKey={form.errors.email} onBlur={event=>form.onBlur('email',event)} onChange={event=>{setEmail(event.target.value);changed();}}/>
              <div className="authentication-password"><TextField labelKey="login.password.label" placeholderKey="login.password.placeholder" name="password" type={visible?'text':'password'} required autoComplete="current-password" value={password} disabled={form.submitPending||passkeyBusy||federationBusy} errorKey={form.errors.password} onBlur={event=>form.onBlur('password',event)} onChange={event=>{setPassword(event.target.value);changed();}}/><button className="authentication-reveal" type="button" aria-label={show.text} aria-pressed={visible} disabled={form.submitPending||passkeyBusy||federationBusy} onClick={()=>setVisible(value=>!value)}><img src="/registration/assets/login-eye-off.svg" alt=""/></button></div>
              <div className="authentication-options"><label><input type="checkbox" checked={remember} disabled={form.submitPending||passkeyBusy||federationBusy} onChange={event=>setRemember(event.target.checked)}/><ContentText contentKey="login.remember"/></label><button type="button" disabled={boot.recoveryAvailable!==true||form.submitPending||passkeyBusy||federationBusy} title={boot.recoveryAvailable===true?undefined:unavailable.text} onClick={recover}><ContentText contentKey="login.forgot"/></button></div>
            </div>
            <div className="authentication-actions"><PrimaryButton type="submit" labelKey="login.submit" disabled={form.submitPending||passkeyBusy||federationBusy} aria-busy={form.submitPending}/><PasskeySignIn key={location.revision} available={boot.passkeyAvailable===true} maximumCredentialJsonBytes={boot.maximumCredentialJsonBytes??0} csrfToken={boot.csrfToken} rememberMe={remember} disabled={form.submitPending||federationBusy} {...(boot.passkeyRecovery?{recovery:boot.passkeyRecovery}:{})} onBusyChange={value=>{passkeyPending.current=value;setPasskeyBusy(value);if(value){setPassword('');setVisible(false);form.reset();}}} onAuthenticated={()=>navigatePath(authenticationRoutes.signedIn.pathname)} onMfaRequired={()=>navigatePath(totpRoutes.mfa.pathname)}/><p className="authentication-create"><ContentText contentKey="login.create.description"/> <a href="/registration"><ContentText contentKey="login.create"/></a></p></div>
            {form.hasErrors&&<FormErrorSummary contentKey="authentication.error.highlighted"/>}
          </form>
        </>}
        {error&&<FormErrorSummary contentKey={error}/>}
        {(form.submitPending||logoutBusy)&&<p role="status"><ContentText contentKey="authentication.working"/></p>}
      </section>}
    </main>
    <footer className="authentication-footer"><a href={boot.privacyUri}><ContentText contentKey="authentication.footer.privacy"/></a><span aria-hidden="true">•</span><a href={boot.termsUri}><ContentText contentKey="authentication.footer.terms"/></a>{boot.supportUri&&<><span aria-hidden="true">•</span><a href={boot.supportUri}><ContentText contentKey="authentication.footer.support"/></a></>}</footer>
  </div></ContentProvider>;
}
createRoot(document.getElementById('root')!).render(<App/>);
