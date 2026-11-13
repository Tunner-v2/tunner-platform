import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentUnavailable,navigatePath,useRouteLocation} from '@tunner/ui';
import {RecoveryPresentation} from './recovery';
import type {RecoveryEntry,RecoveryViewModel} from './recovery';
import {loadRecoveryOptions,parseRecoveryOperation,recoveryCommand,RecoveryHttpError} from './recovery.api';
import type {RecoveryBootstrap,RecoveryOperation,RecoveryOptions,RecoveryOriginal} from './recovery.api';
import {isRecoveryRoute,recoveryRoutes} from './recovery.routes';

interface Work {signal:AbortSignal;revision:number}
function original(operation:RecoveryOperation):RecoveryOriginal{return {requestId:operation.requestId,goalReference:operation.goalReference,version:operation.version};}
function samePublication(a:RecoveryOptions,b:RecoveryOptions){return a.profileVersion===b.profileVersion&&a.publicationVersion===b.publicationVersion&&a.locale===b.locale;}
function view(options:RecoveryOptions,operation:RecoveryOperation|undefined,held:RecoveryOriginal|undefined,unknown:boolean):RecoveryViewModel {
 if(!operation&&!held)return {kind:'entry',goals:options.goals,canStart:true,canBackToSignIn:true};
 const goal=options.goals.find(goal=>goal.reference===(operation?.goalReference??held?.goalReference))??null;
 const common={goal,canCheck:true,canCancel:!unknown&&operation?.canCancel===true};
 if(unknown)return {...common,kind:'unknown',canBackToSignIn:true};
 if(!operation)return {kind:'entry',goals:options.goals,canStart:false,canBackToSignIn:true};
 if(operation.status==='AWAITING_PROOF')return {...common,kind:'proof-choice',choices:[]};
 if(operation.status==='CANCELLED')return {...common,kind:'cancelled',canBackToSignIn:true};
 if(operation.status==='EXPIRED')return {...common,kind:'expired'};
 return {...common,kind:'result',detailsAvailable:!!operation.result,effects:operation.result?.effects.map(effect=>({reference:effect.kind,labelKey:effect.labelKey,valueKey:effect.valueKey}))??null,followUps:operation.result?.followUps??null,canFinish:false,canBackToSignIn:true};
}

/** Native original-browser state owns recovery. This controller never interprets an authenticated session as an outcome. */
function RecoveryApp(){
 const route=useRouteLocation(),revision=useRef(route.revision);revision.current=route.revision;
 const control=useRef(new AbortController()),flight=useRef(false),held=useRef<RecoveryOriginal|undefined>(undefined);
 const [options,setOptions]=useState<RecoveryBootstrap>(),[operation,setOperation]=useState<RecoveryOperation>(),[unknown,setUnknown]=useState(false);
 const [ready,setReady]=useState(-1),[busy,setBusy]=useState(false),[unavailable,setUnavailable]=useState(false),[refresh,setRefresh]=useState(0),[error,setError]=useState<string>();
 const current=(work:Work)=>!work.signal.aborted&&work.signal===control.current.signal&&work.revision===revision.current;
 const scope=():Work=>({signal:control.current.signal,revision:route.revision});
 function running(value:boolean){flight.current=value;setBusy(value);}
 function accept(value:RecoveryOperation,replace=false){
  held.current=original(value);setOperation(value);setUnknown(false);setError(undefined);
  const target=value.status==='AWAITING_PROOF'?recoveryRoutes.verify.pathname:recoveryRoutes.result.pathname;
  navigatePath(target,replace);
 }
 useEffect(()=>{
  control.current.abort();control.current=new AbortController();const work={signal:control.current.signal,revision:route.revision};
  running(false);setReady(-1);setUnavailable(false);setError(undefined);
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
   if(recovered)accept(recovered,true);
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
  const leave=()=>{control.current.abort();flight.current=false;};
  const returned=(event:PageTransitionEvent)=>{if(event.persisted)setRefresh(value=>value+1);};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);
  return()=>{control.current.abort();window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);};
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
  const work=scope();running(true);setError(undefined);
  try{const value=await recoveryCommand(action,retained,work.signal);if(current(work))accept(value);}
  catch{if(current(work)){setUnknown(true);setError('recovery.error.unavailable');navigatePath(recoveryRoutes.result.pathname);}}
  finally{if(current(work))running(false);}
 }
 if(unavailable)return <ContentUnavailable/>;
 if(!options||ready!==route.revision)return <main aria-busy="true"/>;
 const model=view(options,operation,held.current,unknown);
 const originalViewKey=operation||unknown?held.current?.requestId??'entry':'entry';
 return <RecoveryPresentation viewKey={[route.revision,options.profileVersion,options.publicationVersion,originalViewKey,operation?.version??0,unknown?'unknown':model.kind].join(':')} model={model} locale={options.locale} content={options.content} links={{privacyUri:options.privacyUri,termsUri:options.termsUri}} busy={busy} errorKey={error} callbacks={{onStart:start,onChooseProof:()=>{throw new RecoveryHttpError('unavailable');},onSubmitCode:()=>{throw new RecoveryHttpError('unavailable');},onCheck:()=>command('result'),onCancel:()=>command('cancel'),onFinish:()=>{throw new RecoveryHttpError('unavailable');},onBackToSignIn:()=>window.location.assign('/login'),onEdit:()=>setError(undefined)}}/>;
}
createRoot(document.getElementById('root')!).render(<RecoveryApp/>);
