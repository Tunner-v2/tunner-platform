import {createRoot} from 'react-dom/client';
import {useEffect,useState} from 'react';
import {RecoveryPresentation} from '../../apps/user/recovery';
import type {RecoveryViewModel} from '../../apps/user/recovery';
import type {PublishedContent} from '../../packages/ui/src/index';
import proposal from '../../apps/user/recovery.content.proposal.json' with {type:'json'};

const copy={...proposal.content,'fixture.goal.a':'Fixture offered goal A','fixture.goal.b':'Fixture offered goal B','fixture.unoffered':'Fixture unoffered goal','fixture.proof':'Fixture admitted method','fixture.effect.label':'Fixture effect category','fixture.effect.value':'Fixture authoritative value'};
const baseContent:Record<string,PublishedContent>=Object.fromEntries(Object.entries(copy).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'fixture',versionRef:'fixture-v1',publicationRef:'nonfunctional-fixture-publication'}]));
const initial:RecoveryViewModel={kind:'entry',goals:[{reference:'offered-A',labelKey:'fixture.goal.a'},{reference:'offered-B',labelKey:'fixture.goal.b'}],canStart:true,canBackToSignIn:true};
type Action='start'|'choose'|'code'|'check'|'cancel'|'finish'|'back';
export interface RecoveryFixtureSnapshot {calls:Record<Action,number>;chosenGoal:string|null;codeArgumentPresent:boolean;codeDomEmptyAtCallback:boolean}
export interface RecoveryFixtureControls {configure:(model:RecoveryViewModel,options?:{holdCode?:boolean;missingKey?:string})=>void;replaceContext:()=>void;releaseCode:()=>void;snapshot:()=>RecoveryFixtureSnapshot}
declare global {interface Window {recoveryPresentationFixture:RecoveryFixtureControls}}

// Deliberately no HTTP, native recovery, provider, authentication or policy controller.
function App(){
 const [model,setModel]=useState<RecoveryViewModel>(initial),[revision,setRevision]=useState(0),[missingKey,setMissingKey]=useState<string>();
 const [state]=useState(()=>({calls:{start:0,choose:0,code:0,check:0,cancel:0,finish:0,back:0} as Record<Action,number>,chosenGoal:null as string|null,codeArgumentPresent:false,codeDomEmptyAtCallback:false,holdCode:false,release:undefined as (()=>void)|undefined}));
 useEffect(()=>{window.recoveryPresentationFixture={
  configure:(next,options={})=>{state.release?.();state.release=undefined;state.holdCode=options.holdCode===true;for(const action of Object.keys(state.calls) as Action[])state.calls[action]=0;state.chosenGoal=null;state.codeArgumentPresent=false;state.codeDomEmptyAtCallback=false;setMissingKey(options.missingKey);setModel(next);setRevision(value=>value+1);},
  replaceContext:()=>setRevision(value=>value+1),releaseCode:()=>{state.release?.();state.release=undefined;},snapshot:()=>({calls:{...state.calls},chosenGoal:state.chosenGoal,codeArgumentPresent:state.codeArgumentPresent,codeDomEmptyAtCallback:state.codeDomEmptyAtCallback})
 };},[]);
 const content={...baseContent};if(missingKey)delete content[missingKey];
 return <RecoveryPresentation viewKey={'synthetic-render-'+revision} model={model} content={content} locale="en" links={{privacyUri:'#fixture-privacy',termsUri:'#fixture-terms'}} busy={false} callbacks={{
  onStart:entry=>{state.calls.start++;state.chosenGoal=entry.goalReference;},onChooseProof:()=>{state.calls.choose++;},
  onSubmitCode:async code=>{state.calls.code++;state.codeArgumentPresent=code.length>0;state.codeDomEmptyAtCallback=document.querySelector<HTMLInputElement>('input[name=recoveryCode]')?.value==='';if(state.holdCode)await new Promise<void>(resolve=>{state.release=resolve;});},
  onCheck:()=>{state.calls.check++;},onCancel:()=>{state.calls.cancel++;},onFinish:()=>{state.calls.finish++;},onBackToSignIn:()=>{state.calls.back++;}
 }}/>
}
createRoot(document.getElementById('root')!).render(<App/>);
