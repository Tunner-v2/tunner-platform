import {createRoot} from 'react-dom/client';
import {useEffect,useId,useLayoutEffect,useRef,useState} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FormErrorSummary,ModalDialog,PrimaryButton,ThemeProvider,TunnerBrand,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './passkeys.css';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import {bootstrapSchema,matchesPasskeyStart,methodsSchema,parseOperation,passkeyErrorKey,PasskeyHttpError,requestJson,requirePublished,terminalCompleted} from './passkeys.api';
import type {PasskeyBootstrap,PasskeyMetadata,PasskeyMethods,PasskeyOperation,PasskeyStartSnapshot} from './passkeys.api';
import {requiredPasskeyKeys} from './passkeys.content-keys';
import {createPasskey,passkeysSupported} from './passkeys.native';
import {isPasskeyRoute,passkeyRoutes} from './passkeys.routes';
import {sessionRoutes} from './sessions.routes';
import {TotpMethodRow} from './totp.method-row';
import {RecoveryCodesSection} from './recovery-codes';
import {FederationManagement,federationManagementKeys} from './federation.management';

type Phase='review'|'proof'|'authorized'|'native'|'submitting'|'unknown'|'cancelled'|'expired';
type Scope={signal:AbortSignal;revision:number};
const acknowledged=new Set<string>(); // UX acknowledgement only; server facts and operation checks remain authoritative.
async function bootstrap(signal:AbortSignal){const value=bootstrapSchema.parse(await requestJson('/account/passkeys/options',{signal}));requirePublished(value.content,value.locale,requiredPasskeyKeys);return value;}
function PasskeyTime({value,locale}:{value:string;locale:string}){return <time dateTime={value}>{new Intl.DateTimeFormat(locale,{year:'numeric',month:'short',day:'2-digit',timeZone:'UTC'}).format(new Date(value))}</time>;}
function Metadata({credential,locale}:{credential:PasskeyMetadata;locale:string}){return <><p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.added"/> <PasskeyTime value={credential.createdAt} locale={locale}/></p><p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.lastUsed"/>: {credential.lastUsedAt?<PasskeyTime value={credential.lastUsedAt} locale={locale}/>:<ContentText contentKey="passkeys.methods.notUsed"/>}</p>{credential.revokedAt&&<p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.revokedAt"/> <PasskeyTime value={credential.revokedAt} locale={locale}/></p>}</>;}
function App(){return <ThemeProvider surface="user" mode="light"><Account/></ThemeProvider>;}
function Account(){
  const route=useRouteLocation();const revision=useRef(route.revision);revision.current=route.revision;
  const control=useRef(new AbortController());const flight=useRef(false);const operationRef=useRef<PasskeyOperation|undefined>(undefined);
  const starting=useRef<PasskeyStartSnapshot|undefined>(undefined);
  const recoveryBlocking=useRef(false);const [recoveryBlocked,setRecoveryBlocked]=useState(false);
  const federationBlocking=useRef(false);const [federationBlocked,setFederationBlocked]=useState(false);
  function recoveryOwnership(value:boolean){recoveryBlocking.current=value;setRecoveryBlocked(value);}
  const recoveryActions=useRef<HTMLDivElement>(null);const restoreRecoveryFocus=useRef(false);
  const trigger=useRef<HTMLElement|null>(null);const heading=useRef<HTMLHeadingElement>(null);const dialogHeading=useRef<HTMLHeadingElement>(null);const dialogId=useId();
  const [boot,setBoot]=useState<PasskeyBootstrap>();const [methods,setMethods]=useState<PasskeyMethods>();const [ready,setReady]=useState(-1);const [unavailable,setUnavailable]=useState(false);
  const [operation,setOperation]=useState<PasskeyOperation>();const [selected,setSelected]=useState<PasskeyMetadata>();const [modal,setModal]=useState<'ENROLL'|'REVOKE'>();
  const [phase,setPhase]=useState<Phase>('review');const [proof,setProof]=useState<StepUpBootstrap>();const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();
  const [notice,setNotice]=useState<string>();const [toast,setToast]=useState(false);const [ended,setEnded]=useState<'confirmed'|'unknown'>();const [menu,setMenu]=useState(false);
  const scope=():Scope=>({signal:control.current.signal,revision:route.revision});
  const current=(work:Scope)=>!work.signal.aborted&&control.current.signal===work.signal&&revision.current===work.revision;
  function own(value:PasskeyOperation|undefined){operationRef.current=value;setOperation(value);}
  function running(value:boolean){flight.current=value;setBusy(value);}
  const guarded=ready!==route.revision;const methodsPage=route.pathname===passkeyRoutes.methods.pathname;
  useEffect(()=>{
    restoreRecoveryFocus.current=false;
    federationBlocking.current=false;setFederationBlocked(false);
    control.current.abort();control.current=new AbortController();const work={signal:control.current.signal,revision:route.revision};
    running(false);setReady(-1);setProof(undefined);setModal(undefined);setSelected(undefined);setError(undefined);setNotice(undefined);setToast(false);setEnded(undefined);setUnavailable(false);setMenu(false);
    if(!isPasskeyRoute(route.pathname)){window.location.replace('/login');return()=>control.current.abort();}
    if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>control.current.abort();}
    void Promise.all([bootstrap(work.signal),requestJson('/account/passkeys',{signal:work.signal}).then(value=>methodsSchema.parse(value))]).then(async([options,list])=>{
      if(!current(work))return;setBoot(options);setMethods(list);const pending=starting.current;const candidate=options.recovery;const recovery=operationRef.current??(pending?(candidate&&matchesPasskeyStart(pending,candidate)?candidate:undefined):candidate);
      if(pending&&!operationRef.current&&!recovery){own(undefined);setPhase('unknown');setNotice('passkeys.operation.pending');setReady(work.revision);return;}
      if(pending&&recovery&&matchesPasskeyStart(pending,recovery))starting.current=undefined;
      if(recovery&&!acknowledged.has(recovery.ceremonyId)){
        own(recovery);setPhase('unknown');setNotice('passkeys.operation.pending');
        if(recovery.status==='COMPLETED'){
          try{const result=await readOriginal(recovery,work);if(result)await acceptResult(result,work);}
          catch(reason){if(current(work)){setError(passkeyErrorKey(reason));setPhase('unknown');}}
        }
      }else{own(undefined);setPhase('review');}
      if(current(work))setReady(work.revision);
    }).catch(reason=>{if(!current(work))return;if(reason instanceof PasskeyHttpError&&reason.status===401){window.location.replace('/login');return;}setUnavailable(true);setReady(work.revision);});
    return()=>work.signal.aborted?undefined:control.current.abort();
  },[route.revision]);
  useLayoutEffect(()=>{
    if(!restoreRecoveryFocus.current||guarded||busy||ended||modal||proof)return;
    restoreRecoveryFocus.current=false;
    if(operationRef.current||starting.current)recoveryActions.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
  },[guarded,busy,ended,modal,proof]);
  useEffect(()=>{if(!guarded&&!unavailable)heading.current?.focus();},[guarded,unavailable,methodsPage]);
  useEffect(()=>{if(modal&&!proof)dialogHeading.current?.focus();},[modal,proof,phase]);
  async function reloadMethods(work:Scope){const list=methodsSchema.parse(await requestJson('/account/passkeys',{signal:work.signal}));if(current(work))setMethods(list);}
  async function refreshFederationPublications(signal:AbortSignal){const ownerRevision=route.revision;const fresh=await bootstrap(signal);if(fresh.federationAvailable!==true)throw new Error('Unavailable');if(!signal.aborted&&revision.current===ownerRevision)setBoot(fresh);return {content:fresh.content,locale:fresh.locale};}
  async function terminal(original:PasskeyOperation,work:Scope){
    if(original.kind!=='REVOKE'||!original.resultHandle)return false;
    const anonymous=await requestJson('/authentication/options',{signal:work.signal}) as {csrfToken?:unknown};
    if(typeof anonymous.csrfToken!=='string'||!anonymous.csrfToken)throw new Error('Fresh CSRF unavailable.');
    const result=terminalCompleted.parse(await requestJson('/account/passkeys/terminal-result',{csrfToken:anonymous.csrfToken,body:{requestId:original.requestId,ceremonyId:original.ceremonyId,resultHandle:original.resultHandle},signal:work.signal}));
    if(result.requestId!==original.requestId||result.ceremonyId!==original.ceremonyId)throw new Error('The original result changed.');
    if(current(work)){acknowledged.add(original.ceremonyId);setProof(undefined);setModal(undefined);own(undefined);starting.current=undefined;setEnded('confirmed');setNotice(undefined);setError(undefined);}
    return true;
  }
  async function readOriginal(original:PasskeyOperation,work:Scope){
    try{const options=await bootstrap(work.signal);if(current(work))setBoot(options);return parseOperation(await requestJson('/account/passkeys/result',{csrfToken:options.csrfToken,body:{requestId:original.requestId,ceremonyId:original.ceremonyId},signal:work.signal}),original);}
    catch(reason){if(reason instanceof PasskeyHttpError&&reason.status===401){try{if(await terminal(original,work))return null;}catch{/* Missing exact terminal proof cannot become removal success. */}if(current(work))setEnded('unknown');}throw reason;}
  }
  async function acceptResult(result:PasskeyOperation,work:Scope){
    if(!current(work))return;
    const retained={...result,resultHandle:operationRef.current?.resultHandle??result.resultHandle};own(retained);
    if(result.status==='COMPLETED'){
      await reloadMethods(work);if(!current(work))return;acknowledged.add(result.ceremonyId);own(undefined);starting.current=undefined;setModal(undefined);setProof(undefined);setNotice(undefined);setError(undefined);setToast(true);return;
    }
    setProof(undefined);setNotice('passkeys.operation.pending');
    if(result.status==='AUTHORIZED'&&result.kind==='ENROLL'){setModal('ENROLL');setPhase('authorized');}
    else if(result.status==='EXPIRED'||result.status==='REJECTED'){setModal(result.kind==='REVOKE'?'REVOKE':'ENROLL');setPhase(result.status==='EXPIRED'?'expired':'cancelled');setNotice(undefined);}
    else{setPhase('unknown');}
  }
  async function recover(){
    if(flight.current||!boot)return;const work=scope();running(true);setError(undefined);
    try{let original=operationRef.current;if(!original&&starting.current){const fresh=await bootstrap(work.signal);const found=fresh.recovery;if(!found||!matchesPasskeyStart(starting.current,found))throw new Error('The original operation is not available.');if(current(work)){setBoot(fresh);own(found);starting.current=undefined;}original=found;}
      if(original){const result=await readOriginal(original,work);if(result)await acceptResult(result,work);}
    }catch(reason){if(current(work)){setError(passkeyErrorKey(reason));setPhase('unknown');}}finally{if(current(work))running(false);}
  }
  async function afterProof(){const original=operationRef.current;if(!original)return;const work=scope();setProof(undefined);running(true);setError(undefined);try{const result=await readOriginal(original,work);if(result)await acceptResult(result,work);}catch(reason){if(current(work)){setPhase('unknown');setError(passkeyErrorKey(reason));}}finally{if(current(work))running(false);}}
  async function resumeStart(){
    const previous=starting.current;if(!previous||!boot||flight.current)return;const work=scope();running(true);setError(undefined);
    try{const payload=previous.kind==='ENROLL'?{requestId:previous.requestId}:{requestId:previous.requestId,credentialReference:previous.credentialReference,expectedVersion:previous.expectedVersion};const result=parseOperation(await requestJson(previous.kind==='ENROLL'?'/account/passkeys/enrollment/start':'/account/passkeys/revocation/start',{csrfToken:boot.csrfToken,body:payload,signal:work.signal}));if(!matchesPasskeyStart(previous,result)||!result.resultHandle)throw new Error('The original request changed.');if(!current(work))return;own(result);starting.current=undefined;if(result.status==='PENDING')await launchProof(result,work);else await acceptResult(result,work);}
    catch(reason){if(current(work)){setPhase('unknown');setError(passkeyErrorKey(reason));}}finally{if(current(work))running(false);}
  }
  async function proofRecovery(signal:AbortSignal):Promise<StepUpCompletion|null>{
    const original=operationRef.current;if(!original)return null;const work={signal:control.current.signal,revision:route.revision};if(signal.aborted)return null;
    const result=await readOriginal(original,work);if(signal.aborted||!current(work))return null;
    if(result===null)return {status:'COMPLETED',signInRequired:true,operationReference:original.ceremonyId.replaceAll('-','')};
    if(result&&(result.status==='COMPLETED'||result.kind==='ENROLL'&&result.status==='AUTHORIZED'))return {status:'COMPLETED',signInRequired:false,operationReference:original.ceremonyId.replaceAll('-','')};
    return null;
  }
  async function launchProof(original:PasskeyOperation,work:Scope){
    const context=await loadStepUpOptions(work.signal);if(!current(work))return;
    if(context.operationReference!==original.ceremonyId.replaceAll('-',''))throw new PasskeyHttpError(409,'VERSION_CONFLICT');
    if(context.status==='COMPLETED'){const result=await readOriginal(original,work);if(result)await acceptResult(result,work);return;}
    setModal(undefined);setPhase('proof');setProof(context);
  }
  function review(kind:'ENROLL'|'REVOKE',credential?:PasskeyMetadata){if(flight.current||operationRef.current||starting.current||recoveryBlocking.current||federationBlocking.current)return;trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;setSelected(credential);setModal(kind);setPhase('review');setError(undefined);setNotice(undefined);setToast(false);}
  async function start(){
    if(!boot||!modal||flight.current||operationRef.current||starting.current||recoveryBlocking.current||federationBlocking.current)return;
    if(modal==='ENROLL'&&(!methods?.enrollmentAvailable||!passkeysSupported())){setError('passkeys.error.unsupported');return;}
    if(modal==='REVOKE'&&(!selected||selected.state!=='ACTIVE')){setError('passkeys.error.changed');return;}
    const work=scope();const kind=modal;const requestId=crypto.randomUUID();const snapshot:PasskeyStartSnapshot=Object.freeze({requestId,kind,credentialReference:kind==='REVOKE'?selected!.credentialReference:null,expectedVersion:kind==='REVOKE'?selected!.version:0});starting.current=snapshot;running(true);setError(undefined);
    try{const payload=kind==='ENROLL'?{requestId:snapshot.requestId}:{requestId:snapshot.requestId,credentialReference:snapshot.credentialReference,expectedVersion:snapshot.expectedVersion};const result=parseOperation(await requestJson(kind==='ENROLL'?'/account/passkeys/enrollment/start':'/account/passkeys/revocation/start',{csrfToken:boot.csrfToken,body:payload,signal:work.signal}));
      if(!matchesPasskeyStart(snapshot,result)||!result.resultHandle)throw new Error('The original operation changed.');
      if(!current(work))return;own(result);starting.current=undefined;if(result.status==='PENDING')await launchProof(result,work);else await acceptResult(result,work);
    }catch(reason){if(!current(work))return;setError(passkeyErrorKey(reason));setPhase('unknown');setNotice('passkeys.operation.pending');}
    finally{if(current(work))running(false);}
  }
  async function continueEnrollment(){
    const original=operationRef.current;if(!boot||!original||original.kind!=='ENROLL'||flight.current)return;const work=scope();running(true);setError(undefined);
    try{const options=parseOperation(await requestJson('/account/passkeys/enrollment/options',{csrfToken:boot.csrfToken,body:{requestId:original.requestId,ceremonyId:original.ceremonyId},signal:work.signal}),original);if(options.status!=='AUTHORIZED'||!options.publicKey)throw new PasskeyHttpError(409,'VERSION_CONFLICT');if(!current(work))return;setPhase('native');
      let credential:unknown;try{credential=await createPasskey(options.publicKey,boot.maximumCredentialJsonBytes,work.signal);}catch(reason){if(!current(work))return;if(reason instanceof DOMException&&['NotAllowedError','AbortError'].includes(reason.name)){await cancelOriginal(original,work);return;}throw reason;}
      if(!current(work))return;setPhase('submitting');const result=parseOperation(await requestJson('/account/passkeys/enrollment/complete',{csrfToken:boot.csrfToken,body:{requestId:original.requestId,ceremonyId:original.ceremonyId,credential},signal:work.signal}),original);await acceptResult(result,work);
    }catch(reason){if(current(work)){setError(passkeyErrorKey(reason));setPhase('unknown');setNotice('passkeys.setup.unknown');}}
    finally{if(current(work))running(false);}
  }
  async function cancelOriginal(original:PasskeyOperation,work:Scope){if(!boot)return;const result=parseOperation(await requestJson('/account/passkeys/cancel',{csrfToken:boot.csrfToken,body:{requestId:original.requestId,ceremonyId:original.ceremonyId},signal:work.signal}),original);await acceptResult(result,work);}
  async function cancel(){
    if(flight.current)return;const original=operationRef.current;
    if(!original){if(starting.current){restoreRecoveryFocus.current=true;setModal(undefined);setNotice('passkeys.operation.pending');return;}setModal(undefined);setError(undefined);return;}
    if(phase==='unknown'){restoreRecoveryFocus.current=true;setProof(undefined);setModal(undefined);setNotice('passkeys.operation.pending');return;}
    if(phase==='cancelled'||phase==='expired'){acknowledged.add(original.ceremonyId);own(undefined);setModal(undefined);setError(undefined);setNotice(undefined);return;}
    const work=scope();running(true);setProof(undefined);try{await cancelOriginal(original,work);}catch(reason){if(current(work)){setPhase('unknown');setNotice('passkeys.operation.pending');setError(passkeyErrorKey(reason));}}finally{if(current(work))running(false);}
  }
  async function resumeProof(){const original=operationRef.current;if(!original||flight.current)return;const work=scope();running(true);setError(undefined);try{await launchProof(original,work);}catch(reason){if(current(work)){setError(passkeyErrorKey(reason));setPhase('unknown');}}finally{if(current(work))running(false);}}
  if(unavailable)return <ContentUnavailable/>;
  if(guarded||!boot||!methods)return <main className="passkeys-load" aria-busy="true"/>;
  if(boot.federationAvailable===true){try{requirePublished(boot.content,boot.locale,federationManagementKeys);}catch{return <ContentUnavailable/>;}}
  const active=methods.credentials.filter(value=>value.state==='ACTIVE');const passkeyBlocked=busy||!!operation||!!starting.current;const blocked=passkeyBlocked||recoveryBlocked||federationBlocked;const selectedFact=selected??methods.credentials.find(value=>value.credentialReference===operation?.credentialReference);
  const federationModule=(standalone:boolean)=><FederationManagement key={route.revision} standalone={standalone} disabled={passkeyBlocked||recoveryBlocked||!!modal||!!proof} content={boot.content} locale={boot.locale} isCurrent={()=>revision.current===route.revision} onBlockedChange={value=>{if(revision.current!==route.revision)return;federationBlocking.current=value;setFederationBlocked(value);}} fallbackFocus={()=>heading.current} refreshPublications={refreshFederationPublications}/>;
  const errorNode=error?<FormErrorSummary contentKey={error}/>:null;
  const addButton=<PrimaryButton labelKey="passkeys.setup.submit" disabled={blocked||!methods.enrollmentAvailable||!passkeysSupported()} onClick={()=>review('ENROLL')}/>;
  const dialogKind=modal??(operation?.kind==='REVOKE'?'REVOKE':'ENROLL');
  return <ContentProvider locale={boot.locale} audience="account-security" resolve={key=>boot.content[key]}>
    <div className="passkeys-page">
      <header className="passkeys-header"><TunnerBrand contentKey="brand.tunner"/><span className="passkeys-header-label"><ContentText contentKey="passkeys.header.accountCenter"/></span><div className="passkeys-search" aria-hidden="true"><ContentText contentKey="passkeys.header.search"/></div><span className="passkeys-notifications" role="img" aria-label={boot.content['passkeys.header.notifications']!.text}>•</span><span className="passkeys-owner"><ContentText contentKey="passkeys.header.account"/></span><button type="button" className="passkeys-menu" aria-label={boot.content['passkeys.header.menu']!.text} aria-expanded={menu} aria-controls="passkeys-navigation" onClick={()=>setMenu(value=>!value)}>≡</button></header>
      {ended?<main className="passkeys-owner-result" data-tunner-screen={methodsPage?passkeyRoutes.methods.id:passkeyRoutes.security.id}><h1 ref={heading} tabIndex={-1}><ContentText contentKey={ended==='confirmed'?'passkeys.revoke.signInRequired':'passkeys.session.ended'}/></h1><a href="/login"><ContentText contentKey="passkeys.operation.signIn"/></a></main>:<div className="passkeys-shell">
        <nav className={'passkeys-sidebar'+(menu?' open':'')} id="passkeys-navigation" aria-label={boot.content['passkeys.header.menu']!.text}><p className="passkeys-nav-label"><ContentText contentKey="passkeys.navigation.account"/></p>{['overview','profile','security','sessions','billing','notifications','products','activity'].map(item=>item==='security'?<a key={item} className="passkeys-nav-item active" href={passkeyRoutes.security.pathname} aria-current="page" onClick={event=>{event.preventDefault();navigatePath(passkeyRoutes.security.pathname);}}><ContentText contentKey={'passkeys.navigation.'+item}/></a>:item==='sessions'?<a key={item} className="passkeys-nav-item" href={sessionRoutes.list.pathname}><ContentText contentKey={'passkeys.navigation.'+item}/></a>:<span className="passkeys-nav-item" key={item}><ContentText contentKey={'passkeys.navigation.'+item}/></span>)}<div className="passkeys-nav-help"><p className="passkeys-nav-label"><ContentText contentKey="passkeys.navigation.help"/></p><p><ContentText contentKey="passkeys.navigation.helpDescription"/></p></div></nav>
        <main className="passkeys-main" data-tunner-screen={methodsPage?passkeyRoutes.methods.id:passkeyRoutes.security.id}>
          <p className="passkeys-eyebrow"><ContentText contentKey={methodsPage?'passkeys.eyebrow':'passkeys.security.eyebrow'}/></p><h1 ref={heading} tabIndex={-1}><ContentText contentKey={methodsPage?'passkeys.heading':'passkeys.security.heading'}/></h1><p className="passkeys-subtitle"><ContentText contentKey={methodsPage?'passkeys.description':'passkeys.security.description'}/></p>
          <section className={'passkeys-summary'+(!methodsPage?' has-action':'')}><p className="passkeys-eyebrow"><ContentText contentKey="passkeys.summary.label"/></p><h2><ContentText contentKey={active.length?'passkeys.summary.active':'passkeys.summary.empty'}/></h2><p><ContentText contentKey={active.length?'passkeys.summary.activeDescription':'passkeys.summary.emptyDescription'}/></p>{!methodsPage&&<div className="passkeys-summary-action">{addButton}</div>}</section>
          {methodsPage?<div className="passkeys-cards"><section className="passkeys-card passkeys-methods"><h2><ContentText contentKey="passkeys.methods.heading"/></h2><p className="passkeys-card-intro"><ContentText contentKey="passkeys.methods.description"/></p>{methods.credentials.length?methods.credentials.map(credential=><div className="passkeys-method-row" key={credential.credentialReference} role="group" aria-label={boot.content['passkeys.methods.label']!.text}><div><p className="passkeys-method-title"><ContentText contentKey="passkeys.methods.label"/></p><Metadata credential={credential} locale={boot.locale}/></div><span className={'passkeys-pill'+(credential.state==='REVOKED'?' revoked':'')}><ContentText contentKey={credential.state==='ACTIVE'?'passkeys.status.active':'passkeys.status.revoked'}/></span>{credential.state==='ACTIVE'&&<PrimaryButton labelKey="passkeys.methods.manage" appearance="outline" disabled={blocked} onClick={()=>review('REVOKE',credential)}/>}</div>):<div className="passkeys-method-row"><div><p className="passkeys-method-title"><ContentText contentKey="passkeys.methods.plural"/></p><p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.empty"/></p></div></div>}{methods.passwordAvailable&&<div className="passkeys-method-row" role="group" aria-label={boot.content['passkeys.methods.password']!.text}><div><p className="passkeys-method-title"><ContentText contentKey="passkeys.methods.password"/></p><p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.passwordAvailable"/></p></div></div>}<TotpMethodRow disabled={blocked} locale={boot.locale} available={boot.totpAvailable===true}/>{boot.federationAvailable===true&&federationModule(false)}<p className="passkeys-status"><ContentText contentKey="passkeys.methods.otherUnavailable"/></p><div className="passkeys-page-actions">{addButton}</div></section><Guidance/></div>:<div className="passkeys-security-grid"><section className="passkeys-card"><h2><ContentText contentKey="passkeys.methods.heading"/></h2><p><ContentText contentKey={active.length?'passkeys.summary.active':'passkeys.methods.empty'}/></p>{methods.passwordAvailable&&<div className="passkeys-method-row" role="group" aria-label={boot.content['passkeys.methods.password']!.text}><div><p className="passkeys-method-title"><ContentText contentKey="passkeys.methods.password"/></p><p className="passkeys-method-detail"><ContentText contentKey="passkeys.methods.passwordAvailable"/></p></div></div>}<a className="passkeys-inline-link" href={passkeyRoutes.methods.pathname} onClick={event=>{event.preventDefault();navigatePath(passkeyRoutes.methods.pathname);}}><ContentText contentKey="passkeys.methods.review"/></a>{boot.federationAvailable===true&&federationModule(false)}</section><Guidance/><section className="passkeys-card"><h2><ContentText contentKey="passkeys.activity.heading"/></h2><p><ContentText contentKey="passkeys.activity.unavailable"/></p></section>{boot.recoveryCodesAvailable===true?<RecoveryCodesSection disabled={passkeyBlocked||federationBlocked||!!modal||!!proof} onBlockedChange={recoveryOwnership} fallbackFocus={()=>heading.current}/>:<section className="passkeys-card"><h2><ContentText contentKey="passkeys.other.heading"/></h2><p><ContentText contentKey="passkeys.other.unavailable"/></p></section>}</div>}
          {!passkeysSupported()&&<p className="passkeys-status" role="status"><ContentText contentKey="passkeys.error.unsupported"/></p>}{!methods.enrollmentAvailable&&<p className="passkeys-status" role="status"><ContentText contentKey="passkeys.error.limit"/></p>}
          {notice&&<p className="passkeys-status" role="status"><ContentText contentKey={notice}/></p>}{(operation||starting.current)&&<div ref={recoveryActions} className="passkeys-page-actions"><PrimaryButton labelKey="passkeys.operation.check" appearance="outline" disabled={busy} onClick={()=>void recover()}/>{starting.current&&!operation&&<PrimaryButton labelKey="passkeys.operation.retry" disabled={busy} onClick={()=>void resumeStart()}/>} {operation?.status==='PENDING'&&<PrimaryButton labelKey="passkeys.operation.resume" disabled={busy} onClick={()=>void resumeProof()}/>}</div>}{!modal&&!proof&&errorNode}
        </main>
      </div>}
      {toast&&!ended&&<div className="passkeys-toast" role="status"><span className="passkeys-toast-mark" aria-hidden="true">✓</span><strong><ContentText contentKey="passkeys.success.heading"/></strong><p><ContentText contentKey="passkeys.success.description"/></p></div>}
      {modal&&!proof&&!ended&&<ModalDialog labelledBy={dialogId+'-title'} describedBy={dialogId+'-description'} returnFocusTo={trigger.current} busy={busy} onRequestClose={()=>void cancel()} className={'passkeys-dialog'+(dialogKind==='REVOKE'?' remove':'')+(phase!=='review'?' feedback':'')}><div className="passkeys-dialog-content" data-tunner-component={dialogKind==='ENROLL'?'U-AUTH-05':'COMP-PASSKEY-REVOKE'}><h2 id={dialogId+'-title'} ref={dialogHeading} tabIndex={-1}><ContentText contentKey={dialogKind==='ENROLL'?'passkeys.setup.heading':'passkeys.revoke.heading'}/></h2><p id={dialogId+'-description'} className="passkeys-dialog-description"><ContentText contentKey={dialogKind==='ENROLL'?'passkeys.setup.description':'passkeys.revoke.description'}/></p>{dialogKind==='ENROLL'?<div className="passkeys-device"><h3><ContentText contentKey="passkeys.setup.deviceHeading"/></h3><p className="passkeys-device-name"><ContentText contentKey="passkeys.setup.device"/></p><p><ContentText contentKey="passkeys.setup.deviceDescription"/></p></div>:<><p className="passkeys-impact"><ContentText contentKey={boot.revocationSessionEffect==='PreserveExistingSessions'?'passkeys.revoke.preserve':'passkeys.revoke.endSessions'}/></p>{selectedFact&&<div className="passkeys-selected-metadata"><Metadata credential={selectedFact} locale={boot.locale}/></div>}</>}
        {['native','submitting'].includes(phase)&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey="passkeys.setup.pending"/></p>}{phase==='unknown'&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey={dialogKind==='REVOKE'?'passkeys.revoke.unknown':'passkeys.setup.unknown'}/></p>}{phase==='cancelled'&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey={dialogKind==='REVOKE'?'passkeys.revoke.notCompleted':'passkeys.setup.cancelled'}/></p>}{phase==='expired'&&<p className="passkeys-dialog-feedback" role="status"><ContentText contentKey="passkeys.setup.expired"/></p>}{errorNode}
        <div className="passkeys-dialog-actions"><PrimaryButton labelKey={phase==='unknown'||phase==='cancelled'||phase==='expired'?'passkeys.operation.close':dialogKind==='REVOKE'?'passkeys.revoke.cancel':'passkeys.setup.cancel'} appearance="outline" disabled={busy} onClick={()=>void cancel()}/>{phase==='review'?<span className={dialogKind==='REVOKE'?'passkeys-danger-wrap':''}><PrimaryButton labelKey={dialogKind==='REVOKE'?'passkeys.revoke.submit':'passkeys.setup.submit'} disabled={busy||dialogKind==='ENROLL'&&!passkeysSupported()} onClick={()=>void start()}/></span>:phase==='authorized'?<PrimaryButton labelKey="passkeys.operation.resume" disabled={busy||!passkeysSupported()} onClick={()=>void continueEnrollment()}/>:phase==='unknown'?<PrimaryButton labelKey="passkeys.operation.check" disabled={busy} onClick={()=>void recover()}/>:null}</div></div></ModalDialog>}
    </div>
    {proof&&!ended&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={proofRecovery} onCancel={outcome=>{if(outcome?.unresolved){restoreRecoveryFocus.current=true;setProof(undefined);setPhase('unknown');setNotice('passkeys.operation.pending');}else void cancel();}} onComplete={()=>void afterProof()}/>}
  </ContentProvider>;
}
function Guidance(){return <aside className="passkeys-card passkeys-guidance"><h2><ContentText contentKey="passkeys.guidance.heading"/></h2><p><ContentText contentKey="passkeys.guidance.description"/></p><div className="passkeys-helpbox"><strong><ContentText contentKey="passkeys.guidance.control"/></strong><p><ContentText contentKey="passkeys.guidance.confirm"/></p></div><p><ContentText contentKey="passkeys.guidance.native"/></p></aside>;}
createRoot(document.getElementById('root')!).render(<App/>);
