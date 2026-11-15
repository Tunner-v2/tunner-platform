import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentUnavailable,ThemeProvider,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './passkeys.css';
import './sessions.css';
import './devices.css';
import {loadDeviceOptions} from './devices.api';
import type {DeviceOptions} from './devices.api';
import {checkCurrentSession,loadSessionContext,loadSessionRevocationOptions,readSessionRevocationContext,readSessionPage,SessionHttpError,signOutCurrentSession} from './sessions.api';
import type {SessionContext,SessionItem,SessionOptions,SessionRevocationContext} from './sessions.api';
import {SessionRevocation} from './sessions.revocation';
import {SessionsView} from './sessions.ui';
import type {SessionSignOutPhase,SessionViewPhase} from './sessions.ui';
import {isSessionRoute,sessionRoutes} from './sessions.routes';

interface Work{revision:number;signal:AbortSignal}
interface HeldSelection{reference:string;pageCursor:string|null}
function SessionsApp(){
 const route=useRouteLocation(),revision=useRef(route.revision);revision.current=route.revision;
 const controller=useRef(new AbortController()),flight=useRef(false),held=useRef<HeldSelection|undefined>(undefined);
 const cursors=useRef(new Map<string,string|null>()),next=useRef<string|null>(null),csrf=useRef('');
 const acknowledgedRevocations=useRef(new Set<string>());
 const [context,setContext]=useState<SessionContext>(),[phase,setPhase]=useState<SessionViewPhase>('loading'),[items,setItems]=useState<SessionItem[]>([]),[selected,setSelected]=useState<SessionItem>();
 const [revocationOptions,setRevocationOptions]=useState<SessionOptions>(),[original,setOriginal]=useState<SessionRevocationContext>(),[review,setReview]=useState<SessionItem>(),[revocationBlocked,setRevocationBlocked]=useState(false);
 const [deviceOptions,setDeviceOptions]=useState<DeviceOptions>();
 const [hasMore,setHasMore]=useState(false),[moreBusy,setMoreBusy]=useState(false),[signOutPhase,setSignOutPhase]=useState<SessionSignOutPhase>('idle'),[unavailable,setUnavailable]=useState(false),[refresh,setRefresh]=useState(0),[readyRevision,setReadyRevision]=useState(-1);
 const current=(work:Work)=>!work.signal.aborted&&work.signal===controller.current.signal&&work.revision===revision.current;
 function reject(error:unknown,work:Work){
  if(!current(work))return;
  next.current=null;cursors.current.clear();setItems([]);setSelected(undefined);setHasMore(false);
  if(error instanceof SessionHttpError&&error.kind==='authentication'){window.location.replace('/login');return;}
  if(error instanceof SessionHttpError&&error.kind==='changed'&&route.pathname===sessionRoutes.details.pathname){held.current=undefined;navigatePath(sessionRoutes.list.pathname,true);return;}
  setPhase(error instanceof SessionHttpError&&error.kind==='changed'?'changed':'error');setReadyRevision(work.revision);
 }
 useEffect(()=>{
  controller.current.abort();controller.current=new AbortController();const work={revision:route.revision,signal:controller.current.signal};
  setSignOutPhase(value=>value==='busy'?'unknown':value);setReview(undefined);setRevocationBlocked(false);setDeviceOptions(undefined);
  flight.current=true;setPhase('loading');setReadyRevision(-1);setMoreBusy(false);setUnavailable(false);setItems([]);setSelected(undefined);setHasMore(false);next.current=null;cursors.current.clear();
  if(!isSessionRoute(route.pathname)){window.location.replace('/login');return()=>work.signal.aborted?undefined:controller.current.abort();}
  if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>controller.current.abort();}
  const detail=route.pathname===sessionRoutes.details.pathname,selection=held.current;
  let publishedReady=false;
  void (async()=>{
   try{
    const fresh=await loadSessionContext(work.signal);if(!current(work))return;csrf.current=fresh.csrfToken;setContext(fresh);publishedReady=true;
    const optional=await loadSessionRevocationOptions(fresh.options.locale,work.signal);if(!current(work))return;
    const devices=await loadDeviceOptions(work.signal).catch(error=>{if(error instanceof SessionHttpError&&error.kind==='authentication')throw error;return undefined;});if(!current(work))return;setDeviceOptions(devices);
    const recovered=optional?await readSessionRevocationContext(work.signal):undefined;if(!current(work))return;
    setRevocationOptions(optional);setOriginal(recovered&&!acknowledgedRevocations.current.has(recovered.requestId)?recovered:undefined);
    // Recover the protected owner context before enforcing the ordinary in-memory detail guard.
    if(detail&&!selection){navigatePath(sessionRoutes.list.pathname,true);return;}
    const boundary=detail&&selection?selection.pageCursor:null;
    const page=await readSessionPage(fresh.csrfToken,boundary,work.signal);if(!current(work))return;
    for(const item of page.sessions)cursors.current.set(item.reference,boundary);
    if(detail&&selection){const exact=page.sessions.find(item=>item.reference===selection.reference);if(!exact){held.current=undefined;navigatePath(sessionRoutes.list.pathname,true);return;}setSelected(exact);}
    setItems(page.sessions);next.current=page.continuation;setHasMore(page.hasMore);setPhase('ready');setReadyRevision(work.revision);
   }catch(error){if(!current(work))return;if(!publishedReady)setUnavailable(true);reject(error,work);}
   finally{if(current(work))flight.current=false;}
  })();
  return()=>{work.signal.aborted||controller.current.abort();};
 },[route.revision,refresh]);
 useEffect(()=>{
  const leave=()=>{controller.current.abort();setSignOutPhase(value=>value==='busy'?'unknown':value);csrf.current='';held.current=undefined;next.current=null;cursors.current.clear();setItems([]);setSelected(undefined);setReadyRevision(-1);};
  const returnToPage=(event:PageTransitionEvent)=>{if(event.persisted)setRefresh(value=>value+1);};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returnToPage);
  return()=>{window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returnToPage);};
 },[]);
 function reload(){if(flight.current||revocationBlocked)return;held.current=route.pathname===sessionRoutes.details.pathname?held.current:undefined;setRefresh(value=>value+1);}
 function select(item:SessionItem){if(flight.current||revocationBlocked||phase!=='ready'||signOutPhase!=='idle')return;const pageCursor=cursors.current.get(item.reference);if(pageCursor===undefined)return;held.current={reference:item.reference,pageCursor};navigatePath(sessionRoutes.details.pathname);}
 async function more(){
  if(flight.current||revocationBlocked||!next.current||phase!=='ready'||signOutPhase!=='idle')return;
  const work={revision:route.revision,signal:controller.current.signal},boundary=next.current;flight.current=true;setMoreBusy(true);
  try{const page=await readSessionPage(csrf.current,boundary,work.signal);if(!current(work))return;
   for(const item of page.sessions)cursors.current.set(item.reference,boundary);
   setItems(previous=>{const merged=new Map(previous.map(item=>[item.reference,item]));for(const item of page.sessions)merged.set(item.reference,item);return [...merged.values()];});next.current=page.continuation;setHasMore(page.hasMore);
  }catch(error){reject(error,work);}finally{if(current(work)){flight.current=false;setMoreBusy(false);}}
 }
 async function signOut(){
  if(flight.current||revocationBlocked||signOutPhase!=='idle'||!csrf.current)return;
  const work={revision:route.revision,signal:controller.current.signal};flight.current=true;setSignOutPhase('busy');
  try{await signOutCurrentSession(csrf.current,work.signal);if(current(work))window.location.replace('/login');}
  catch(error){if(!current(work))return;if(error instanceof SessionHttpError&&error.kind==='authentication'){window.location.replace('/login');return;}setSignOutPhase('unknown');}
  finally{if(current(work))flight.current=false;}
 }
 async function checkSignIn(){
  if(flight.current||signOutPhase!=='unknown')return;const work={revision:route.revision,signal:controller.current.signal};flight.current=true;setMoreBusy(true);
  try{const token=await checkCurrentSession(work.signal);if(!current(work))return;csrf.current=token;setSignOutPhase('idle');setRefresh(value=>value+1);}
  catch(error){if(current(work)&&error instanceof SessionHttpError&&error.kind==='authentication')window.location.replace('/login');}
  finally{if(current(work)){flight.current=false;setMoreBusy(false);}}
 }
 if(unavailable)return <ContentUnavailable/>;
 if(!context)return <main className="sessions-initial" aria-busy="true"/>;
 const displayOptions=revocationOptions?{...context.options,content:{...context.options.content,...revocationOptions.content}}:context.options;
 return <SessionsView key={route.revision} options={displayOptions} deviceOptions={deviceOptions} phase={readyRevision===route.revision?phase:'loading'} detail={route.pathname===sessionRoutes.details.pathname} revision={route.revision} items={items} selected={selected} hasMore={hasMore} moreBusy={moreBusy} signOutPhase={signOutPhase} revocationAvailable={!!revocationOptions} revocationBlocked={revocationBlocked} onRevoke={item=>{if(!flight.current&&!revocationBlocked&&signOutPhase==='idle')setReview(item);}} onRefresh={reload} onLoadMore={()=>void more()} onSelect={select} onSignOut={()=>void signOut()} onCheckSignIn={()=>void checkSignIn()}>
  {revocationOptions&&readyRevision===route.revision&&phase==='ready'&&signOutPhase==='idle'&&<SessionRevocation options={displayOptions} csrfToken={csrf.current} revision={route.revision} initial={original} selection={review} onSelectionHandled={()=>setReview(undefined)} onBlockedChange={setRevocationBlocked} onAcknowledged={requestId=>acknowledgedRevocations.current.add(requestId)} onRefresh={()=>{held.current=undefined;setOriginal(undefined);setRevocationBlocked(false);if(route.pathname===sessionRoutes.details.pathname)navigatePath(sessionRoutes.list.pathname,true);else setRefresh(value=>value+1);}}/>}
 </SessionsView>;
}
createRoot(document.getElementById('root')!).render(<ThemeProvider surface="user" mode="light"><SessionsApp/></ThemeProvider>);
