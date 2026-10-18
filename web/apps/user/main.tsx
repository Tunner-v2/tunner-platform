import { createRoot } from 'react-dom/client';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { ContentProvider, ContentText, ContentUnavailable, PrimaryButton, TextField, ThemeProvider, TunnerBrand } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import '@tunner/ui/styles.css';
import './registration.css';

interface Bootstrap {
  csrfToken: string; serverTime: string; policyVersion: string; termsVersion: string; privacyVersion: string;
  passwordMinimumLength: number; passwordMaximumLength: number; proofDigits: number;
  locale: string; content: Record<string,PublishedContent>; termsUri: string; privacyUri: string; supportUri: string | null;
}
interface Operation {
  operationId: string; correlationId: string; status: string; version: number; expiresAt: string | null; resendAfter: string | null;
  nextAction: 'VERIFY_CONTACT'|'PERSONAL_INFORMATION'|'CONSENT_REQUIRED'|'COMPLETE';
  personalInformation?: {fullName: string|null;displayName: string|null;verifiedEmail: string;fullNameMaximumLength: number;displayNameMaximumLength: number}|null;
}
interface Problem { error_code?: string; current_version?: number; correlation_id?: string }
const errorKeys: Readonly<Record<string,string>> = {
  VERSION_CONFLICT: 'registration.error.changed',
  RATE_OR_ABUSE_LIMITED: 'registration.error.limited', NOT_FOUND_OR_NOT_DISCLOSABLE: 'registration.error.proof',
  DEPENDENCY_UNAVAILABLE: 'registration.error.unavailable', VALIDATION_ERROR: 'registration.error.invalid'
};
function App() {
  const [boot,setBoot]=useState<Bootstrap>();const [unavailable,setUnavailable]=useState(false);
  useEffect(()=>{void fetch('/registration/options',{credentials:'same-origin',cache:'no-store',redirect:'error'})
    .then(async response=>{if(!response.ok)throw new Error('Unavailable');setBoot(await response.json() as Bootstrap);})
    .catch(()=>setUnavailable(true));},[]);
  if(unavailable)return <ContentUnavailable />;
  if(!boot)return <main aria-busy="true" />;
  const options=boot;
  return <ThemeProvider surface="user" mode="light"><ContentProvider locale={options.locale} audience="registration" resolve={key=>options.content[key]}><Registration boot={options} /></ContentProvider></ThemeProvider>;
}
function Registration({boot}:{boot:Bootstrap}) {
  const [stage,setStage]=useState<'register'|'personal'|'consent'|'verify'|'ready'>('register');
  const [email,setEmail]=useState('');const [password,setPassword]=useState('');const [birth,setBirth]=useState('');const [confirmation,setConfirmation]=useState('');
  const [fullName,setFullName]=useState('');const [displayName,setDisplayName]=useState('');
  const [notices,setNotices]=useState(false);const [code,setCode]=useState('');const [operation,setOperation]=useState<Operation>();
  const [error,setError]=useState<string>();const [uncertain,setUncertain]=useState(false);const [busy,setBusy]=useState(false);
  const serverClock=useRef({epoch:Date.parse(boot.serverTime),started:performance.now()});
  const [now,setNow]=useState(serverClock.current.epoch);const inFlight=useRef(false);const registerKey=useRef(crypto.randomUUID());
  const heading=useRef<HTMLHeadingElement>(null);
  useEffect(()=>{heading.current?.focus();},[stage,error]);
  useEffect(()=>{const timer=window.setInterval(()=>setNow(serverClock.current.epoch+performance.now()-serverClock.current.started),1000);return ()=>window.clearInterval(timer);},[]);
  function changed() {registerKey.current=crypto.randomUUID();setError(undefined);}
  function accept(result:Operation) {
    const next=result.nextAction;
    if(!['VERIFY_CONTACT','PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE'].includes(next))throw new Error('Unavailable');
    if(next==='PERSONAL_INFORMATION'&&!result.personalInformation)throw new Error('Unavailable');
    setOperation(result);setUncertain(false);setCode('');setError(undefined);
    if(result.personalInformation){setFullName(result.personalInformation.fullName??'');setDisplayName(result.personalInformation.displayName??'');}
    setStage(next==='COMPLETE'?'ready':next==='PERSONAL_INFORMATION'?'personal':next==='CONSENT_REQUIRED'?'consent':'verify');
  }
  async function send(path:string,body:unknown) {
    if(inFlight.current)return;
    inFlight.current=true;setBusy(true);setError(undefined);
    try {
      const response=await fetch(path,{method:'POST',credentials:'same-origin',redirect:'error',cache:'no-store',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify(body)});
      if(response.ok)accept(await response.json() as Operation);
      else {const problem=await response.json() as Problem;setError(errorKeys[problem.error_code??'']??'registration.error.unavailable');if(path==='/registration/register')setStage('register');
        if(problem.current_version!==undefined)setOperation(current=>current?{...current,version:problem.current_version!}:current);}
    } catch {setUncertain(true);setError('registration.error.uncertain');}
    finally {setPassword('');setConfirmation('');setBirth('');inFlight.current=false;setBusy(false);}
  }
  function start(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();const length=Array.from(password).length;
    if(length<boot.passwordMinimumLength||length>boot.passwordMaximumLength){setError('registration.error.password');return;}
    if(password!==confirmation){setError('registration.error.confirmation');return;}
    void send('/registration/register',{idempotencyKey:registerKey.current,email,password,passwordConfirmation:confirmation,termsVersion:boot.termsVersion,privacyVersion:boot.privacyVersion,acknowledgedNotices:notices});
  }
  function personal(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();if(operation)void send('/registration/personal-information',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version,dateOfBirth:birth,fullName,displayName});
  }
  function verify(event:FormEvent<HTMLFormElement>) {event.preventDefault();if(operation)void send('/registration/verify',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version,code});}
  async function recover() {
    if(inFlight.current)return;inFlight.current=true;setBusy(true);
    try {const response=await fetch('/registration/status/'+encodeURIComponent(registerKey.current),{credentials:'same-origin',redirect:'error',cache:'no-store'});
      if(response.ok)accept(await response.json() as Operation);else {const problem=await response.json() as Problem;setError(errorKeys[problem.error_code??'']??'registration.error.uncertain');}}
    catch {setError('registration.error.uncertain');}finally {inFlight.current=false;setBusy(false);}
  }
  const scene=stage==='register'||stage==='personal'||stage==='consent'?'register':stage==='verify'?'verify':'ready';
  const expiry=operation?.expiresAt?new Date(operation.expiresAt):undefined;
  const resendDisabled=busy||!operation||Boolean(operation.resendAfter&&now<Date.parse(operation.resendAfter));
  return <div className="registration-page">
    <img className="registration-glow indigo" src={'/registration/assets/'+scene+'-imgBgGlowIndigo.svg'} alt="" aria-hidden="true" />
    <img className="registration-glow violet" src={'/registration/assets/'+scene+'-imgBgGlowViolet.svg'} alt="" aria-hidden="true" />
    <header className="registration-header"><TunnerBrand contentKey="brand.tunner" /></header>
    <main className="registration-stage">
      <section className={'registration-card '+stage} aria-busy={busy}>
        {stage==='register'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="registration.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="registration.description" /></p>
          <aside className="account-context"><strong><ContentText contentKey="registration.context.heading" /></strong><p><ContentText contentKey="registration.context.description" /></p></aside>
          <form onSubmit={start}>
            <TextField presentation="spacious" labelKey="registration.email.label" placeholderKey="registration.email.placeholder" name="email" type="email" value={email} required autoComplete="email" maxLength={256} disabled={busy||uncertain} onChange={e=>{setEmail(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="registration.password.label" placeholderKey="registration.password.placeholder" helpKey="registration.password.help" name="password" type="password" value={password} required autoComplete="new-password" maxLength={boot.passwordMaximumLength*2} disabled={busy||uncertain} onChange={e=>{setPassword(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="registration.confirmation.label" placeholderKey="registration.confirmation.placeholder" name="password_confirmation" type="password" value={confirmation} required autoComplete="new-password" maxLength={boot.passwordMaximumLength*2} disabled={busy||uncertain} onChange={e=>{setConfirmation(e.target.value);changed();}} />
            <label className="notice-ack"><input type="checkbox" checked={notices} required disabled={busy||uncertain} onChange={e=>{setNotices(e.target.checked);changed();}} /><span className="notice-copy"><span className="notice-intro"><ContentText contentKey="registration.notices.ack" /></span><a className="notice-terms" href={boot.termsUri}><ContentText contentKey="registration.terms" /></a><span className="notice-and"><ContentText contentKey="registration.notices.and" /></span><a href={boot.privacyUri}><ContentText contentKey="registration.privacy" /></a></span></label>
            <PrimaryButton presentation="spacious" type="submit" labelKey="registration.continue" disabled={busy||uncertain} aria-busy={busy} />
          </form>
        </>:stage==='personal'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="personal.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="personal.description" /></p>
          <form onSubmit={personal}>
            <TextField presentation="spacious" labelKey="personal.fullName.label" name="full_name" value={fullName} autoComplete="name" maxLength={operation?.personalInformation?.fullNameMaximumLength} disabled={busy||uncertain} onChange={e=>setFullName(e.target.value)} />
            <TextField presentation="spacious" labelKey="personal.displayName.label" helpKey="personal.displayName.help" name="display_name" value={displayName} autoComplete="nickname" maxLength={operation?.personalInformation?.displayNameMaximumLength} disabled={busy||uncertain} onChange={e=>setDisplayName(e.target.value)} />
            <TextField presentation="spacious" labelKey="personal.birth.label" name="date_of_birth" type="date" value={birth} required autoComplete="bday" disabled={busy||uncertain} onChange={e=>setBirth(e.target.value)} />
            <TextField presentation="spacious" labelKey="personal.email.label" helpKey="personal.email.help" name="verified_email" type="email" value={operation?.personalInformation?.verifiedEmail??''} readOnly autoComplete="email" />
            <PrimaryButton presentation="spacious" type="submit" labelKey="registration.continue" disabled={busy||uncertain} aria-busy={busy} />
          </form>
        </>:stage==='consent'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="consent.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="consent.description" /></p>
          <PrimaryButton presentation="spacious" labelKey="consent.check" disabled={busy||uncertain} onClick={()=>{if(operation)void send('/registration/continue',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version});}} />
        </>:stage==='verify'?<>
          <span className="step"><ContentText contentKey="verification.step" /></span>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="verification.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="verification.description" /></p>
          <form onSubmit={verify} className="verification-form">
            <TextField presentation="verification" labelKey="verification.code.label" placeholderKey="verification.code.label" name="verification_code" value={code} required autoComplete="one-time-code" inputMode="numeric" maxLength={boot.proofDigits} disabled={busy||uncertain} onChange={e=>setCode(e.target.value)} />
            <p className="expiry"><ContentText contentKey="verification.expiry" /></p>
            <PrimaryButton presentation="spacious" type="submit" labelKey="verification.submit" disabled={busy||uncertain||Boolean(expiry&&now>=expiry.valueOf())} aria-busy={busy} />
            <PrimaryButton presentation="spacious" appearance="outline" labelKey="verification.resend" disabled={resendDisabled||uncertain} onClick={()=>{if(operation)void send('/registration/resend',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version});}} />
          </form>
          <aside className="verification-note"><strong><ContentText contentKey="verification.note.heading" /></strong><p><ContentText contentKey="verification.note.description" /></p></aside>
          <p className="verification-back"><span className="wrong-email"><ContentText contentKey="verification.wrongEmail" /></span> <button type="button" className="text-action" disabled={busy||uncertain} onClick={()=>{setStage('register');setOperation(undefined);setCode('');setError(undefined);registerKey.current=crypto.randomUUID();}}><ContentText contentKey="verification.back" /></button></p>
        </>:<>
          <div className="verified-mark"><img src="/registration/assets/ready-imgEllipse1.svg" alt="" /><img className="approved-check" src="/registration/assets/ready-approved-check.svg" alt="" /></div>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="registration.ready.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="registration.ready.description" /></p>
          <aside className="verification-note"><strong><ContentText contentKey="registration.context.heading" /></strong><p><ContentText contentKey="registration.ready.boundary" /></p></aside>
          <p className="ready-help"><ContentText contentKey="registration.ready.next" /></p>
          {boot.supportUri&&<p className="ready-support"><span><ContentText contentKey="registration.ready.needHelp" /></span> <a href={boot.supportUri}><ContentText contentKey="registration.ready.contact" /></a></p>}
        </>}
        {busy&&<p role="status"><ContentText contentKey="registration.working" /></p>}
        {error&&<p className="registration-error" role="alert"><ContentText contentKey={error} />{error==='registration.error.password'&&<> {boot.passwordMinimumLength}–{boot.passwordMaximumLength}</>}</p>}
        {uncertain&&<><PrimaryButton presentation="spacious" appearance="outline" labelKey="registration.checkProgress" disabled={busy} onClick={()=>void recover()} /><button type="button" className="text-action" disabled={busy} onClick={()=>window.location.reload()}><ContentText contentKey="registration.startAgain" /></button></>}
      </section>
    </main>
    <footer className="registration-footer"><a href={boot.privacyUri}><ContentText contentKey="registration.privacy" /></a><img src={'/registration/assets/'+scene+'-imgEllipse.svg'} alt="" /><a href={boot.termsUri}><ContentText contentKey="registration.footer.terms" /></a>{boot.supportUri&&<><img src={'/registration/assets/'+scene+'-imgEllipse.svg'} alt="" /><a href={boot.supportUri}><ContentText contentKey="registration.footer.contact" /></a></>}</footer>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App />);
