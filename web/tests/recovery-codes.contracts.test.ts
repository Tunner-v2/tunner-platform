import test from 'node:test';
import assert from 'node:assert/strict';
import {matchesRecoveryStart,parseRecoveryOperation,parseRecoveryOptions,recoveryCodeDeliveryReceiver,requiredRecoveryCodeKeys,requireRecoveryImpact} from '../apps/user/recovery-codes.api';
import type {RecoveryCodeOperation} from '../apps/user/recovery-codes.api';
import {parseStepUpCompletion,rejectStepUpRecoveryDelivery} from '../apps/user/step-up.completion';

// Public nonfunctional values only. No account, admitted runtime profile or verifier accepts them.
const owner='11111111-1111-4111-8111-111111111111',proof='22222222-2222-4222-8222-222222222222',set='33333333-3333-4333-8333-333333333333',reference='4'.repeat(32);
const original=():RecoveryCodeOperation=>({status:'PENDING_PROOF',ownerRequestId:owner,operationReference:reference,setReference:set,ownerContextDigest:'A'.repeat(64),deliveryProfileVersion:'PUBLIC-DELIVERY-V1',formatProfileVersion:'NATIVE_HEX32_V1',codesPerSet:2,maximumCodesPerSet:4,expiresAt:'2030-01-01T00:00:00Z',expectedVersion:1,kind:'GENERATE',impact:{impactKey:'public.recovery.impact',factsDigest:'B'.repeat(64),publicationVersion:'PUBLIC-BUNDLE-V1',publicationDigest:'C'.repeat(64),publicationValidity:{from:'2026-01-01T00:00:00Z',includesFrom:true,until:null,includesUntil:false},canContinue:true,priorSetCount:0,remainingCodeCount:0,sessionEffect:'NO_SESSION_MUTATION',replacementMechanism:'ATOMIC_RETIRE_EXACT_EXPECTED_SETS_AT_ISSUE_COMMIT_V1'},codesUnavailable:false});
function bootstrap(){const op=original();return {csrfToken:'PUBLIC-NONFUNCTIONAL-CSRF',ownerRequestId:owner,expectedVersion:1,policyVersion:'PUBLIC-POLICY-V1',formatProfileVersion:'NATIVE_HEX32_V1',deliveryProfileVersion:op.deliveryProfileVersion,codesPerSet:2,maximumCodesPerSet:4,canGenerate:true,canReplace:true,activeSetCount:2,remainingCodeCount:3,locale:'en',content:Object.fromEntries([...requiredRecoveryCodeKeys,op.impact.impactKey].map(key=>[key,{key,text:'Public nonfunctional publication',locale:'en',state:'PUBLISHED',variantRef:'PUBLIC',versionRef:'PUBLIC-V1',publicationRef:'NOT-PRODUCTION'}])),recovery:null};}
function payload(){const op=original();return {kind:'RECOVERY_CODES_FIRST_DELIVERY_V1',ownerRequestId:op.ownerRequestId,stepUpRequestId:proof,operationReference:op.operationReference,setReference:op.setReference,ownerContextDigest:op.ownerContextDigest,deliveryProfileVersion:op.deliveryProfileVersion,formatProfileVersion:op.formatProfileVersion,codes:['A'.repeat(32),'B'.repeat(32)]};}
const response=()=>({status:'COMPLETED',signInRequired:false,operationReference:reference,recoveryCodeFirstDelivery:payload()});

test('recovery options preserve both independently admitted actions without choosing capacity or targets',()=>{
 const boot=parseRecoveryOptions(bootstrap());assert.equal(boot.canGenerate,true);assert.equal(boot.canReplace,true);assert.equal(boot.activeSetCount,2);assert.equal(boot.maximumCodesPerSet,4);
});
test('safe recovery options and operation reads reject a delivery field even when null',()=>{
 for(const value of [null,payload()]){assert.throws(()=>parseRecoveryOptions({...bootstrap(),recoveryCodeFirstDelivery:value}));assert.throws(()=>parseRecoveryOperation({...original(),recoveryCodeFirstDelivery:value}));assert.throws(()=>rejectStepUpRecoveryDelivery({status:'COMPLETED',recoveryCodeFirstDelivery:value}));}
 assert.throws(()=>parseRecoveryOperation({...original(),codes:payload().codes}));
});
test('missing published current impact never becomes an enabled pending operation',()=>{
 const boot=bootstrap();const op=original();const current=parseRecoveryOptions({...boot,recovery:op});requireRecoveryImpact(op,current);
 requireRecoveryImpact({...op,impact:{...op.impact,canContinue:false}},current); // Start/read is conservative; only the separately compared current options flag grants continuation.
 assert.throws(()=>requireRecoveryImpact(op,parseRecoveryOptions({...boot,recovery:{...op,impact:{...op.impact,canContinue:false}}})));
 assert.throws(()=>requireRecoveryImpact({...op,impact:{...op.impact,impactKey:'not-published'}},current));
 assert.throws(()=>parseRecoveryOptions({...boot,recovery:op,content:{}}));
});
test('safe versions, profiles and explicit count bounds fail closed before use',()=>{
 for(const delta of [{expectedVersion:Number.MAX_SAFE_INTEGER+1},{codesPerSet:5,maximumCodesPerSet:4},{policyVersion:'bad\nprofile'},{formatProfileVersion:'UNKNOWN'}])assert.throws(()=>parseRecoveryOptions({...bootstrap(),...delta}));
});
test('current publication must match held original version, digest, key, facts and exact interval',()=>{
 const op=original(),base=bootstrap();for(const change of [{impactKey:'another-key'},{factsDigest:'D'.repeat(64)},{publicationVersion:'PUBLIC-BUNDLE-V2'},{publicationDigest:'E'.repeat(64)},{publicationValidity:{...op.impact.publicationValidity,includesFrom:false}},{publicationValidity:{...op.impact.publicationValidity,until:'2030-01-01T00:00:00Z'}}]){
  const fresh=parseRecoveryOptions({...base,recovery:{...op,impact:{...op.impact,...change}},content:{...base.content,'another-key':{...base.content[op.impact.impactKey]!,key:'another-key'}}});assert.throws(()=>requireRecoveryImpact(op,fresh));
 }
});
test('start reconciliation is exact original owner identity and kind, not a new request',()=>{
 const op=original();assert.equal(matchesRecoveryStart(op,op),true);assert.throws(()=>parseRecoveryOperation({...op,ownerRequestId:proof},op));assert.throws(()=>parseRecoveryOperation({...op,kind:'REPLACE'},op));assert.throws(()=>parseRecoveryOperation({...op,expectedVersion:2},op));
});
test('safe completed operation means unavailable, never a fresh display grant',()=>{
 const op=original();const completed=parseRecoveryOperation({...op,status:'COMPLETED',codesUnavailable:true,impact:{...op.impact,canContinue:false}},op);assert.equal(completed.codesUnavailable,true);
 assert.throws(()=>parseRecoveryOperation({...completed,codesUnavailable:false}));assert.throws(()=>parseRecoveryOperation({...completed,impact:{...op.impact,canContinue:true}}));
});
test('winning delivery is synchronously captured before safe completion and only once',()=>{
 const order:string[]=[];const receive=recoveryCodeDeliveryReceiver(original(),proof,()=>true,codes=>{assert.equal(codes.length,2);order.push('capture');});
 const safe=parseStepUpCompletion(response(),proof,reference,receive);order.push('safe-completion');assert.deepEqual(order,['capture','safe-completion']);assert.deepEqual(Object.keys(safe).sort(),['operationReference','signInRequired','status']);assert.throws(()=>parseStepUpCompletion(response(),proof,reference,receive));
});
test('owner and proof request identifiers are independently bound',()=>{
 let captures=0;const receive=()=>recoveryCodeDeliveryReceiver(original(),proof,()=>true,()=>{captures++;});
 assert.throws(()=>parseStepUpCompletion({...response(),recoveryCodeFirstDelivery:{...payload(),ownerRequestId:proof}},proof,reference,receive()));
 assert.throws(()=>parseStepUpCompletion({...response(),recoveryCodeFirstDelivery:{...payload(),stepUpRequestId:owner}},proof,reference,receive()));
 assert.throws(()=>parseStepUpCompletion(response(),owner,reference,receive()));assert.equal(captures,0);
});
test('all immutable set, context and profile bindings must match the held original',()=>{
 let captures=0;for(const change of [{operationReference:'5'.repeat(32)},{setReference:owner},{ownerContextDigest:'C'.repeat(64)},{deliveryProfileVersion:'OTHER-DELIVERY'},{formatProfileVersion:'UNKNOWN'}]){
  const receive=recoveryCodeDeliveryReceiver(original(),proof,()=>true,()=>{captures++;});assert.throws(()=>parseStepUpCompletion({...response(),recoveryCodeFirstDelivery:{...payload(),...change}},proof,reference,receive));
 }assert.equal(captures,0);
});
test('delivery count, uniqueness and canonical response representation are admitted before capture',()=>{
 let captures=0;for(const codes of [[],['A'.repeat(32)],['A'.repeat(32),'A'.repeat(32)],['XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX','B'.repeat(32)],['a'.repeat(32),'B'.repeat(32)]]){
  const receive=recoveryCodeDeliveryReceiver(original(),proof,()=>true,()=>{captures++;});assert.throws(()=>parseStepUpCompletion({...response(),recoveryCodeFirstDelivery:{...payload(),codes}},proof,reference,receive));
 }assert.equal(captures,0);
});
test('a canceled or stale receiver cannot capture a late winning body',()=>{
 let live=true,captures=0;const receive=recoveryCodeDeliveryReceiver(original(),proof,()=>live,()=>{captures++;});live=false;assert.throws(()=>parseStepUpCompletion(response(),proof,reference,receive));assert.equal(captures,0);
});
test('unexpected recipient, terminal combination and extra carrier fields reject rather than strip',()=>{
 const receive=()=>recoveryCodeDeliveryReceiver(original(),proof,()=>true,()=>assert.fail('Unexpected capture'));
 assert.throws(()=>parseStepUpCompletion(response(),proof,reference));assert.throws(()=>parseStepUpCompletion({...response(),signInRequired:true},proof,reference,receive()));assert.throws(()=>parseStepUpCompletion({...response(),recoveryCodeFirstDelivery:{...payload(),extra:true}},proof,reference,receive()));
});
test('legacy completion and body-loss status remain safe without manufacturing delivery',()=>{
 let captures=0;const receive=recoveryCodeDeliveryReceiver(original(),proof,()=>true,()=>{captures++;});assert.equal(parseStepUpCompletion({status:'COMPLETED',operationReference:reference},proof,reference,receive).signInRequired,false);assert.equal(captures,0);
 assert.deepEqual(parseStepUpCompletion({status:'COMPLETED',signInRequired:true,operationReference:reference},proof,reference),{status:'COMPLETED',signInRequired:true,operationReference:reference});
});
