import {createRoot} from 'react-dom/client';
import {useEffect,useRef,useState} from 'react';
import {ContentUnavailable,ThemeProvider,navigatePath,useRouteLocation} from '@tunner/ui';
import '@tunner/ui/styles.css';
import './passkeys.css';
import './sessions.css';
import {checkCurrentSession,loadSessionContext,readSessionPage,SessionHttpError,signOutCurrentSession} from './sessions.api';
import type {SessionContext,SessionItem} from './sessions.api';
import {SessionsView} from './sessions.ui';
import type {SessionSignOutPhase,SessionViewPhase} from './sessions.ui';
import {isSessionRoute,sessionRoutes} from './sessions.routes';

interface Work{revision:number;signal:AbortSignal}
interface HeldSelection{reference:string;pageCursor:string|null}
function SessionsApp(){
 const route=useRouteLocation(),revision=useRef(route.revision);revision.current=route.revision;
 const controller=useRef(new AbortController()),flight=useRef(false),held=useRef<HeldSelection|undefined>(undefined);
 const cursors=useRef(new Map<string,string|null>()),next=useRef<string|null>(null),csrf=useRef('');
 const [context,setContext]=useState<SessionContext>(),[phase,setPhase]=useState<SessionViewPhase>('loading'),[items,setItems]=useState<SessionItem[]>([]),[selected,setSelected]=useState<SessionItem>();
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
  setSignOutPhase(value=>value==='busy'?'unknown':value);
  flight.current=true;setPhase('loading');setReadyRevision(-1);setMoreBusy(false);setUnavailable(false);setItems([]);setSelected(undefined);setHasMore(false);next.current=null;cursors.current.clear();
  if(!isSessionRoute(route.pathname)){window.location.replace('/login');return()=>work.signal.aborted?undefined:controller.current.abort();}
  if(window.location.search||window.location.hash){navigatePath(route.pathname,true);return()=>controller.current.abort();}
  const detail=route.pathname===sessionRoutes.details.pathname,selection=held.current;
  if(detail&&!selection){navigatePath(sessionRoutes.list.pathname,true);return()=>controller.current.abort();}
  let publishedReady=false;
  void (async()=>{
   try{
    const fresh=await loadSessionContext(work.signal);if(!current(work))return;csrf.current=fresh.csrfToken;setContext(fresh);publishedReady=true;
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
 function reload(){if(flight.current)return;held.current=route.pathname===sessionRoutes.details.pathname?held.current:undefined;setRefresh(value=>value+1);}
 function select(item:SessionItem){if(flight.current||phase!=='ready'||signOutPhase!=='idle')return;const pageCursor=cursors.current.get(item.reference);if(pageCursor===undefined)return;held.current={reference:item.reference,pageCursor};navigatePath(sessionRoutes.details.pathname);}
 async function more(){
  if(flight.current||!next.current||phase!=='ready'||signOutPhase!=='idle')return;
  const work={revision:route.revision,signal:controller.current.signal},boundary=next.current;flight.current=true;setMoreBusy(true);
  try{const page=await readSessionPage(csrf.current,boundary,work.signal);if(!current(work))return;
   for(const item of page.sessions)cursors.current.set(item.reference,boundary);
   setItems(previous=>{const merged=new Map(previous.map(item=>[item.reference,item]));for(const item of page.sessions)merged.set(item.reference,item);return [...merged.values()];});next.current=page.continuation;setHasMore(page.hasMore);
  }catch(error){reject(error,work);}finally{if(current(work)){flight.current=false;setMoreBusy(false);}}
 }
 async function signOut(){
  if(flight.current||signOutPhase!=='idle'||!csrf.current)return;
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
 return <SessionsView key={route.revision} options={context.options} phase={readyRevision===route.revision?phase:'loading'} detail={route.pathname===sessionRoutes.details.pathname} revision={route.revision} items={items} selected={selected} hasMore={hasMore} moreBusy={moreBusy} signOutPhase={signOutPhase} onRefresh={reload} onLoadMore={()=>void more()} onSelect={select} onSignOut={()=>void signOut()} onCheckSignIn={()=>void checkSignIn()}/>;
}
createRoot(document.getElementById('root')!).render(<ThemeProvider surface="user" mode="light"><SessionsApp/></ThemeProvider>);
