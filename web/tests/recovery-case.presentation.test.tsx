import {test} from 'node:test';
import assert from 'node:assert/strict';
import {renderToStaticMarkup} from 'react-dom/server';
import {RecoveryPresentation,recoveryPasswordSchema} from '../apps/user/recovery';
import {validateTunnerForm} from '../packages/ui/src/index';
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
const repair=(change:Partial<Extract<RecoveryViewModel,{kind:'password-repair'}>>={}):Extract<RecoveryViewModel,{kind:'password-repair'}>=>({kind:'password-repair',goal,canCheck:true,canCancel:false,canBackToSignIn:true,canRepair:true,passwordMinimumLength:3,passwordMaximumLength:6,...change});
const repairCallbacks={...callbacks,onRepair:()=>assert.fail('SSR invoked password repair')};

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

test('password repair schema rejects empty and mismatched entries without borrowing registration requirements',()=>{
 const schema=recoveryPasswordSchema(3,6);
 const empty=validateTunnerForm(schema,{password:'',passwordConfirmation:''});assert.equal(empty.valid,false);assert.deepEqual(empty.errors,{password:'recovery.error.passwordRequired',passwordConfirmation:'recovery.error.passwordConfirmationRequired'});
 const mismatch=validateTunnerForm(schema,{password:'abc',passwordConfirmation:'abd'});assert.equal(mismatch.valid,false);assert.equal(mismatch.errors.passwordConfirmation,'recovery.error.passwordConfirmation');
 assert.equal(schema.safeParse({password:'abc',passwordConfirmation:'abc'}).success,true);
});

test('password repair schema obeys supplied inclusive rune length bounds and preserves exact values',()=>{
 const schema=recoveryPasswordSchema(3,4);
 for(const password of ['ab','abcde']){const checked=validateTunnerForm(schema,{password,passwordConfirmation:password});assert.equal(checked.valid,false);assert.equal(checked.errors.password,'recovery.error.passwordLength');}
 for(const password of ['abc','abcd','😀😀😀',' a '])assert.deepEqual(schema.parse({password,passwordConfirmation:password}),{password,passwordConfirmation:password});
 for(const bounds of [[0,4],[3,2],[1.5,4],[1,Infinity],[NaN,4],[1,Number.MAX_SAFE_INTEGER+1]])assert.throws(()=>recoveryPasswordSchema(bounds[0]!,bounds[1]!));
});

test('password repair SSR requires supplied callback, valid bounds and every required publication',()=>{
 for(const override of [{callbacks},{model:repair({passwordMinimumLength:0})},{model:repair({passwordMaximumLength:2})}]){const markup=html(repair(),{callbacks:repairCallbacks,...override});assert.match(markup,/role="alert"/);assert.doesNotMatch(markup,/<form|<button/);}
 for(const key of ['recovery.repair.requirements','recovery.repair.password','recovery.error.passwordConfirmation','recovery.repair.submit']){const missing={...content};delete missing[key];const markup=html(repair(),{callbacks:repairCallbacks,content:missing});assert.match(markup,/role="alert"/);assert.doesNotMatch(markup,/<form|<button/);}
 const invalid={...content,'recovery.repair.requirements':{...content['recovery.repair.requirements']!,text:'Missing request requirements'}};assert.doesNotMatch(html(repair(),{callbacks:repairCallbacks,content:invalid}),/<form|<button/);
});

test('password repair SSR uses only two new-password inputs and holds missing goal, capability or pending state',()=>{
 const markup=html(repair(),{callbacks:repairCallbacks});assert.equal((markup.match(/type="password"/g)??[]).length,2);assert.equal((markup.match(/autocomplete="new-password"/gi)??[]).length,2);assert.match(markup,/Use 3 to 6 characters/);assert.match(markup,/FORM-RECOVERY-PASSWORD-REPAIR/);assert.match(markup,/novalidate=""/i);assert.doesNotMatch(button(markup,'Save new password'),/disabled/);assertNoRestorationClaim(markup);
 for(const override of [{model:repair({canRepair:false})},{model:repair({goal:null})},{busy:true}])assert.match(button(html(repair(),{callbacks:repairCallbacks,...override}),'Save new password'),/disabled/);
});

test('password repair shared TextField interpolates and associates server length help before its error',()=>{
 const markup=html(repair(),{callbacks:repairCallbacks,fieldErrors:{password:'recovery.error.passwordLength'}}),input=markup.match(/<input\b[^>]*name="password"[^>]*>/)?.[0];assert.ok(input);const descriptions=input.match(/aria-describedby="([^"]+)"/)?.[1]?.split(' ');assert.equal(descriptions?.length,2);
 for(const id of descriptions!)assert.ok(markup.includes('id="'+id+'"'));assert.ok(markup.indexOf('Use 3 to 6 characters for this request.')<markup.indexOf('Use a password within the length requirements shown for this request.'));assert.doesNotMatch(markup,/helpValues|\{minimum\}|\{maximum\}/);
});

test('SSR optional contact purpose and Continue label reuse code layout without standalone verification claim',()=>{
 const markup=html({kind:'recovery-code',purpose:'contact',submitLabelKey:'recovery.action.continue',goal,canVerify:true,canCheck:true,canCancel:false});assert.match(markup,/Enter your verification code/);assert.match(markup,/name="recoveryCode"/);assert.ok(button(markup,'Continue'));assert.doesNotMatch(markup,/Verify recovery code|name="password"/);
});

const repairStatus=(state:'pending'|'unknown'|'updated',change:Partial<Extract<RecoveryViewModel,{kind:'password-repair-status'}>>={}):Extract<RecoveryViewModel,{kind:'password-repair-status'}>=>({kind:'password-repair-status',state,goal,canCheck:true,canCancel:true,canBackToSignIn:true,canViewResult:state==='updated',...change});
test('password repair pending and unknown use approved notices without secret inputs and obey status capabilities',()=>{
 const pending=html(repairStatus('pending'),{busy:true});assert.match(pending,/Updating your password/);assert.match(pending,/No completed password update is confirmed yet/);assert.match(button(pending,'Save new password'),/disabled/);assert.match(button(pending,'Check recovery status'),/disabled/);assert.doesNotMatch(pending,/<input|<form|Cancel request<|Back to sign in<|Please wait/);
 const unknown=html(repairStatus('unknown'));assert.match(unknown,/Check your password update/);assert.match(unknown,/before attempting another update/);assert.equal((unknown.match(/<button\b/g)??[]).length,2);assert.doesNotMatch(unknown,/<input|<form|Save new password<|Cancel request</);assert.doesNotMatch(html(repairStatus('unknown',{canCheck:false,canBackToSignIn:false})),/<button\b/);
});
test('password repair updated exposes only an explicit result handoff and cannot infer restored sign-in access',()=>{
 const handoff={...callbacks,onViewResult:()=>assert.fail('SSR invoked result handoff')},markup=html(repairStatus('updated'),{callbacks:handoff});assert.match(markup,/Password updated/);assert.match(markup,/The password update for this request is confirmed/);assert.match(markup,/does not confirm that sign-in access has been restored/);assert.equal((markup.match(/<button\b/g)??[]).length,1);assert.doesNotMatch(button(markup,'View recovery result'),/disabled/);assert.doesNotMatch(markup,/<input|<form|Check recovery status<|Cancel request<|Back to sign in</);assertNoRestorationClaim(markup);
 for(const change of [{canViewResult:false},{goal:null}])assert.match(button(html(repairStatus('updated',change),{callbacks:handoff}),'View recovery result'),/disabled/);
 assert.doesNotMatch(html(repairStatus('updated')),/<button|<input|<form/);
 for(const key of ['recovery.repair.pending.noticeDescription','recovery.repair.unknown.heading','recovery.repair.updated.boundary']){const state=key.includes('.pending.')?'pending':key.includes('.unknown.')?'unknown':'updated',missing={...content};delete missing[key];assert.doesNotMatch(html(repairStatus(state),{callbacks:handoff,content:missing}),/<button|<input|<form/);}
});
