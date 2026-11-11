// Public nonfunctional presentation only: no BFF, account, proof or file transport.
import {createRoot} from 'react-dom/client';
import {useRef,useState} from 'react';
import {ContentProvider,ThemeProvider} from '../../packages/ui/src/index';
import type {PublishedContent} from '../../packages/ui/src/index';
import '../../packages/ui/styles/index.css';
import '../../apps/user/passkeys.css';
import {RecoveryCodesCard,RecoveryCodesDialog} from '../../apps/user/recovery-codes.ui';
import type {RecoveryCodesView} from '../../apps/user/recovery-codes.ui';
import proposal from '../../apps/user/recovery-codes.content.proposal.json';

const marker='XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX'; // 39 chars; not a supported code.
const samples=(count:number):readonly [string,...string[]]=>[marker,...Array.from({length:count-1},()=>marker)];
const content:Record<string,PublishedContent>=Object.fromEntries(Object.entries({...proposal.content,
 'public.recovery.impact':'PUBLIC OWNER IMPACT — this nonfunctional fixture selects no replacement or session policy.',
 'public.recovery.longImpact':'PUBLIC OWNER IMPACT — '+('Public localized layout text with no production effect. '.repeat(12))
}).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'PUBLIC-RECOVERY-PRESENTATION',versionRef:'PUBLIC-NONFUNCTIONAL',publicationRef:'NOT-PRODUCTION-PUBLICATION'}]));

function Fixture(){
 const [view,setView]=useState<RecoveryCodesView>(),[busy,setBusy]=useState(false),[downloads,setDownloads]=useState(0),[continues,setContinues]=useState(0),[checks,setChecks]=useState(0),[closes,setCloses]=useState(0),[starts,setStarts]=useState(0);
 const trigger=useRef<HTMLElement|null>(null);
 function open(value:RecoveryCodesView,pending=false){trigger.current=document.activeElement instanceof HTMLElement?document.activeElement:null;setBusy(pending);setView(value);}
 function close(){setView(undefined);setBusy(false);setCloses(value=>value+1);}
 function download(){setDownloads(value=>value+1);setView(value=>value?.kind==='reveal'?{...value,download:'requested'}:value);}
 return <div className="passkeys-page"><main className="passkeys-main"><h1>Public component fixture</h1><p className="passkeys-subtitle">Nonfunctional samples only. This fixture makes no account requests and creates no files.</p>
  <RecoveryCodesCard available action="generate" busy={false} onStart={()=>setStarts(value=>value+1)}/>
  <div className="passkeys-page-actions">
   <button type="button" onClick={()=>open({kind:'reveal',codes:samples(8),download:'available'})}>Show eight samples</button>
   <button type="button" onClick={()=>open({kind:'reveal',codes:samples(3),download:'unavailable'})}>Show three samples</button>
   <button type="button" onClick={()=>open({kind:'review',impactKey:undefined,canContinue:true})}>Show missing impact</button>
   <button type="button" onClick={()=>open({kind:'review',impactKey:'public.recovery.impact',canContinue:false})}>Show stale review</button>
   <button type="button" onClick={()=>open({kind:'review',impactKey:'public.recovery.impact',canContinue:true})}>Show current review</button>
   <button type="button" onClick={()=>open({kind:'review',impactKey:'public.recovery.longImpact',canContinue:true})}>Show long review</button>
   <button type="button" onClick={()=>open({kind:'review',impactKey:'public.recovery.impact',canContinue:false},true)}>Show busy review</button>
   <button type="button" onClick={()=>open({kind:'unknown',canCheck:true})}>Show unknown result</button>
   <button type="button" onClick={()=>open({kind:'completed-without-payload'})}>Show lost body</button>
   <button type="button" onClick={()=>open({kind:'already-presented'})}>Show known presentation</button>
  </div>
  <dl aria-label="Public callback counters"><dt>Download callbacks</dt><dd data-fixture-count="downloads">{downloads}</dd><dt>Continue callbacks</dt><dd data-fixture-count="continues">{continues}</dd><dt>Check callbacks</dt><dd data-fixture-count="checks">{checks}</dd><dt>Close callbacks</dt><dd data-fixture-count="closes">{closes}</dd><dt>Start callbacks</dt><dd data-fixture-count="starts">{starts}</dd></dl>
  {view&&<RecoveryCodesDialog view={view} busy={busy} returnFocusTo={trigger.current} onClose={close} onContinue={()=>setContinues(value=>value+1)} onCheck={()=>setChecks(value=>value+1)} onDownload={download}/>}
 </main></div>;
}
createRoot(document.getElementById('root')!).render(<ThemeProvider surface="user" mode="light"><ContentProvider locale="en" audience="recovery-public-presentation" resolve={key=>content[key]}><Fixture/></ContentProvider></ThemeProvider>);
