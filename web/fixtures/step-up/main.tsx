import { createRoot } from 'react-dom/client';
import { StrictMode, useRef, useState } from 'react';
import { ThemeProvider } from '../../packages/ui/src/index';
import '../../packages/ui/styles/index.css';
import { loadStepUpOptions, StepUpDialog } from '../../apps/user/step-up';
import type { StepUpBootstrap } from '../../apps/user/step-up';
import './fixture.css';

function App(){
 const [boot,setBoot]=useState<StepUpBootstrap>();const [busy,setBusy]=useState(false);const [status,setStatus]=useState('Ready');const pending=useRef(false);const trigger=useRef<HTMLButtonElement>(null);
 async function open(){if(pending.current)return;pending.current=true;setBusy(true);setStatus('Loading');
   try{const value=await loadStepUpOptions();if(value.status==='COMPLETED')setStatus('Verification completed');else{setBoot(value);setStatus('Ready');}}
   catch{setStatus('Verification unavailable. Sign in, then try again.');}finally{pending.current=false;setBusy(false);}}
 return <ThemeProvider surface="user"><main className="step-up-fixture"><h1>Identity verification</h1><p>Isolated local test. This verifies your signed-in identity and records a test result. It changes no account settings.</p>
   <button ref={trigger} type="button" onClick={()=>void open()} disabled={busy}>Verify identity</button><p role="status">{status}</p><a href="/login">Sign in</a>
   {boot&&<StepUpDialog bootstrap={boot} returnFocusTo={trigger.current} onCancel={()=>{setBoot(undefined);setStatus('Cancelled');}} onComplete={()=>{setBoot(undefined);setStatus('Verification completed');}}/>}
 </main></ThemeProvider>;
}
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>);
