import { createRoot } from 'react-dom/client';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { ContentProvider, ContentText, ContentUnavailable, PrimaryButton, TextField, ThemeProvider, TunnerBrand, useTunnerForm, FormErrorSummary, FieldError, navigatePath, useRouteLocation } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';
import '@tunner/ui/styles.css';
import './registration.css';
import { registrationRoutes, stageForAction } from './routes';
import type { RegistrationStage } from './routes';
import { registrationSchema, verificationSchema, personalInformationSchema } from './validation';
import { requirePublished } from './passkeys.api';
import {federationNoticeSchema,federationRegistrationKeys,federatedRegistrationCommand,parseFederationIntake,sameFederationIntake} from './registration.federation';
import type {FederationIntake} from './registration.federation';

interface Bootstrap {
  csrfToken: string; registrationKey?: string; federationIntake?: FederationIntake|undefined; serverTime: string; policyVersion: string; termsVersion: string; privacyVersion: string;
  passwordMinimumLength: number; passwordMaximumLength: number; proofDigits: number;
  locale: string; content: Record<string,PublishedContent>; termsUri: string; privacyUri: string; supportUri: string | null;
}
interface Operation {
  operationId: string; correlationId: string; status: string; version: number; expiresAt: string | null; resendAfter: string | null;
  nextAction: 'VERIFY_CONTACT'|'PERSONAL_INFORMATION'|'CONSENT_REQUIRED'|'COMPLETE';
  personalInformation?: {fullName: string|null;displayName: string|null;verifiedEmail: string;fullNameMaximumLength: number;displayNameMaximumLength: number}|null|undefined;
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
    .then(async response=>{if(!response.ok)throw new Error('Unavailable');const value=await response.json() as Bootstrap;setBoot({...value,federationIntake:parseFederationIntake(value.federationIntake,value.registrationKey)});})
    .catch(()=>setUnavailable(true));},[]);
  if(unavailable)return <ContentUnavailable />;
  if(!boot)return <main aria-busy="true" />;
  const options=boot;
  return <ThemeProvider surface="user" mode="light"><ContentProvider locale={options.locale} audience="registration" resolve={key=>options.content[key]}><Registration boot={options} /></ContentProvider></ThemeProvider>;
}
function Registration({boot}:{boot:Bootstrap}) {
  const navigation=useRouteLocation();const pathname=navigation.pathname;const intake=boot.federationIntake;
  const routeRevision=useRef(navigation.revision);routeRevision.current=navigation.revision;
  const federationController=useRef<AbortController|undefined>(undefined);
  const [stage,setStage]=useState<RegistrationStage>('register');
  const [guardBusy,setGuardBusy]=useState(true);const [guardFailed,setGuardFailed]=useState(false);
  const confirmedPath=useRef<string|undefined>(undefined);
  const [email,setEmail]=useState('');const [password,setPassword]=useState('');const [birth,setBirth]=useState('');const [confirmation,setConfirmation]=useState('');
  const [fullName,setFullName]=useState('');const [displayName,setDisplayName]=useState('');
  const [notices,setNotices]=useState(false);const [code,setCode]=useState('');const [operation,setOperation]=useState<Operation>();
  const [error,setError]=useState<string>();const [uncertain,setUncertain]=useState(false);const [busy,setBusy]=useState(false);
  const serverClock=useRef({epoch:Date.parse(boot.serverTime),started:performance.now()});
  const [now,setNow]=useState(serverClock.current.epoch);const inFlight=useRef(false);const registerKey=useRef(boot.registrationKey??crypto.randomUUID());
  const heading=useRef<HTMLHeadingElement>(null);
  useEffect(()=>{if(!guardBusy)heading.current?.focus();},[stage,guardBusy]);
  useEffect(()=>{const timer=window.setInterval(()=>setNow(serverClock.current.epoch+performance.now()-serverClock.current.started),1000);return ()=>window.clearInterval(timer);},[]);
  const registrationForm=useTunnerForm(registrationSchema(boot),{email,password,password_confirmation:confirmation,notices});
  const federationForm=useTunnerForm(federationNoticeSchema,{notices});const noticeForm=intake?federationForm:registrationForm;
  const verificationForm=useTunnerForm(verificationSchema(boot.proofDigits),{verification_code:code});
  const personalForm=useTunnerForm(personalInformationSchema(operation?.personalInformation?.fullNameMaximumLength??0,operation?.personalInformation?.displayNameMaximumLength??0,new Date(now).toISOString().slice(0,10)),{full_name:fullName,display_name:displayName,date_of_birth:birth});
  useEffect(()=>{if(!intake)return;federationController.current?.abort();inFlight.current=false;setBusy(false);
    const leave=()=>{if(federationController.current){federationController.current.abort();setUncertain(true);setError('registration.error.uncertain');inFlight.current=false;setBusy(false);}};
    window.addEventListener('pagehide',leave);return()=>{federationController.current?.abort();window.removeEventListener('pagehide',leave);};
  },[navigation.revision]);
  function changed() {setError(undefined);}
  function move(next:RegistrationStage,replace=false) {
    const target=registrationRoutes[next].pathname;
    confirmedPath.current=window.location.pathname!==target||window.location.search||window.location.hash?target:undefined;
    setStage(next);setGuardBusy(false);navigatePath(target,replace);
  }
  useEffect(()=>{
    if(confirmedPath.current===pathname){confirmedPath.current=undefined;return;}
    const controller=new AbortController();let active=true;setGuardBusy(true);setGuardFailed(false);
    void fetch('/registration/progress',{credentials:'same-origin',redirect:'error',cache:'no-store',signal:controller.signal}).then(async response=>{
      if(response.ok){const result=await response.json() as Operation;if(!active)return;
        if((pathname==='/registration'||pathname==='/registration/')&&result.nextAction==='COMPLETE'&&!intake){
          await restart(true);
        }else accept(result,true);
      }else{const problem=await response.json() as Problem;if(!active)return;
        if(response.status===403||problem.error_code==='NOT_FOUND_OR_NOT_DISCLOSABLE'){
          if(intake){if(response.status===403){setGuardFailed(true);setGuardBusy(false);}else{setOperation(undefined);setUncertain(false);move('register',true);}}else await restart(true);
        }else{setGuardFailed(true);setGuardBusy(false);}
      }
    }).catch(()=>{if(active){setGuardFailed(true);setGuardBusy(false);}});
    return()=>{active=false;controller.abort();};
  },[navigation.revision]);
  function accept(result:Operation,replace=false) {
    const next=result.nextAction;
    if(!['VERIFY_CONTACT','PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE'].includes(next))throw new Error('Unavailable');
    if(next==='PERSONAL_INFORMATION'&&!result.personalInformation)throw new Error('Unavailable');
    setOperation(result);setUncertain(false);setCode('');setError(undefined);
    if(result.personalInformation){setFullName(result.personalInformation.fullName??'');setDisplayName(result.personalInformation.displayName??'');}
    registrationForm.reset();personalForm.reset();verificationForm.reset();
    const target=stageForAction(next);if(!target)throw new Error('Unavailable');move(target,replace);
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
    finally {setPassword('');setConfirmation('');setBirth('');setCode('');registrationForm.reset();personalForm.reset();verificationForm.reset();inFlight.current=false;setBusy(false);}
  }
  async function federated(kind:'complete'|'result') {
    if(!intake?.canContinue||inFlight.current)return;
    const controller=new AbortController(),revision=navigation.revision;federationController.current=controller;inFlight.current=true;setBusy(true);setError(undefined);
    const current=()=>!controller.signal.aborted&&routeRevision.current===revision&&federationController.current===controller;
    try {
      if(kind==='complete'){
        const response=await fetch('/registration/options',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:controller.signal});if(!response.ok)throw new Error('Unavailable');
        const fresh=await response.json() as Bootstrap;if(!current())return;
        if(!sameFederationIntake(intake,parseFederationIntake(fresh.federationIntake,fresh.registrationKey))||fresh.termsVersion!==boot.termsVersion||fresh.privacyVersion!==boot.privacyVersion||fresh.termsUri!==boot.termsUri||fresh.privacyUri!==boot.privacyUri){setNotices(false);setError('registration.error.changed');return;}
        requirePublished(fresh.content,fresh.locale,[...federationRegistrationKeys,'registration.notices.ack','registration.terms','registration.privacy']);
      }
      const result=await federatedRegistrationCommand(kind,intake,boot,controller.signal);if(!current())return;
      if(result)accept(result);else{setUncertain(true);setError('registration.error.uncertain');}
    }catch{if(current()){setUncertain(true);setError('registration.error.uncertain');}}
    finally{if(federationController.current===controller){federationController.current=undefined;inFlight.current=false;setBusy(false);federationForm.reset();}}
  }
  function start(event:FormEvent<HTMLFormElement>) {
    if(intake){void federationForm.handleSubmit(event,()=>federated('complete'));return;}
    void registrationForm.handleSubmit(event,()=>send('/registration/register',{idempotencyKey:registerKey.current,email,password,passwordConfirmation:confirmation,termsVersion:boot.termsVersion,privacyVersion:boot.privacyVersion,acknowledgedNotices:notices}));
  }
  function personal(event:FormEvent<HTMLFormElement>) {
    void personalForm.handleSubmit(event,async()=>{if(operation)await send('/registration/personal-information',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version,dateOfBirth:birth,fullName,displayName});});
  }
  function verify(event:FormEvent<HTMLFormElement>) {void verificationForm.handleSubmit(event,async()=>{if(operation)await send('/registration/verify',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version,code});});}
  async function restart(replace=false) {
    if(inFlight.current)return;inFlight.current=true;setBusy(true);
    try{const response=await fetch('/registration/restart',{method:'POST',credentials:'same-origin',redirect:'error',cache:'no-store',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:'{}'});
      if(!response.ok){setError('registration.error.unavailable');if(guardBusy){setGuardFailed(true);setGuardBusy(false);}return;}
      const next=await response.json() as {registrationKey:string};registerKey.current=next.registrationKey;setOperation(undefined);setCode('');setError(undefined);setUncertain(false);setPassword('');setConfirmation('');setBirth('');registrationForm.reset();personalForm.reset();verificationForm.reset();move('register',replace);
    }catch{setError('registration.error.unavailable');if(guardBusy){setGuardFailed(true);setGuardBusy(false);}}finally{inFlight.current=false;setBusy(false);}
  }
  async function recover() {
    if(stage==='register'&&intake){await federated('result');return;}
    if(inFlight.current)return;inFlight.current=true;setBusy(true);
    try {const response=await fetch('/registration/status/'+encodeURIComponent(registerKey.current),{credentials:'same-origin',redirect:'error',cache:'no-store'});
      if(response.ok)accept(await response.json() as Operation);else {const problem=await response.json() as Problem;setError(errorKeys[problem.error_code??'']??'registration.error.uncertain');}}
    catch {setError('registration.error.uncertain');}finally {inFlight.current=false;setBusy(false);}
  }
  const scene=stage==='register'||stage==='personal'||stage==='consent'?'register':stage==='verify'?'verify':'ready';
  const expiry=operation?.expiresAt?new Date(operation.expiresAt):undefined;
  const resendDisabled=busy||!operation||Boolean(operation.resendAfter&&now<Date.parse(operation.resendAfter));
  if(guardBusy||guardFailed)return <ContentUnavailable />;
  if(stage==='register'&&intake){if(!intake.canContinue||!intake.verifiedEmail)return <ContentUnavailable/>;try{requirePublished(boot.content,boot.locale,[...federationRegistrationKeys]);}catch{return <ContentUnavailable/>;}}
  if(stage==='ready'){try{requirePublished(boot.content,boot.locale,['registration.ready.signIn']);}catch{return <ContentUnavailable/>;}}
  return <div className="registration-page">
    <img className="registration-glow indigo" src={'/registration/assets/'+scene+'-imgBgGlowIndigo.svg'} alt="" aria-hidden="true" />
    <img className="registration-glow violet" src={'/registration/assets/'+scene+'-imgBgGlowViolet.svg'} alt="" aria-hidden="true" />
    <header className="registration-header"><TunnerBrand contentKey="brand.tunner" /></header>
    <main className="registration-stage">
      <section className={'registration-card '+stage+(stage==='register'&&intake?' federated':'')} aria-busy={busy}>
        {stage==='register'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="registration.heading" /></h1>
          <p className="subtitle"><ContentText contentKey={intake?'registration.federation.description':'registration.description'} /></p>
          <aside className="account-context"><strong><ContentText contentKey="registration.context.heading" /></strong><p><ContentText contentKey="registration.context.description" /></p></aside>
          <form noValidate data-tunner-form="FORM-REGISTRATION" onSubmit={start}>
            {intake?<TextField presentation="spacious" labelKey="registration.email.label" helpKey="registration.federation.emailSource.Google" type="email" value={intake.verifiedEmail??''} readOnly autoComplete="email" />:<><TextField presentation="spacious" labelKey="registration.email.label" placeholderKey="registration.email.placeholder" name="email" errorKey={registrationForm.errors.email} onBlur={event=>registrationForm.onBlur('email',event)} type="email" value={email} required autoComplete="email" maxLength={256} disabled={busy||uncertain} onChange={e=>{setEmail(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="registration.password.label" placeholderKey="registration.password.placeholder" helpKey={registrationForm.errors.password?undefined:'registration.password.help'} name="password" errorKey={registrationForm.errors.password} errorDetails={<> {boot.passwordMinimumLength}–{boot.passwordMaximumLength}</>} onBlur={event=>registrationForm.onBlur('password',event)} type="password" value={password} required autoComplete="new-password" maxLength={boot.passwordMaximumLength*2} disabled={busy||uncertain} onChange={e=>{setPassword(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="registration.confirmation.label" placeholderKey="registration.confirmation.placeholder" name="password_confirmation" errorKey={registrationForm.errors.password_confirmation} onBlur={event=>registrationForm.onBlur('password_confirmation',event)} type="password" value={confirmation} required autoComplete="new-password" maxLength={boot.passwordMaximumLength*2} disabled={busy||uncertain} onChange={e=>{setConfirmation(e.target.value);changed();}} /></>}
            <div className="notice-field"><label className="notice-ack"><input id="registration-notices" className="tn-checkbox" name="notices" type="checkbox" aria-invalid={noticeForm.errors.notices?true:undefined} aria-describedby={noticeForm.errors.notices?'registration-notices-error':undefined} onBlur={event=>noticeForm.onBlur('notices',event)} checked={notices} required disabled={busy||uncertain} onChange={e=>{setNotices(e.target.checked);changed();}} /><span className="notice-copy"><span className="notice-intro"><ContentText contentKey="registration.notices.ack" /></span><a className="notice-terms" href={boot.termsUri}><ContentText contentKey="registration.terms" /></a><span className="notice-and"><ContentText contentKey="registration.notices.and" /></span><a href={boot.privacyUri}><ContentText contentKey="registration.privacy" /></a></span></label>{noticeForm.errors.notices&&<FieldError id="registration-notices-error" contentKey={noticeForm.errors.notices} />}</div>
            <PrimaryButton presentation="spacious" type="submit" labelKey="registration.continue" disabled={busy||uncertain} aria-busy={busy} />
            {noticeForm.hasErrors&&<FormErrorSummary contentKey="registration.error.highlighted" />}
          </form>
        </>:stage==='personal'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="personal.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="personal.description" /></p>
          <form noValidate data-tunner-form="FORM-PERSONAL-INFORMATION" onSubmit={personal}>
            <TextField presentation="spacious" labelKey="personal.fullName.label" name="full_name" errorKey={personalForm.errors.full_name} onBlur={event=>personalForm.onBlur('full_name',event)} value={fullName} autoComplete="name" maxLength={operation?.personalInformation?operation.personalInformation.fullNameMaximumLength*2:undefined} disabled={busy||uncertain} onChange={e=>{setFullName(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="personal.displayName.label" helpKey={personalForm.errors.display_name?undefined:'personal.displayName.help'} name="display_name" errorKey={personalForm.errors.display_name} onBlur={event=>personalForm.onBlur('display_name',event)} value={displayName} autoComplete="nickname" maxLength={operation?.personalInformation?operation.personalInformation.displayNameMaximumLength*2:undefined} disabled={busy||uncertain} onChange={e=>{setDisplayName(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="personal.birth.label" name="date_of_birth" errorKey={personalForm.errors.date_of_birth} onBlur={event=>personalForm.onBlur('date_of_birth',event)} type="date" value={birth} required autoComplete="bday" disabled={busy||uncertain} onChange={e=>{setBirth(e.target.value);changed();}} />
            <TextField presentation="spacious" labelKey="personal.email.label" helpKey="personal.email.help" name="verified_email" type="email" value={operation?.personalInformation?.verifiedEmail??''} readOnly autoComplete="email" />
            <PrimaryButton presentation="spacious" type="submit" labelKey="registration.continue" disabled={busy||uncertain} aria-busy={busy} />
            {personalForm.hasErrors&&<FormErrorSummary contentKey="registration.error.highlighted" />}
          </form>
        </>:stage==='consent'?<>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="consent.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="consent.description" /></p>
          <PrimaryButton presentation="spacious" labelKey="consent.check" disabled={busy||uncertain} onClick={()=>{if(operation)void send('/registration/continue',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version});}} />
        </>:stage==='verify'?<>
          <span className="step"><ContentText contentKey="verification.step" /></span>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="verification.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="verification.description" /></p>
          <form noValidate data-tunner-form="FORM-VERIFY-EMAIL" onSubmit={verify} className="verification-form">
            <TextField presentation="verification" labelKey="verification.code.label" placeholderKey="verification.code.label" name="verification_code" errorKey={verificationForm.errors.verification_code} onBlur={event=>verificationForm.onBlur('verification_code',event)} value={code} required autoComplete="one-time-code" inputMode="numeric" maxLength={boot.proofDigits} disabled={busy||uncertain} onChange={e=>{setCode(e.target.value);changed();}} />
            <p className="expiry"><ContentText contentKey="verification.expiry" /></p>
            <PrimaryButton presentation="spacious" type="submit" labelKey="verification.submit" disabled={busy||uncertain||Boolean(expiry&&now>=expiry.valueOf())} aria-busy={busy} />
            <PrimaryButton presentation="spacious" appearance="outline" labelKey="verification.resend" disabled={resendDisabled||uncertain} onClick={()=>{if(operation)void send('/registration/resend',{operationId:operation.operationId,idempotencyKey:crypto.randomUUID(),expectedVersion:operation.version});}} />
            {verificationForm.hasErrors&&<FormErrorSummary contentKey="registration.error.highlighted" />}
          </form>
          <aside className="verification-note"><strong><ContentText contentKey="verification.note.heading" /></strong><p><ContentText contentKey="verification.note.description" /></p></aside>
          <p className="verification-back"><span className="wrong-email"><ContentText contentKey="verification.wrongEmail" /></span> <button type="button" className="text-action" disabled={busy||uncertain} onClick={()=>void restart()}><ContentText contentKey="verification.back" /></button></p>
        </>:<>
          <div className="verified-mark"><img src="/registration/assets/ready-imgEllipse1.svg" alt="" /><img className="approved-check" src="/registration/assets/ready-approved-check.svg" alt="" /></div>
          <h1 ref={heading} tabIndex={-1}><ContentText contentKey="registration.ready.heading" /></h1>
          <p className="subtitle"><ContentText contentKey="registration.ready.description" /></p>
          <aside className="verification-note"><strong><ContentText contentKey="registration.context.heading" /></strong><p><ContentText contentKey="registration.ready.boundary" /></p></aside>
          <a className="tn-primary-button registration-ready-sign-in" data-presentation="spacious" data-appearance="filled" href="/login"><ContentText contentKey="registration.ready.signIn"/></a>
          <p className="ready-help"><ContentText contentKey="registration.ready.next" /></p>
          {boot.supportUri&&<p className="ready-support"><span><ContentText contentKey="registration.ready.needHelp" /></span> <a href={boot.supportUri}><ContentText contentKey="registration.ready.contact" /></a></p>}
        </>}
        {busy&&<p role="status"><ContentText contentKey="registration.working" /></p>}
        {error&&<FormErrorSummary contentKey={error} />}
        {uncertain&&<><PrimaryButton presentation="spacious" appearance="outline" labelKey="registration.checkProgress" disabled={busy} onClick={()=>void recover()} /><button type="button" className="text-action" disabled={busy} onClick={()=>window.location.reload()}><ContentText contentKey="registration.startAgain" /></button></>}
      </section>
    </main>
    <footer className="registration-footer"><a href={boot.privacyUri}><ContentText contentKey="registration.privacy" /></a><img src={'/registration/assets/'+scene+'-imgEllipse.svg'} alt="" /><a href={boot.termsUri}><ContentText contentKey="registration.footer.terms" /></a>{boot.supportUri&&<><img src={'/registration/assets/'+scene+'-imgEllipse.svg'} alt="" /><a href={boot.supportUri}><ContentText contentKey="registration.footer.contact" /></a></>}</footer>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App />);
