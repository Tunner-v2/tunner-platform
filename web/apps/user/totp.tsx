import {createRoot} from 'react-dom/client';
import {flushSync} from 'react-dom';
import {useEffect,useRef,useState} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FormErrorSummary,ModalDialog,PrimaryButton,ThemeProvider,TunnerBrand,navigatePath,useRouteLocation,useTunnerForm} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './passkeys.css';
import './authentication.css';
import './totp.css';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import {loadTotpMethods,loadTotpOptions,loadTotpTerminalOptions,matchesTotpStart,parseTotpDisclosure,parseTotpOperation,parseTotpTerminal,requestTotpJson,TotpHttpError,totpErrorKey} from './totp.api';
import type {TotpBootstrap,TotpDisclosure,TotpMetadata,TotpMethods,TotpOperation,TotpStartSnapshot,TotpTerminalOptions} from './totp.api';
import {isTotpAccountRoute,totpRoutes} from './totp.routes';
import {totpCodeSchema} from './totp.validation';
import {TotpAccountShell,TotpManagement,TotpReady,TotpSetup} from './totp.ui';
import type {TotpSetupMode} from './totp.ui';

type Work={signal:AbortSignal;revision:number};
const acknowledged=new Set<string>(); // Local acknowledgement only; never operation/result authority.
const body=(operation:TotpOperation)=>({requestId:operation.requestId,enrollmentId:operation.enrollmentId});
function App(){return <ThemeProvider surface="user" mode="light"><Account/></ThemeProvider>;}
function EndedResult({presentation,value}:{presentation:TotpTerminalOptions;value:TotpOperation}){return <ContentProvider locale={presentation.locale} audience="totp-terminal" resolve={key=>presentation.content[key]}><div className="authentication-page"><img className="authentication-glow indigo" src="/registration/assets/login-bg-indigo.svg" alt=""/><img className="authentication-glow violet" src="/registration/assets/login-bg-violet.svg" alt=""/><header className="authentication-header"><TunnerBrand contentKey="brand.tunner"/></header><main className="authentication-stage"><section className="authentication-card totp-terminal"><h1 id="totp-page-heading" tabIndex={-1}><ContentText contentKey="totp.terminal.endedHeading"/></h1><h2><ContentText contentKey={value.kind==='RESET'?'totp.terminal.resetHeading':'totp.terminal.disableHeading'}/></h2><p><ContentText contentKey="totp.terminal.endedDescription"/></p>{value.kind==='RESET'&&<p><ContentText contentKey="totp.terminal.resetDescription"/></p>}<a className="tn-primary-button" href="/login"><ContentText contentKey="totp.action.signIn"/></a></section></main></div></ContentProvider>;}
function Account(){
 const route=useRouteLocation(),revision=useRef(route.revision);revision.current=route.revision;
 const control=useRef(new AbortController()),flight=useRef(false),original=useRef<TotpOperation|undefined>(undefined),starting=useRef<TotpStartSnapshot|undefined>(undefined);
 const attempt=useRef<{enrollmentId:string;attemptId:string}|undefined>(undefined),trigger=useRef<HTMLElement|null>(null);
 const [boot,setBoot]=useState<TotpBootstrap>(),[methods,setMethods]=useState<TotpMethods>(),[operation,setOperation]=useState<TotpOperation>();
 const [ready,setReady]=useState(-1),[refresh,setRefresh]=useState(0),[unavailable,setUnavailable]=useState(false),[busy,setBusy]=useState(false);
 const [mode,setMode]=useState<TotpSetupMode>('unknown'),[disclosure,setDisclosure]=useState<TotpDisclosure>(),[code,setCode]=useState('');
 const [error,setError]=useState<string>(),[serverField,setServerField]=useState<string>(),[proof,setProof]=useState<StepUpBootstrap>(),[review,setReview]=useState(false);
 const [terminal,setTerminal]=useState<TotpOperation>(),[ended,setEnded]=useState(false),[unresolved,setUnresolved]=useState(false);
 const [terminalPresentation,setTerminalPresentation]=useState<TotpTerminalOptions>();
 const form=useTunnerForm(totpCodeSchema(),{code});
 const guarded=ready!==route.revision;
 const work=():Work=>({signal:control.current.signal,revision:route.revision});
 const current=(scope:Work)=>!scope.signal.aborted&&scope.signal===control.current.signal&&scope.revision===revision.current;
 function running(value:boolean){flight.current=value;setBusy(value);}
 function own(value:TotpOperation|undefined){original.current=value;setOperation(value);}
 function removeSensitive(){setDisclosure(undefined);setCode('');}

 useEffect(()=>{
  control.current.abort();control.current=new AbortController();const scope={signal:control.current.signal,revision:route.revision};
  running(false);setReady(-1);removeSensitive();form.reset();setServerField(undefined);setError(undefined);setProof(undefined);setReview(false);setUnavailable(false);
  if(!isTotpAccountRoute(route.pathname)){window.location.replace('/login');return()=>control.current.abort();}
  if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>control.current.abort();}
  void Promise.all([loadTotpOptions(scope.signal),loadTotpMethods(scope.signal)]).then(async([options,list])=>{
   if(!current(scope))return;setBoot(options);setMethods(list);
   const retained=original.current;const pending=starting.current;let recovered=options.recovery??undefined;
   if(retained)recovered=parseTotpOperation(await requestTotpJson('/account/totp/result',{csrfToken:options.csrfToken,body:body(retained),signal:scope.signal}),retained);
   else if(pending&&recovered&&!matchesTotpStart(pending,recovered))recovered=undefined;
   if(!current(scope))return;
   if(recovered&&acknowledged.has(recovered.enrollmentId)&&route.pathname===totpRoutes.manage.pathname)recovered=undefined;
   own(recovered);if(recovered&&pending&&matchesTotpStart(pending,recovered))starting.current=undefined;
   setUnresolved(Boolean(starting.current));setReady(scope.revision);
   if(route.pathname===totpRoutes.setup.pathname){
    if(!recovered||recovered.kind!=='ENROLL'){navigatePath(totpRoutes.manage.pathname,true);return;}
    await presentSetup(recovered,scope,options);
   }else if(route.pathname===totpRoutes.ready.pathname){
    if(!recovered||recovered.kind!=='ENROLL'||recovered.status!=='COMPLETED'||!recovered.activationCompleted||!list.authenticators.some(f=>f.authenticatorReference===recovered!.authenticatorReference&&f.state==='ACTIVE'))navigatePath(totpRoutes.manage.pathname,true);
   }
  }).catch(async reason=>{if(!current(scope))return;if(reason instanceof TotpHttpError&&reason.status===401){try{const options=await loadTotpTerminalOptions(scope.signal);const result=parseTotpTerminal(await requestTotpJson('/account/totp/terminal-result',{csrfToken:options.csrfToken,body:body(options.recovery),signal:scope.signal}),options.recovery);if(current(scope)){own(result.operation);setTerminalPresentation(options);setTerminal(result.operation);setEnded(true);setReady(scope.revision);}return;}catch{if(current(scope))window.location.replace('/login');return;}}setUnavailable(true);setReady(scope.revision);});
  return()=>control.current.abort();
 },[route.revision,refresh]);
 useEffect(()=>{
  const hide=()=>{control.current.abort();flushSync(()=>{removeSensitive();setProof(undefined);setReady(-1);});};
  const show=(event:PageTransitionEvent)=>{if(event.persisted)setRefresh(value=>value+1);};
  window.addEventListener('pagehide',hide);window.addEventListener('pageshow',show);return()=>{window.removeEventListener('pagehide',hide);window.removeEventListener('pageshow',show);};
 },[]);
 useEffect(()=>{if(!guarded&&!proof&&!review)document.getElementById('totp-page-heading')?.focus();},[guarded,proof,review,mode]);
 useEffect(()=>{if(review&&!proof)document.getElementById('totp-review-title')?.focus();},[review,proof]);
 useEffect(()=>{if(serverField&&!busy)document.getElementById('totp-setup-code')?.focus();},[serverField,busy]);

 async function terminalResult(value:TotpOperation,scope:Work){
  const options=await loadTotpTerminalOptions(scope.signal);
  const result=parseTotpTerminal(await requestTotpJson('/account/totp/terminal-result',{csrfToken:options.csrfToken,body:body(value),signal:scope.signal}),value);
  if(current(scope)){removeSensitive();setProof(undefined);setReview(false);own(result.operation);setTerminalPresentation(options);setTerminal(result.operation);setEnded(true);setUnresolved(false);setError(undefined);}return result.operation;
 }
 async function readOriginal(value:TotpOperation,scope:Work){
  try{const options=await loadTotpOptions(scope.signal);if(current(scope))setBoot(options);return parseTotpOperation(await requestTotpJson('/account/totp/result',{csrfToken:options.csrfToken,body:body(value),signal:scope.signal}),value);}
  catch(reason){if(reason instanceof TotpHttpError&&reason.status===401)return terminalResult(value,scope);throw reason;}
 }
 async function accept(value:TotpOperation,scope:Work){
  if(!current(scope))return;own(value);setUnresolved(false);setProof(undefined);removeSensitive();
  if(value.status==='COMPLETED'){
   if(value.currentSessionEnds){await terminalResult(value,scope);return;}
   const list=await loadTotpMethods(scope.signal);if(!current(scope))return;setMethods(list);setReview(false);
   if(value.kind==='ENROLL'){
    if(!value.activationCompleted||!list.authenticators.some(f=>f.authenticatorReference===value.authenticatorReference&&f.state==='ACTIVE'))throw new TotpHttpError(409,'VERSION_CONFLICT');
    navigatePath(totpRoutes.ready.pathname);
   }else setTerminal(value);
  }else if(value.kind==='ENROLL'&&['AUTHORIZED','PREPARING','AWAITING_FIRST_CODE'].includes(value.status)){
   if(route.pathname!==totpRoutes.setup.pathname)navigatePath(totpRoutes.setup.pathname);else await presentSetup(value,scope,boot!);
  }else if(value.status==='EXPIRED'||value.status==='REJECTED'){setMode('expired');setReview(false);setError('totp.error.changed');}
 }
 async function presentSetup(value:TotpOperation,scope:Work,options:TotpBootstrap){
  if(!current(scope))return;own(value);removeSensitive();
  if(value.status==='COMPLETED'){await accept(value,scope);return;}
  if(value.status==='AWAITING_FIRST_CODE'&&value.disclosureConsumed){setMode('consumed');return;}
  if(value.status==='EXPIRED'||value.status==='REJECTED'){setMode('expired');return;}
  if((value.status==='AUTHORIZED'||value.status==='PREPARING')&&value.disclosureAvailable){
   setMode('preparing');running(true);
   try{const shown=parseTotpDisclosure(await requestTotpJson('/account/totp/enrollment/disclose',{csrfToken:options.csrfToken,body:body(value),signal:scope.signal}),value);if(current(scope)){setDisclosure(shown);setMode('disclosed');own({...value,status:'AWAITING_FIRST_CODE',disclosureAvailable:false,disclosureConsumed:true});}}
   catch(reason){if(!current(scope))return;removeSensitive();if(reason instanceof TotpHttpError&&reason.code==='DISCLOSURE_UNAVAILABLE'){const result=await readOriginal(value,scope);if(current(scope)){own(result);setMode(result.status==='AWAITING_FIRST_CODE'?'consumed':'unknown');}}else{setMode('unknown');setUnresolved(true);setError(totpErrorKey(reason));}}
   finally{if(current(scope))running(false);}return;
  }
  setMode('unknown');setUnresolved(true);
 }
 async function launchProof(value:TotpOperation,scope:Work){
  const result=await loadStepUpOptions(scope.signal);if(!current(scope))return;
  if(result.operationReference!==value.enrollmentId.replaceAll('-',''))throw new TotpHttpError(409,'VERSION_CONFLICT');
  if(result.status==='COMPLETED'){await accept(await readOriginal(value,scope),scope);return;}
  setReview(false);setProof(result);
 }
 async function start(kind:'ENROLL'|'DISABLE'|'RESET',factor?:TotpMetadata){
  if(!boot||flight.current||original.current||starting.current)return;
  if(kind==='ENROLL'?!boot.canStartEnrollment:!factor||factor.state!=='ACTIVE'||(kind==='DISABLE'?!boot.canStartDisable:!boot.canStartReset))return;
  trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;
  const snapshot:TotpStartSnapshot=Object.freeze({requestId:crypto.randomUUID(),kind,authenticatorReference:factor?.authenticatorReference??null,expectedVersion:factor?.version??0});starting.current=snapshot;
  const scope=work();running(true);setError(undefined);setTerminal(undefined);
  try{const value=parseTotpOperation(await requestTotpJson('/account/totp/'+(kind==='ENROLL'?'enrollment':kind.toLowerCase())+'/start',{csrfToken:boot.csrfToken,body:{requestId:snapshot.requestId,authenticatorReference:snapshot.authenticatorReference,expectedVersion:snapshot.expectedVersion},signal:scope.signal}));if(!matchesTotpStart(snapshot,value))throw new TotpHttpError(409,'VERSION_CONFLICT');if(!current(scope))return;own(value);starting.current=undefined;setUnresolved(false);if(value.status==='PENDING_PROOF'){if(kind==='ENROLL')await launchProof(value,scope);else setReview(true);}else await accept(value,scope);}
  catch(reason){if(current(scope)){setUnresolved(true);setError(totpErrorKey(reason));}}finally{if(current(scope))running(false);}
 }
 async function check(){
  if(!boot||flight.current)return;const scope=work();running(true);setError(undefined);
  try{let value=original.current;if(!value&&starting.current){value=parseTotpOperation(await requestTotpJson('/account/totp/start-result',{csrfToken:boot.csrfToken,body:starting.current,signal:scope.signal}));if(!matchesTotpStart(starting.current,value))throw new TotpHttpError(409,'VERSION_CONFLICT');if(current(scope)){own(value);starting.current=undefined;}}
   if(value){value=await readOriginal(value,scope);if(!current(scope))return;if(value.status==='PENDING_PROOF'){own(value);setReview(true);setUnresolved(false);}else await accept(value,scope);}
  }catch(reason){if(current(scope)){setUnresolved(true);removeSensitive();setMode('unknown');setError(totpErrorKey(reason));}}finally{if(current(scope))running(false);}
 }
 async function cancel(){
  if(flight.current)return;const value=original.current;removeSensitive();setProof(undefined);
  if(!value||unresolved){setReview(false);navigatePath(totpRoutes.manage.pathname);return;}
  if(value.status==='COMPLETED'){done();return;}if(!boot)return;const scope=work();running(true);setError(undefined);
  try{const result=parseTotpOperation(await requestTotpJson('/account/totp/cancel',{csrfToken:boot.csrfToken,body:body(value),signal:scope.signal}),value);if(!current(scope))return;if(result.status==='COMPLETED'){await accept(result,scope);return;}if(result.status!=='REJECTED'&&result.status!=='EXPIRED')throw new TotpHttpError(409,'VERSION_CONFLICT');acknowledged.add(result.enrollmentId);own(undefined);starting.current=undefined;setReview(false);setUnresolved(false);navigatePath(totpRoutes.manage.pathname);}
  catch(reason){if(current(scope)){setUnresolved(true);setError(totpErrorKey(reason));}}finally{if(current(scope))running(false);}
 }
 async function submitCode(data:{code:string}){
  const value=original.current;if(!boot||!value||value.kind!=='ENROLL'||flight.current)return;const scope=work();running(true);removeSensitive();setMode('verifying');setError(undefined);setServerField(undefined);
  if(attempt.current?.enrollmentId!==value.enrollmentId)attempt.current={enrollmentId:value.enrollmentId,attemptId:crypto.randomUUID()};
  try{const result=parseTotpOperation(await requestTotpJson('/account/totp/enrollment/activate',{csrfToken:boot.csrfToken,body:{...body(value),attemptId:attempt.current.attemptId,code:data.code},signal:scope.signal}),value);await accept(result,scope);}
  catch(reason){if(!current(scope))return;if(reason instanceof TotpHttpError&&(reason.code==='AUTHENTICATION_FAILED'||reason.code==='CODE_REPLAYED'||reason.code==='VALIDATION_ERROR')){setMode('consumed');setServerField(reason.code==='VALIDATION_ERROR'?'totp.error.code':reason.code==='CODE_REPLAYED'?'totp.error.replayed':'totp.error.credentials');}else{try{await accept(await readOriginal(value,scope),scope);}catch{if(current(scope)){setMode('unknown');setUnresolved(true);setError(totpErrorKey(reason));}}}}
  finally{if(current(scope)){setCode('');form.reset();running(false);}}
 }
 function done(){const value=original.current;if(value)acknowledged.add(value.enrollmentId);own(undefined);starting.current=undefined;attempt.current=undefined;setTerminal(undefined);setUnresolved(false);setError(undefined);navigatePath(totpRoutes.manage.pathname);if(route.pathname===totpRoutes.manage.pathname)setRefresh(value=>value+1);}
 async function afterProof(){const value=original.current;if(!value)return;const scope=work();setProof(undefined);running(true);try{await accept(await readOriginal(value,scope),scope);}catch(reason){if(current(scope)){setUnresolved(true);setError(totpErrorKey(reason));}}finally{if(current(scope))running(false);}}
 async function proofRecovery(signal:AbortSignal):Promise<StepUpCompletion|null>{const value=original.current;if(!value||signal.aborted)return null;const scope=work();const result=await readOriginal(value,scope);if(signal.aborted||!current(scope))return null;if(result.status==='COMPLETED'||result.kind==='ENROLL'&&result.status==='AUTHORIZED')return {status:'COMPLETED',signInRequired:result.currentSessionEnds,operationReference:value.enrollmentId.replaceAll('-','')};return null;}

 if(unavailable)return <ContentUnavailable/>;
 if(guarded)return <main className="passkeys-load" aria-busy="true"/>;
 if(ended)return terminalPresentation&&terminal?<EndedResult presentation={terminalPresentation} value={terminal}/>:<ContentUnavailable/>;
 if(!boot||!methods)return <main className="passkeys-load" aria-busy="true"/>;
 const setup=route.pathname===totpRoutes.setup.pathname,readyPage=route.pathname===totpRoutes.ready.pathname;
 const blocked=busy||Boolean(operation)||Boolean(starting.current);
 const content=<>{terminal?<section className="totp-panel totp-terminal"><h2><ContentText contentKey={terminal.kind==='RESET'?'totp.terminal.resetHeading':'totp.terminal.disableHeading'}/></h2><p><ContentText contentKey={ended?'totp.terminal.endedDescription':'totp.terminal.preserveDescription'}/></p>{terminal.kind==='RESET'&&<p><ContentText contentKey="totp.terminal.resetDescription"/></p>}<div className="totp-actions">{ended?<a className="tn-primary-button" href="/login"><ContentText contentKey="totp.action.signIn"/></a>:<PrimaryButton labelKey="totp.action.done" onClick={done}/>}</div></section>:readyPage?<TotpReady onDone={done}/>:setup?<TotpSetup mode={mode} disclosure={disclosure} code={code} fieldError={serverField??form.errors.code} summaryKey={error??(form.hasErrors?'totp.error.highlighted':undefined)} busy={busy} onCode={value=>{setCode(value);setServerField(undefined);setError(undefined);}} onBlur={event=>form.onBlur('code',event)} onSubmit={event=>void form.handleSubmit(event,submitCode)} onCancel={()=>void cancel()} onCheck={()=>void check()}/>:<TotpManagement factors={methods.authenticators} locale={boot.locale} canEnroll={boot.canStartEnrollment} canDisable={boot.canStartDisable} canReset={boot.canStartReset} busy={blocked} onEnroll={()=>void start('ENROLL')} onReview={(kind,factor)=>void start(kind,factor)}/ >}
 {!setup&&!terminal&&!readyPage&&(operation||starting.current)&&<section className="totp-recovery" role="status"><p><ContentText contentKey="totp.setup.unknownDescription"/></p><div className="totp-actions"><PrimaryButton labelKey="totp.action.check" disabled={busy} onClick={()=>void check()}/></div></section>}{!setup&&error&&<FormErrorSummary contentKey={error}/>}</>;
 return <ContentProvider locale={boot.locale} audience="account-totp" resolve={key=>boot.content[key]}>{ended?<div className="authentication-page"><img className="authentication-glow indigo" src="/registration/assets/login-bg-indigo.svg" alt=""/><img className="authentication-glow violet" src="/registration/assets/login-bg-violet.svg" alt=""/><header className="authentication-header"><TunnerBrand contentKey="brand.tunner"/></header><main className="authentication-stage"><section className="authentication-card totp-terminal"><h1 tabIndex={-1} id="totp-page-heading"><ContentText contentKey="totp.terminal.endedHeading"/></h1>{content}</section></main></div>:<TotpAccountShell screenId={readyPage?totpRoutes.ready.id:setup?totpRoutes.setup.id:totpRoutes.manage.id} titleKey={setup?(mode==='consumed'?'totp.setup.consumedHeading':'totp.setup.heading'):'totp.heading'} descriptionKey={setup?(mode==='consumed'?'totp.setup.consumedDescription':'totp.setup.description'):'totp.description'} ready={readyPage} labels={{navigation:boot.content['passkeys.header.menu']!.text,notifications:boot.content['passkeys.header.notifications']!.text}}>{content}</TotpAccountShell>}
 {review&&operation&&!proof&&<ModalDialog labelledBy="totp-review-title" className="passkeys-dialog remove feedback totp-review-dialog" busy={busy} returnFocusTo={trigger.current} onRequestClose={()=>void cancel()}><div className="passkeys-dialog-content" data-tunner-component="COMP-TOTP-MUTATION-REVIEW"><h2 id="totp-review-title" tabIndex={-1}><ContentText contentKey={operation.kind==='RESET'?'totp.reset.heading':operation.kind==='DISABLE'?'totp.disable.heading':'totp.setup.heading'}/></h2><p className="passkeys-dialog-description"><ContentText contentKey={operation.kind==='RESET'?'totp.reset.separateEnrollment':operation.kind==='DISABLE'?'totp.disable.description':'totp.setup.description'}/></p>{operation.kind!=='ENROLL'&&operation.factorSessionEffect&&<p className="passkeys-impact"><ContentText contentKey={'totp.'+(operation.kind==='RESET'?'reset':'disable')+'.'+(operation.currentSessionEnds?'currentSessionEnds':operation.factorSessionEffect==='RevokeFactorSessions'?'affectedSessions':'preserve')}/></p>}<div className="passkeys-dialog-actions"><PrimaryButton labelKey="totp.action.cancel" appearance="outline" disabled={busy} onClick={()=>void cancel()}/><PrimaryButton labelKey="totp.action.continue" disabled={busy||operation.status!=='PENDING_PROOF'} onClick={()=>{const scope=work();running(true);void launchProof(operation,scope).catch(reason=>{if(current(scope)){setError(totpErrorKey(reason));setUnresolved(true);}}).finally(()=>{if(current(scope))running(false);});}}/></div></div></ModalDialog>}
 {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} onComplete={()=>void afterProof()} onCancel={outcome=>{setProof(undefined);if(outcome?.unresolved){setUnresolved(true);return;}void cancel();}} reconcile={proofRecovery}/>}</ContentProvider>;
}
createRoot(document.getElementById('root')!).render(<App/>);
