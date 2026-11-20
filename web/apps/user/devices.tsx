import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentText,ContentUnavailable,PrimaryButton,ThemeProvider,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './passkeys.css';
import './sessions.css';
import './devices.css';
import {checkCurrentSession,SessionHttpError,signOutCurrentSession} from './sessions.api';
import {loadDeviceOptions,parseDevicePage,parseDeviceTrustRemovalContext,prepareDeviceTrustRemoval,readDeviceDetails,readDevicePage,readDeviceTrustRemovalContext,readDeviceTrustRemovalResult} from './devices.api';
import type {DeviceItem,DeviceOptions,DeviceTrustRemovalContext,DeviceTrustRemovalRequest,DeviceTrustRemovalResult} from './devices.api';
import {DeviceRemovalDialog,DevicesView,canReviewDeviceTrust} from './devices.ui';
import type {DeviceRemovalPhase,DeviceRemovalTarget,DeviceSignOutPhase,DeviceViewPhase} from './devices.ui';
import {deviceDetailsPath,deviceRoute,deviceRoutes} from './devices.routes';
import {loadStepUpOptions,StepUpDialog} from './step-up';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';

interface Work{revision:number;signal:AbortSignal}
/** Device intent is retained only in this owner and the protected server original; never in URLs/storage. */
export function DevicesApp(){
 const route=useRouteLocation(),location=deviceRoute(route.pathname),revision=useRef(route.revision);revision.current=route.revision;
 const controller=useRef(new AbortController()),flight=useRef(false),csrf=useRef(''),next=useRef<string|undefined>(undefined),trigger=useRef<HTMLElement|null>(null);
 const original=useRef<DeviceTrustRemovalContext|undefined>(undefined),candidate=useRef<DeviceTrustRemovalRequest|undefined>(undefined),receipt=useRef<DeviceTrustRemovalResult|undefined>(undefined),removal=useRef<DeviceRemovalPhase>('idle');
 const acknowledged=useRef(new Set<string>());
 const [options,setOptions]=useState<DeviceOptions>(),[phase,setPhase]=useState<DeviceViewPhase>('loading'),[items,setItems]=useState<DeviceItem[]>([]),[selected,setSelected]=useState<DeviceItem>(),[hasMore,setHasMore]=useState(false),[busy,setBusy]=useState(false);
 const [removalPhase,setRemovalPhase]=useState<DeviceRemovalPhase>('idle'),[target,setTarget]=useState<DeviceRemovalTarget>(),[open,setOpen]=useState(false),[proof,setProof]=useState<StepUpBootstrap>(),[signOutPhase,setSignOutPhase]=useState<DeviceSignOutPhase>('idle'),[unavailable,setUnavailable]=useState(false),[refresh,setRefresh]=useState(0),[readyRevision,setReadyRevision]=useState(-1);
 const current=(work:Work)=>!work.signal.aborted&&work.signal===controller.current.signal&&work.revision===revision.current;
 const work=():Work=>({revision:revision.current,signal:controller.current.signal});
 function setRemoval(value:DeviceRemovalPhase){removal.current=value;setRemovalPhase(value);}
 function running(value:boolean){flight.current=value;setBusy(value);}
 function authenticate(error:unknown){if(error instanceof SessionHttpError&&error.kind==='authentication'){window.location.replace('/login');return true;}return false;}
 function own(value:DeviceTrustRemovalContext){original.current=value;candidate.current=undefined;setTarget(previous=>previous?.reference===value.target.reference?previous:{reference:value.target.reference});}
 function reject(error:unknown,scope:Work){if(!current(scope)||authenticate(error))return;next.current=undefined;setItems([]);setSelected(undefined);setHasMore(false);setPhase(error instanceof SessionHttpError&&error.kind==='changed'?'changed':'error');setReadyRevision(scope.revision);}
 async function result(value:DeviceTrustRemovalContext,scope:Work){
  const found=await readDeviceTrustRemovalResult(csrf.current,value,scope.signal);if(!current(scope))return;
  receipt.current=found;setTarget({reference:found.target.reference});setProof(undefined);setRemoval('success');setOpen(true);
 }
 useEffect(()=>{
  controller.current.abort();const active=new AbortController();controller.current=active;const scope={revision:route.revision,signal:active.signal};
  if(removal.current==='review'){candidate.current=undefined;setRemoval('idle');}else if(['preparing','proof','checking'].includes(removal.current))setRemoval('unknown');
  setOpen(false);setProof(undefined);setTarget(undefined);setSignOutPhase(value=>value==='busy'?'unknown':value);running(true);setPhase('loading');setReadyRevision(-1);setUnavailable(false);setItems([]);setSelected(undefined);setHasMore(false);next.current=undefined;
  if(!location){navigatePath(deviceRoutes.list.pathname,true);return()=>active.abort();}
  if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>active.abort();}
  let publicationReady=false;
  void (async()=>{
   try{
    const token=await checkCurrentSession(scope.signal);if(!current(scope))return;csrf.current=token;
    const published=await loadDeviceOptions(scope.signal);if(!current(scope))return;if(!published)throw new SessionHttpError('unavailable');setOptions(published);publicationReady=true;
    const recovered=await readDeviceTrustRemovalContext(scope.signal);if(!current(scope))return;
    if(recovered&&!acknowledged.current.has(recovered.requestId)){
     const expected=original.current??candidate.current,value=parseDeviceTrustRemovalContext(recovered,expected);own(value);
     // Reload/navigation recover ownership only. An explicit check reads its exact causal outcome.
     setRemoval(value.status==='CHANGED'?'changed':'unknown');
    }else if(original.current||candidate.current)setRemoval('unknown');
    if(!current(scope))return;
    if(location.detail&&location.reference){const found=await readDeviceDetails(token,location.reference,scope.signal);if(!current(scope))return;setSelected(found);}
    else{const found=await readDevicePage(token,undefined,scope.signal);if(!current(scope))return;setItems(found.devices);setHasMore(found.hasMore);next.current=found.continuation??undefined;}
    if(!current(scope))return;setPhase('ready');setReadyRevision(scope.revision);
   }catch(error){if(!current(scope))return;if(!publicationReady)setUnavailable(true);reject(error,scope);}
   finally{if(current(scope))running(false);}
  })();return()=>active.abort();
 },[route.revision,refresh]);
 useEffect(()=>{
  const leave=()=>{controller.current.abort();csrf.current='';next.current=undefined;setItems([]);setSelected(undefined);setTarget(undefined);setProof(undefined);setOpen(false);setReadyRevision(-1);setSignOutPhase(value=>value==='busy'?'unknown':value);if(['preparing','proof','checking'].includes(removal.current))setRemoval('unknown');};
  const returned=(event:PageTransitionEvent)=>{if(event.persisted)setRefresh(value=>value+1);};window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);return()=>{window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);};
 },[]);
 function reload(){if(flight.current||original.current||candidate.current||signOutPhase!=='idle')return;setRefresh(value=>value+1);}
 function select(item:DeviceItem){if(flight.current||original.current||candidate.current||phase!=='ready'||signOutPhase!=='idle')return;navigatePath(deviceDetailsPath(item.reference));}
 async function more(){
  if(flight.current||!next.current||original.current||candidate.current||phase!=='ready'||signOutPhase!=='idle')return;const scope=work(),boundary=next.current;running(true);
  try{const found=await readDevicePage(csrf.current,boundary,scope.signal);if(!current(scope))return;const merged=new Map(items.map(item=>[item.reference,item]));for(const item of found.devices)merged.set(item.reference,item);const checked=parseDevicePage({...found,devices:[...merged.values()]});setItems(checked.devices);setHasMore(found.hasMore);next.current=found.continuation??undefined;}catch(error){reject(error,scope);}finally{if(current(scope))running(false);}
 }
 function review(item:DeviceItem){
  if(flight.current||original.current||candidate.current||!options||phase!=='ready'||signOutPhase!=='idle'||!canReviewDeviceTrust(item))return;
  trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;candidate.current={requestId:options.requestId,reference:item.reference,expectedVersion:item.version,expectedTrustVersion:item.trustVersion,confirmed:true};receipt.current=undefined;
  setTarget({reference:item.reference,lastObservedAt:item.lastObservedAt,...(item.characteristics.BrowserFamily?{browserFamily:item.characteristics.BrowserFamily}:{})});setRemoval('review');setOpen(true);
 }
 async function confirm(){
  const request=candidate.current;if(flight.current||removal.current!=='review'||!request)return;const scope=work();running(true);setRemoval('preparing');
  try{
   const value=await prepareDeviceTrustRemoval(csrf.current,request,scope.signal);if(!current(scope))return;own(value);
   if(value.status==='COMPLETED'){setRemoval('checking');await result(value,scope);return;}if(value.status==='CHANGED'){setRemoval('changed');return;}
   const bootstrap=await loadStepUpOptions(scope.signal);if(!current(scope))return;if(bootstrap.operationReference!==value.operationReference)throw new SessionHttpError('unavailable');
   if(bootstrap.status==='COMPLETED'){setRemoval('checking');await result(value,scope);return;}
   setRemoval('proof');setOpen(false);setProof(bootstrap);
  }catch(error){if(current(scope)&&!authenticate(error)){setProof(undefined);setOpen(true);setRemoval('unknown');}}finally{if(current(scope))running(false);}
 }
 async function check(){
  if(flight.current||(!original.current&&!candidate.current))return;const scope=work();running(true);setOpen(true);setRemoval('checking');
  try{
   let value=original.current;
   if(!value){const recovered=await readDeviceTrustRemovalContext(scope.signal);if(!current(scope))return;if(!recovered||!candidate.current)throw new SessionHttpError('unavailable');value=parseDeviceTrustRemovalContext(recovered,candidate.current);own(value);}
   await result(value,scope);
  }catch(error){if(current(scope)&&!authenticate(error))setRemoval('unknown');}finally{if(current(scope))running(false);}
 }
 async function afterProof(outcome:StepUpCompletion){
  const value=original.current;setProof(undefined);setOpen(true);
  if(!value||outcome.operationReference!==value.operationReference||outcome.signInRequired){setRemoval('unknown');return;}
  const scope=work();running(true);setRemoval('checking');try{await result(value,scope);}catch(error){if(current(scope)&&!authenticate(error))setRemoval('unknown');}finally{if(current(scope))running(false);}
 }
 async function reconcile(signal:AbortSignal):Promise<StepUpCompletion|null>{
  const value=original.current,scope=work();if(!value||signal.aborted)return null;
  try{const found=await readDeviceTrustRemovalResult(csrf.current,value,signal);if(signal.aborted||!current(scope))return null;receipt.current=found;return {status:'COMPLETED',signInRequired:false,operationReference:value.operationReference};}catch(error){if(!signal.aborted&&current(scope))authenticate(error);return null;}
 }
 function close(){
  if(flight.current)return;setOpen(false);
  if(removal.current==='review'){candidate.current=undefined;setTarget(undefined);setRemoval('idle');}
  else if(removal.current==='success'&&receipt.current){acknowledged.current.add(receipt.current.requestId);candidate.current=undefined;original.current=undefined;receipt.current=undefined;setTarget(undefined);setRemoval('idle');if(route.pathname===deviceRoutes.list.pathname)setRefresh(value=>value+1);else navigatePath(deviceRoutes.list.pathname);}
  else if(route.pathname!==deviceRoutes.list.pathname)navigatePath(deviceRoutes.list.pathname);
 }
 async function signOut(){
  if(flight.current||original.current||candidate.current||signOutPhase!=='idle')return;const scope=work();running(true);setSignOutPhase('busy');
  try{await signOutCurrentSession(csrf.current,scope.signal);if(current(scope))window.location.replace('/login');}catch(error){if(current(scope)&&!authenticate(error))setSignOutPhase('unknown');}finally{if(current(scope))running(false);}
 }
 async function checkSignIn(){
  if(flight.current||signOutPhase!=='unknown')return;const scope=work();running(true);
  try{const token=await checkCurrentSession(scope.signal);if(!current(scope))return;csrf.current=token;setSignOutPhase('idle');setRefresh(value=>value+1);}catch(error){if(current(scope))authenticate(error);}finally{if(current(scope))running(false);}
 }
 if(unavailable)return <ContentUnavailable/>;if(!options)return <main className="sessions-initial" aria-busy="true"/>;
 const blocked=busy||!!original.current||!!candidate.current||signOutPhase!=='idle';
 return <DevicesView options={options} phase={readyRevision===route.revision?phase:'loading'} detail={location?.detail??false} revision={route.revision} items={items} selected={selected} hasMore={hasMore} busy={busy} blocked={blocked} signOutPhase={signOutPhase} onSelect={select} onReview={review} onRefresh={reload} onLoadMore={()=>void more()} onSignOut={()=>void signOut()} onCheckSignIn={()=>void checkSignIn()}>
  {!open&&!proof&&(original.current||candidate.current)&&<div className="sessions-notice devices-recovery" role="status"><p><ContentText contentKey={'account.devices.'+(removalPhase==='changed'?'changedOriginal.description':'unknown.description')}/></p><PrimaryButton appearance="outline" labelKey="account.devices.check" disabled={busy} onClick={()=>void check()}/></div>}
  {open&&!proof&&<DeviceRemovalDialog options={options} phase={removalPhase} target={target} busy={busy} returnFocusTo={trigger.current} onConfirm={()=>void confirm()} onCheck={()=>void check()} onClose={close}/>}
  {proof&&<StepUpDialog bootstrap={proof} returnFocusTo={trigger.current} reconcile={reconcile} onComplete={outcome=>void afterProof(outcome)} onCancel={()=>{setProof(undefined);setOpen(true);setRemoval('unknown');}}/>}
 </DevicesView>;
}
createRoot(document.getElementById('root')!).render(<ThemeProvider surface="user" mode="light"><DevicesApp/></ThemeProvider>);
