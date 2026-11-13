import {test} from 'node:test';
import assert from 'node:assert/strict';
import {parseRecoveryOperation,parseRecoveryOptions,recoveryCommand,RecoveryHttpError} from '../apps/user/recovery.api';

const requestId='12121212-3434-4567-8989-121212121212',otherId='23232323-4545-4678-8989-232323232323';
const original={requestId,goalReference:'fixture-goal',version:1};
const operation=()=>({...original,status:'AWAITING_PROOF',canCancel:true,canSubmitRepair:true,codeSubmission:'ATOMIC_REPAIR_ONLY',authenticated:false});
const result={outcome:'PASSWORD_REPAIRED_NO_SESSION',effects:[{kind:'TUNNER_SESSION_INVALIDATION',labelKey:'fixture.effect.label',valueKey:'fixture.effect.value'}],followUps:[]};
const options=()=>({available:true,profileVersion:'fixture-profile',publicationVersion:'fixture-publication',locale:'en',content:{},goals:[{reference:'fixture-goal',labelKey:'fixture.goal',descriptionKey:'fixture.description'}],repair:{available:true,passwordMinimumLength:12,passwordMaximumLength:128,sessionEffect:'InvalidateExistingTunnerSessions',effectLabelKey:'fixture.effect.label',effectValueKey:'fixture.effect.value'},operation:operation()});
const context={csrfToken:'fixture-current-csrf',recoveryAvailable:true,termsUri:'#fixture-terms',privacyUri:'#fixture-privacy'};

test('Neutral AWAITING_PROOF capabilities are not a verified or authenticated outcome and private selectors are discarded',()=>{
 const parsed=parseRecoveryOperation({...operation(),accountId:otherId,sessionId:otherId,caseId:otherId});assert.equal(parsed.status,'AWAITING_PROOF');assert.equal(parsed.authenticated,false);assert.equal(parsed.codeSubmission,'ATOMIC_REPAIR_ONLY');assert.equal(parsed.result,undefined);assert.equal('accountId' in parsed,false);assert.equal('sessionId' in parsed,false);assert.equal('caseId' in parsed,false);assert.throws(()=>parseRecoveryOperation({...operation(),authenticated:true}),RecoveryHttpError);
});
test('A retained original rejects request/goal substitution and version rollback',()=>{
 for(const change of [{requestId:otherId},{goalReference:'other-goal'},{version:1}])assert.throws(()=>parseRecoveryOperation({...operation(),...change},{...original,version:2}),RecoveryHttpError);
 assert.equal(parseRecoveryOperation({...operation(),version:2},original).version,2);
});
test('Only actual password completion carries the explicit native result and never a session-success shape',()=>{
 assert.throws(()=>parseRecoveryOperation({...operation(),status:'PASSWORD_REPAIRED_NO_SESSION'}),RecoveryHttpError);assert.throws(()=>parseRecoveryOperation({...operation(),result}),RecoveryHttpError);
 const completed=parseRecoveryOperation({...operation(),version:2,status:'PASSWORD_REPAIRED_NO_SESSION',canCancel:false,canSubmitRepair:false,result});assert.equal(completed.authenticated,false);assert.equal(completed.result?.outcome,'PASSWORD_REPAIRED_NO_SESSION');assert.deepEqual(completed.result?.followUps,[]);
 for(const status of ['REJECTED','UNKNOWN','COMPLETED','PROOF_VERIFIED'])assert.throws(()=>parseRecoveryOperation({...operation(),status}),RecoveryHttpError);
});
test('Missing current capability, ambiguous goals or invalid explicit password bounds fail closed',()=>{
 assert.throws(()=>parseRecoveryOptions({...options(),available:false}),RecoveryHttpError);assert.throws(()=>parseRecoveryOptions({...options(),goals:[...options().goals,...options().goals]}),RecoveryHttpError);assert.throws(()=>parseRecoveryOptions({...options(),repair:{...options().repair,passwordMinimumLength:129}}),RecoveryHttpError);
 assert.equal(parseRecoveryOptions({...options(),operation:{...operation(),canSubmitRepair:false,canCancel:false}}).operation?.canSubmitRepair,false);
});
test('Start and exact result use fresh anonymous CSRF and only their closed approved request bodies',async()=>{
 const previous=globalThis.fetch,requests:{path:string;body:unknown;csrf:string|null}[]=[];let reads=0;
 globalThis.fetch=async(input,init)=>{const path=String(input);if(path==='/authentication/options'){reads++;return Response.json({...context,csrfToken:'fixture-current-'+reads});}requests.push({path,body:JSON.parse(String(init?.body)),csrf:new Headers(init?.headers).get('X-Tunner-CSRF')});return Response.json({operation:operation()});};
 try{await recoveryCommand('start',{...original,version:0},new AbortController().signal,{email:'person@example.test'});await recoveryCommand('result',original,new AbortController().signal);assert.deepEqual(requests,[{path:'/recovery/start',body:{requestId,email:'person@example.test',goalReference:original.goalReference},csrf:'fixture-current-1'},{path:'/recovery/result',body:{requestId},csrf:'fixture-current-2'}]);}finally{globalThis.fetch=previous;}
});
test('Lost Start remains UNKNOWN and is never retried or followed by an invented proof call',async()=>{
 const previous=globalThis.fetch,paths:string[]=[];globalThis.fetch=async input=>{const path=String(input);paths.push(path);if(path==='/authentication/options')return Response.json(context);throw new Error('synthetic network loss');};
 try{await assert.rejects(recoveryCommand('start',{...original,version:0},new AbortController().signal,{email:'person@example.test'}),(error:unknown)=>error instanceof RecoveryHttpError&&error.kind==='unknown');assert.deepEqual(paths,['/authentication/options','/recovery/start']);}finally{globalThis.fetch=previous;}
});
test('Unadvertised recovery stops before mutation; a different returned original is a safe conflict',async()=>{
 const previous=globalThis.fetch;let posts=0;globalThis.fetch=async input=>String(input)==='/authentication/options'?Response.json({...context,recoveryAvailable:undefined}):(posts++,Response.json({operation:operation()}));
 try{await assert.rejects(recoveryCommand('cancel',original,new AbortController().signal),RecoveryHttpError);assert.equal(posts,0);globalThis.fetch=async input=>String(input)==='/authentication/options'?Response.json(context):Response.json({operation:{...operation(),requestId:otherId}});await assert.rejects(recoveryCommand('result',original,new AbortController().signal),(error:unknown)=>error instanceof RecoveryHttpError&&error.kind==='changed');}finally{globalThis.fetch=previous;}
});
test('A generic proof rejection retains neither backend problem details nor an account-existence outcome',async()=>{
 const previous=globalThis.fetch;globalThis.fetch=async input=>String(input)==='/authentication/options'?Response.json(context):Response.json({error_code:'RECOVERY_PROOF_REJECTED',private_fixture_detail:'not-for-client-state'},{status:400});
 try{await assert.rejects(recoveryCommand('result',original,new AbortController().signal),(error:unknown)=>error instanceof RecoveryHttpError&&error.kind==='unknown'&&!JSON.stringify(error).includes('not-for-client-state')&&!('details' in error));}finally{globalThis.fetch=previous;}
});
