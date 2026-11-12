import {test} from 'node:test';
import assert from 'node:assert/strict';
import {renderToStaticMarkup} from 'react-dom/server';
import {RecoveryPresentation} from '../apps/user/recovery';
import type {RecoveryPresentationProps,RecoveryViewModel} from '../apps/user/recovery';
import type {PublishedContent} from '../packages/ui/src/index';
import proposal from '../apps/user/recovery.content.proposal.json' with {type:'json'};

// Explicit nonfunctional publications and offered references only; not a runtime goal/policy catalog.
const copy={...proposal.content,'fixture.goal.a':'Fixture offered goal A','fixture.goal.b':'Fixture offered goal B','fixture.unoffered':'Fixture unoffered goal','fixture.proof':'Fixture admitted method','fixture.effect.label':'Fixture effect category','fixture.effect.value':'Fixture authoritative value'};
const content:Record<string,PublishedContent>=Object.fromEntries(Object.entries(copy).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'fixture',versionRef:'fixture-v1',publicationRef:'nonfunctional-fixture-publication'}]));
const goal={reference:'offered-A',labelKey:'fixture.goal.a'},other={reference:'offered-B',labelKey:'fixture.goal.b'};
const detail={reference:'effect-A',labelKey:'fixture.effect.label',valueKey:'fixture.effect.value'};
const callbacks:RecoveryPresentationProps['callbacks']={onStart:()=>assert.fail('SSR invoked Start'),onChooseProof:()=>assert.fail('SSR invoked proof'),onSubmitCode:()=>assert.fail('SSR invoked code'),onCheck:()=>assert.fail('SSR invoked Check'),onCancel:()=>assert.fail('SSR invoked Cancel'),onFinish:()=>assert.fail('SSR invoked Finish'),onBackToSignIn:()=>assert.fail('SSR invoked navigation')};
function props(model:RecoveryViewModel):RecoveryPresentationProps{return {viewKey:'fixture-original-view',model,locale:'en',content,links:{privacyUri:'#fixture-privacy',termsUri:'#fixture-terms'},busy:false,callbacks};}
function html(model:RecoveryViewModel,override:Partial<RecoveryPresentationProps>={}){return renderToStaticMarkup(<RecoveryPresentation {...props(model)} {...override}/>);}
function button(markup:string,label:string){const match=[...markup.matchAll(/<button\b([^>]*)>([\s\S]*?)<\/button>/g)].find(x=>x[2]?.includes('>'+label+'<'));assert.ok(match,'Missing button '+label);return match[1]??'';}
function assertNoRestorationClaim(markup:string){assert.doesNotMatch(markup,/>Account recovered</i);assert.doesNotMatch(markup,/>(?:Your account (?:has been|is) recovered|You are signed in|(?:Sign-in )?Access has been restored)[.!]?</i);}
const result=(change:Partial<Extract<RecoveryViewModel,{kind:'result'}>>={}):Extract<RecoveryViewModel,{kind:'result'}>=>({kind:'result',goal,canCheck:true,canCancel:false,canBackToSignIn:true,detailsAvailable:true,effects:[detail],followUps:[],canFinish:true,...change});

test('SSR entry offers only supplied goals and never preselects a goal or invokes a callback',()=>{
 const markup=html({kind:'entry',goals:[goal,other],canStart:true,canBackToSignIn:true});
 assert.equal((markup.match(/role="radio"/g)??[]).length,2);assert.equal((markup.match(/aria-checked="false"/g)??[]).length,2);assert.doesNotMatch(markup,/aria-checked="true"|Fixture unoffered goal/);assert.match(markup,/novalidate=""/i);assertNoRestorationClaim(markup);
});
test('SSR external busy and absent admitted goals keep Start held',()=>{
 const model:RecoveryViewModel={kind:'entry',goals:[goal],canStart:true,canBackToSignIn:true};assert.match(button(html(model,{busy:true}),'Continue'),/disabled/);assert.match(button(html({...model,goals:[]}),'Continue'),/disabled/);
});
test('SSR missing base or actually rendered goal publication fails closed with no form/actions',()=>{
 for(const key of ['recovery.entry.heading','fixture.goal.a']){const missing={...content};delete missing[key];const markup=html({kind:'entry',goals:[goal],canStart:true,canBackToSignIn:true},{content:missing});assert.match(markup,/role="alert"/);assert.doesNotMatch(markup,/<form|<button/);}
});
test('SSR missing or partial RecoveryResult details cannot Finish despite a contradictory flag',()=>{
 for(const change of [{detailsAvailable:false},{effects:null},{followUps:null},{goal:null}]){const markup=html(result(change));assert.match(button(markup,'Finish'),/disabled/);assertNoRestorationClaim(markup);}
});
test('SSR complete supplied result still needs explicit Finish capability and reports only supplied effects',()=>{
 assert.match(button(html(result({canFinish:false})),'Finish'),/disabled/);const markup=html(result());assert.doesNotMatch(button(markup,'Finish'),/disabled/);assert.match(markup,/Fixture authoritative value/);assert.match(markup,/No follow-up steps were reported/);assertNoRestorationClaim(markup);
});
test('SSR pending-review and UNKNOWN expose only admitted original status/navigation controls',()=>{
 for(const kind of ['pending-review','unknown'] as const){const markup=html({kind,goal,canCheck:true,canCancel:false,canBackToSignIn:true});assert.equal((markup.match(/<button\b/g)??[]).length,2);assert.ok(button(markup,'Check recovery status'));assert.ok(button(markup,'Back to sign in'));assert.doesNotMatch(markup,/Recovery code<|Verify recovery code<|Cancel request<|Finish</);assertNoRestorationClaim(markup);}
});
test('SSR proof choices remain exact offered references and obey independent canSelect',()=>{
 const markup=html({kind:'proof-choice',goal,choices:[{reference:'proof-A',labelKey:'fixture.proof',canSelect:false}],canCheck:true,canCancel:false});assert.match(button(markup,'Fixture admitted method'),/disabled/);assert.doesNotMatch(markup,/Fixture unoffered goal|Verify recovery code</);assertNoRestorationClaim(markup);
});
