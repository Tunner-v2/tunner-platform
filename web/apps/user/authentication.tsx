import { createRoot } from 'react-dom/client';
import { useEffect, useRef, useState } from 'react';
import { ContentProvider, ContentText, ContentUnavailable, FormErrorSummary, PrimaryButton, TextField, ThemeProvider, TunnerBrand, navigatePath, useRouteLocation, useTunnerForm } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import '@tunner/ui/styles.css';
import './authentication.css';
import { passwordSignInSchema } from './authentication.validation';
import { authenticationRoutes } from './authentication.routes';
interface Bootstrap {csrfToken:string;idempotencyKey:string;passwordMaximumLength:number;locale:string;content:Record<string,PublishedContent>;termsUri:string;privacyUri:string;supportUri:string|null}
interface Session {authenticated:true;csrfToken:string}
const errors:Readonly<Record<string,string>>={AUTHENTICATION_FAILED:'authentication.error.credentials',RATE_OR_ABUSE_LIMITED:'authentication.error.limited'};
async function options(signal?:AbortSignal):Promise<Bootstrap>{const response=await fetch('/authentication/options',{credentials:'same-origin',cache:'no-store',redirect:'error',...(signal?{signal}:{})});if(!response.ok)throw new Error('Unavailable');return response.json() as Promise<Bootstrap>;}
function App(){
  const [boot,setBoot]=useState<Bootstrap>();const [unavailable,setUnavailable]=useState(false);
  useEffect(()=>{const controller=new AbortController();void options(controller.signal).then(setBoot).catch(()=>{if(!controller.signal.aborted)setUnavailable(true);});return()=>controller.abort();},[]);
  if(unavailable)return <ContentUnavailable/>;if(!boot)return <main aria-busy="true"/>;
  return <ThemeProvider surface="user" mode="light"><ContentProvider locale={boot.locale} audience="authentication" resolve={key=>boot.content[key]}><Screens initial={boot}/></ContentProvider></ThemeProvider>;
}
function Screens({initial}:{initial:Bootstrap}){
  const location=useRouteLocation();const [boot,setBoot]=useState(initial);const [session,setSession]=useState<Session>();const [guard,setGuard]=useState(true);
  const [email,setEmail]=useState('');const [password,setPassword]=useState('');const [remember,setRemember]=useState(false);const [visible,setVisible]=useState(false);
  const [error,setError]=useState<string>();const [logoutBusy,setLogoutBusy]=useState(false);const logoutPending=useRef(false);const heading=useRef<HTMLHeadingElement>(null);
  const form=useTunnerForm(passwordSignInSchema(boot.passwordMaximumLength),{email,password,remember});
  const show=boot.content[visible?'login.hide':'login.show']!;const unavailable=boot.content['login.unavailable']!;
  const signedIn=location.pathname===authenticationRoutes.signedIn.pathname;
  useEffect(()=>{
    const controller=new AbortController();let active=true;setPassword('');setVisible(false);setError(undefined);setSession(undefined);setGuard(true);form.reset();
    if(location.pathname!==authenticationRoutes.login.pathname&&location.pathname!==authenticationRoutes.signedIn.pathname){navigatePath(authenticationRoutes.login.pathname,true);return()=>controller.abort();}
    if(window.location.search||window.location.hash)navigatePath(location.pathname,true);
    if(!signedIn){setGuard(false);return()=>controller.abort();}
    void fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal}).then(async response=>{
      if(response.status===401){if(active)navigatePath(authenticationRoutes.login.pathname,true);return;}
      if(!response.ok)throw new Error('Unavailable');const result=await response.json() as Session;
      if(!result.authenticated||!result.csrfToken)throw new Error('Unavailable');if(active){setSession(result);setGuard(false);}
    }).catch(()=>{if(active){setError('authentication.error.unavailable');setGuard(false);}});
    return()=>{active=false;controller.abort();};
  },[location.revision]);
  useEffect(()=>{if(!guard)heading.current?.focus();},[guard,signedIn]);
  async function signIn(){
    setError(undefined);
    try{
      const response=await fetch('/authentication/sign-in',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify({idempotencyKey:boot.idempotencyKey,email,password,rememberMe:remember})});
      if(response.ok){navigatePath(authenticationRoutes.signedIn.pathname);return;}
      const problem=await response.json() as {error_code?:string};setError(errors[problem.error_code??'']??'authentication.error.unavailable');
      if(response.status===403||response.status===409)setBoot(await options());
    }catch{
      // A lost success response may already have installed its cookie. Resolve authoritative state before another credential submission.
      try{const response=await fetch('/authentication/session',{credentials:'same-origin',cache:'no-store',redirect:'error'});if(response.ok){navigatePath(authenticationRoutes.signedIn.pathname);return;}}catch{/* Safe generic presentation below. */}
      setError('authentication.error.unavailable');
    }finally{setPassword('');setVisible(false);form.reset();}
  }
  async function signOut(){
    if(logoutPending.current||!session)return;logoutPending.current=true;setLogoutBusy(true);setError(undefined);
    try{
      const response=await fetch('/authentication/sign-out',{method:'POST',credentials:'same-origin',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json','X-Tunner-CSRF':session.csrfToken},body:'{}'});
      if(!response.ok&&response.status!==401)throw new Error('Unavailable');setSession(undefined);navigatePath(authenticationRoutes.login.pathname);setBoot(await options());
    }catch{setError('authentication.error.unavailable');}
    finally{logoutPending.current=false;setLogoutBusy(false);}
  }
  function changed(){setError(undefined);}
  return <div className="authentication-page">
    <img className="authentication-glow indigo" src="/registration/assets/login-bg-indigo.svg" alt=""/><img className="authentication-glow violet" src="/registration/assets/login-bg-violet.svg" alt=""/>
    <header className="authentication-header"><TunnerBrand contentKey="brand.tunner"/></header>
    <main className="authentication-stage" aria-busy={guard}>
      {!guard&&<section className={'authentication-card'+(signedIn?' signed-in':'')} data-tunner-screen={signedIn?authenticationRoutes.signedIn.id:authenticationRoutes.login.id}>
        {signedIn?<>{session&&<>
          <div className="authentication-check" aria-hidden="true"><img src="/registration/assets/ready-imgEllipse1.svg" alt=""/><img className="approved-check" src="/registration/assets/ready-approved-check.svg" alt=""/></div>
          <div className="authentication-intro"><h1 ref={heading} tabIndex={-1}><ContentText contentKey="signedIn.heading"/></h1><p><ContentText contentKey="signedIn.description"/></p></div>
          <aside className="authentication-note"><strong><ContentText contentKey="signedIn.note.heading"/></strong><p><ContentText contentKey="signedIn.note.description"/></p></aside>
          <PrimaryButton labelKey="signedIn.signOut" disabled={logoutBusy} aria-busy={logoutBusy} onClick={()=>void signOut()}/>
          <p className="authentication-caption"><ContentText contentKey="signedIn.caption"/></p>
        </>}</>:<>
          <div className="authentication-intro"><h1 ref={heading} tabIndex={-1}><ContentText contentKey="login.heading"/></h1><p><ContentText contentKey="login.description"/></p></div>
          <div><div className="authentication-providers"><button type="button" disabled title={unavailable.text}><ContentText contentKey="login.google"/><img src="/registration/assets/login-google.svg" alt=""/></button><button type="button" disabled title={unavailable.text}><ContentText contentKey="login.microsoft"/></button></div><div className="authentication-separator"><ContentText contentKey="login.separator"/></div></div>
          <form noValidate data-tunner-form="FORM-LOGIN" onSubmit={event=>void form.handleSubmit(event,signIn)}>
            <div className="authentication-fields">
              <TextField labelKey="login.email.label" placeholderKey="login.email.placeholder" name="email" type="email" required autoComplete="username" value={email} disabled={form.submitPending} errorKey={form.errors.email} onBlur={()=>form.onBlur('email')} onChange={event=>{setEmail(event.target.value);changed();}}/>
              <div className="authentication-password"><TextField labelKey="login.password.label" placeholderKey="login.password.placeholder" name="password" type={visible?'text':'password'} required autoComplete="current-password" value={password} disabled={form.submitPending} errorKey={form.errors.password} onBlur={()=>form.onBlur('password')} onChange={event=>{setPassword(event.target.value);changed();}}/><button className="authentication-reveal" type="button" aria-label={show.text} aria-pressed={visible} disabled={form.submitPending} onClick={()=>setVisible(value=>!value)}><img src="/registration/assets/login-eye-off.svg" alt=""/></button></div>
              <div className="authentication-options"><label><input type="checkbox" checked={remember} disabled={form.submitPending} onChange={event=>setRemember(event.target.checked)}/><ContentText contentKey="login.remember"/></label><button type="button" disabled title={unavailable.text}><ContentText contentKey="login.forgot"/></button></div>
            </div>
            <div className="authentication-actions"><PrimaryButton type="submit" labelKey="login.submit" disabled={form.submitPending} aria-busy={form.submitPending}/><p className="authentication-create"><ContentText contentKey="login.create.description"/> <a href="/registration"><ContentText contentKey="login.create"/></a></p></div>
            {form.hasErrors&&<FormErrorSummary contentKey="authentication.error.highlighted"/>}
          </form>
        </>}
        {error&&<FormErrorSummary contentKey={error}/>}
        {(form.submitPending||logoutBusy)&&<p role="status"><ContentText contentKey="authentication.working"/></p>}
      </section>}
    </main>
    <footer className="authentication-footer"><a href={boot.privacyUri}><ContentText contentKey="authentication.footer.privacy"/></a><span aria-hidden="true">•</span><a href={boot.termsUri}><ContentText contentKey="authentication.footer.terms"/></a>{boot.supportUri&&<><span aria-hidden="true">•</span><a href={boot.supportUri}><ContentText contentKey="authentication.footer.support"/></a></>}</footer>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App/>);
