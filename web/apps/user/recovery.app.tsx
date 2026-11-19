import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentUnavailable,navigatePath,useRouteLocation} from '@tunner/ui';
import {RecoveryPresentation} from './recovery';
import type {RecoveryEntry,RecoveryPasswordEntry,RecoveryViewModel} from './recovery';
import {loadRecoveryOptions,parseRecoveryOperation,prepareRecoveryContact,recoveryCommand,repairRecoveryPassword,RecoveryHttpError} from './recovery.api';
import type {RecoveryBootstrap,RecoveryOperation,RecoveryOptions,RecoveryOriginal,RecoveryProof} from './recovery.api';
import {passkeysSupported} from './passkeys.native';
import {isRecoveryRoute,recoveryRoutes} from './recovery.routes';

interface Work {signal:AbortSignal;revision:number}
function original(operation:RecoveryOperation):RecoveryOriginal{return {requestId:operation.requestId,goalReference:operation.goalReference,version:operation.version};}
function samePublication(a:Pick<RecoveryOptions,'profileVersion'|'publicationVersion'|'locale'>,b:RecoveryOptions){return a.profileVersion===b.profileVersion&&a.publicationVersion===b.publicationVersion&&a.locale===b.locale;}
interface Selection {original:RecoveryOriginal;profileVersion:string;publicationVersion:string;locale:string;kind:RecoveryProof['kind'];stage:'code'|'password';challengeId?:string;code?:string}
type RepairStatus='pending'|'unknown'|'updated'|undefined;
function matchesSelection(selection:Selection,options:RecoveryOptions,operation:RecoveryOperation){return samePublication(selection,options)&&selection.original.requestId===operation.requestId&&selection.original.goalReference===operation.goalReference&&selection.original.version===operation.version&&operation.status==='AWAITING_PROOF'&&operation.canSubmitRepair;}
function available(options:RecoveryOptions,kind:RecoveryProof['kind']){return kind==='code'?options.code?.available===true:kind==='contact'?options.contact?.available===true:options.passkey?.available===true&&options.passkey.maximumCredentialJsonBytes!=null&&passkeysSupported();}
function view(options:RecoveryOptions,operation:RecoveryOperation|undefined,held:RecoveryOriginal|undefined,unknown:boolean,selection:Selection|undefined,repairStatus:RepairStatus):RecoveryViewModel {
 if(!operation&&!held)return {kind:'entry',goals:options.goals,canStart:true,canBackToSignIn:true};
 const goal=options.goals.find(goal=>goal.reference===(operation?.goalReference??held?.goalReference))??null;
 const common={goal,canCheck:true,canCancel:!unknown&&operation?.canCancel===true};
 if(repairStatus)return {...common,kind:'password-repair-status',state:repairStatus,canViewResult:repairStatus==='updated'&&operation?.status==='PASSWORD_REPAIRED_NO_SESSION'&&!!operation.result,canBackToSignIn:repairStatus==='unknown'};
 if(unknown)return {...common,kind:'unknown',canBackToSignIn:true};
 if(!operation)return {kind:'entry',goals:options.goals,canStart:false,canBackToSignIn:true};
 if(operation.status==='AWAITING_PROOF'){
  if(selection?.stage==='password')return {...common,kind:'password-repair',canRepair:operation.canSubmitRepair,passwordMinimumLength:options.repair.passwordMinimumLength,passwordMaximumLength:options.repair.passwordMaximumLength,canBackToSignIn:true};
  if(selection?.stage==='code')return {...common,kind:'recovery-code',canVerify:operation.canSubmitRepair,purpose:selection.kind==='contact'?'contact':'recovery',submitLabelKey:'recovery.action.continue'};
  return {...common,kind:'proof-choice',choices:(['code','passkey','contact'] as const).filter(kind=>available(options,kind)).map(kind=>({reference:kind,labelKey:'recovery.proof.'+kind+'.label',descriptionKey:'recovery.proof.'+kind+'.description',canSelect:operation.canSubmitRepair}))};
 }
 if(operation.status==='CANCELLED')return {...common,kind:'cancelled',canBackToSignIn:true};
 if(operation.status==='EXPIRED')return {...common,kind:'expired'};
 return {...common,kind:'result',detailsAvailable:!!operation.result,effects:operation.result?.effects.map(effect=>({reference:effect.kind,labelKey:effect.labelKey,valueKey:effect.valueKey}))??null,followUps:operation.result?.followUps??null,canFinish:false,canBackToSignIn:true};
}

/** Native original-browser state owns recovery. This controller never interprets an authenticated session as an outcome. */
function RecoveryApp(){
 const route=useRouteLocation(),revision=useRef(route.revision);revision.current=route.revision;
 const control=useRef(new AbortController()),flight=useRef(false),held=useRef<RecoveryOriginal|undefined>(undefined);
 const selected=useRef<Selection|undefined>(undefined);
 const feedback=useRef<string|undefined>(undefined);
 const [options,setOptions]=useState<RecoveryBootstrap>(),[operation,setOperation]=useState<RecoveryOperation>(),[unknown,setUnknown]=useState(false);
 const [ready,setReady]=useState(-1),[busy,setBusy]=useState(false),[unavailable,setUnavailable]=useState(false),[refresh,setRefresh]=useState(0),[error,setError]=useState<string>();
 const [repairStatus,setRepairStatus]=useState<RepairStatus>(undefined);
 const current=(work:Work)=>!work.signal.aborted&&work.signal===control.current.signal&&work.revision===revision.current;
 const scope=():Work=>({signal:control.current.signal,revision:route.revision});
 function running(value:boolean){flight.current=value;setBusy(value);}
 function accept(value:RecoveryOperation,replace=false,repairResponse=false){
  held.current=original(value);setOperation(value);setUnknown(false);setError(undefined);
  if(repairResponse&&value.status==='PASSWORD_REPAIRED_NO_SESSION'&&value.result){selected.current=undefined;setRepairStatus('updated');return;}
  setRepairStatus(undefined);
  const preserve=options&&selected.current&&matchesSelection(selected.current,options,value)&&selected.current.stage==='password';
  if(!preserve)selected.current=undefined;
  const target=value.status==='AWAITING_PROOF'?preserve?recoveryRoutes.repair.pathname:recoveryRoutes.verify.pathname:recoveryRoutes.result.pathname;
  navigatePath(target,replace);
 }
 useEffect(()=>{
  control.current.abort();control.current=new AbortController();const work={signal:control.current.signal,revision:route.revision};
  const message=feedback.current;feedback.current=undefined;
  running(false);setRepairStatus(undefined);setReady(-1);setUnavailable(false);setError(message);
  if(route.pathname!==recoveryRoutes.repair.pathname)selected.current=undefined;
  if(!isRecoveryRoute(route.pathname)){window.location.replace('/login');return()=>control.current.abort();}
  if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>control.current.abort();}
  void loadRecoveryOptions(work.signal).then(async fresh=>{
   if(!current(work))return;setOptions(fresh);
   const retained=held.current;let recovered=fresh.operation;
   if(retained){
    if(recovered)recovered=parseRecoveryOperation(recovered,retained);
    else recovered=await recoveryCommand('result',retained,work.signal);
   }
   if(!current(work))return;
   if(selected.current&&(!recovered||!matchesSelection(selected.current,fresh,recovered)||!available(fresh,selected.current.kind)))selected.current=undefined;
   if(recovered){accept(recovered,true);if(message)setError(message);}
   else{held.current=undefined;setOperation(undefined);setUnknown(false);navigatePath(recoveryRoutes.entry.pathname,true);}
   setReady(work.revision);
  }).catch(reason=>{
   if(!current(work))return;
   if(held.current&&reason instanceof RecoveryHttpError){setUnknown(true);setError('recovery.error.unavailable');setReady(work.revision);navigatePath(recoveryRoutes.result.pathname,true);}
   else{setUnavailable(true);setReady(work.revision);}
  });
  return()=>control.current.abort();
 },[route.revision,refresh]);
 useEffect(()=>{
  const leave=()=>{selected.current=undefined;control.current.abort();flight.current=false;};
  const returned=(event:PageTransitionEvent)=>{if(event.persisted)setRefresh(value=>value+1);};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);
  return()=>{selected.current=undefined;control.current.abort();window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);};
 },[]);
 async function start(entry:RecoveryEntry){
  if(flight.current||!options||held.current||!options.goals.some(goal=>goal.reference===entry.goalReference))return;
  const work=scope();running(true);setError(undefined);
  try{
   const fresh=await loadRecoveryOptions(work.signal);if(!current(work))return;
   if(!samePublication(options,fresh)||!fresh.goals.some(goal=>goal.reference===entry.goalReference))throw new RecoveryHttpError('changed');
   setOptions(fresh);if(fresh.operation){accept(fresh.operation);return;}
   const request={requestId:crypto.randomUUID(),goalReference:entry.goalReference,version:0};held.current=request;
   const value=await recoveryCommand('start',request,work.signal,{email:entry.email});if(current(work))accept(value);
  }catch{if(current(work)){setUnknown(!!held.current);setError('recovery.error.unavailable');if(held.current)navigatePath(recoveryRoutes.result.pathname);}}
  finally{if(current(work))running(false);}
 }
 async function command(action:'result'|'cancel'){
  const retained=held.current;if(flight.current||!retained)return;
  selected.current=undefined;
  const work=scope();running(true);setError(undefined);
  try{const value=await recoveryCommand(action,retained,work.signal);if(current(work))accept(value,false,action==='result'&&repairStatus==='unknown');}
  catch{if(current(work)){setUnknown(true);setError('recovery.error.unavailable');if(repairStatus!=='unknown')navigatePath(recoveryRoutes.result.pathname);}}
  finally{if(current(work))running(false);}
 }
 async function chooseProof(reference:string){
  if(reference!=='code'&&reference!=='contact'&&reference!=='passkey')return;
  const retained=held.current;if(flight.current||!retained||!operation||!options||operation.status!=='AWAITING_PROOF'||!operation.canSubmitRepair||!available(options,reference))return;
  const work=scope();running(true);setError(undefined);selected.current=undefined;
  try{
   const fresh=await loadRecoveryOptions(work.signal);if(!current(work))return;
   if(!samePublication(options,fresh)||!fresh.operation||fresh.operation.version!==retained.version||fresh.operation.requestId!==retained.requestId||fresh.operation.goalReference!==retained.goalReference||fresh.operation.status!=='AWAITING_PROOF'||!fresh.operation.canSubmitRepair||!available(fresh,reference))throw new RecoveryHttpError('changed');
   const selection:Selection={original:retained,profileVersion:fresh.profileVersion,publicationVersion:fresh.publicationVersion,locale:fresh.locale,kind:reference,stage:reference==='passkey'?'password':'code'};
   if(reference==='contact'){const contact=await prepareRecoveryContact(retained,work.signal);if(!current(work))return;selection.challengeId=contact.challengeId;}
   selected.current=selection;setOptions(fresh);setOperation(fresh.operation);
   if(selection.stage==='password')navigatePath(recoveryRoutes.repair.pathname);
  }catch{if(current(work)){selected.current=undefined;setError('recovery.error.unavailable');}}
  finally{if(current(work))running(false);}
 }
 function submitCode(code:string){
  const selection=selected.current;
  if(flight.current||!selection||selection.stage!=='code'||!code||!options||!operation||!matchesSelection(selection,options,operation))return;
  selection.code=code;selection.stage='password';setError(undefined);navigatePath(recoveryRoutes.repair.pathname);
 }
 async function repair(entry:RecoveryPasswordEntry){
  const selection=selected.current,retained=held.current;
  if(flight.current||!selection||selection.stage!=='password'||!retained||!options||!operation||!matchesSelection(selection,options,operation))return;
  const proof:RecoveryProof|undefined=selection.kind==='passkey'?{kind:'passkey'}:selection.kind==='code'&&selection.code?{kind:'code',code:selection.code}:selection.kind==='contact'&&selection.code&&selection.challengeId?{kind:'contact',code:selection.code,challengeId:selection.challengeId}:undefined;
  if(!proof)return;
  // Move proof into this one in-flight command; no retry, history or subsequent view retains it.
  selected.current=undefined;const work=scope();setRepairStatus('pending');running(true);setError(undefined);
  try{
   const fresh=await loadRecoveryOptions(work.signal);if(!current(work))return;
   if(!fresh.operation||!matchesSelection(selection,fresh,fresh.operation)||!available(fresh,proof.kind))throw new RecoveryHttpError('changed');
   setOptions(fresh);const value=await repairRecoveryPassword(retained,proof,entry,fresh,work.signal);if(current(work))accept(value,false,true);
  }catch(reason){if(current(work)){
   if(reason instanceof RecoveryHttpError&&reason.kind==='validation'){feedback.current='recovery.error.passwordRejected';navigatePath(recoveryRoutes.verify.pathname);}
   else{setUnknown(true);setRepairStatus('unknown');}
  }}finally{if(current(work))running(false);}
 }
 if(unavailable)return <ContentUnavailable/>;
 if(!options||ready!==route.revision)return <main aria-busy="true"/>;
 const model=view(options,operation,held.current,unknown,selected.current,repairStatus);
 const originalViewKey=operation||unknown?held.current?.requestId??'entry':'entry';
 return <RecoveryPresentation viewKey={[route.revision,options.profileVersion,options.publicationVersion,originalViewKey,operation?.version??0,repairStatus??(unknown?'unknown':model.kind)].join(':')} model={model} locale={options.locale} content={options.content} links={{privacyUri:options.privacyUri,termsUri:options.termsUri}} busy={busy} errorKey={error} callbacks={{onStart:start,onChooseProof:chooseProof,onSubmitCode:submitCode,onRepair:repair,onViewResult:()=>{if(operation?.status==='PASSWORD_REPAIRED_NO_SESSION'&&operation.result){setRepairStatus(undefined);navigatePath(recoveryRoutes.result.pathname);}},onCheck:()=>command('result'),onCancel:()=>command('cancel'),onFinish:()=>{throw new RecoveryHttpError('unavailable');},onBackToSignIn:()=>{selected.current=undefined;window.location.assign('/login');},onEdit:()=>setError(undefined)}}/>;
}
createRoot(document.getElementById('root')!).render(<RecoveryApp/>);
