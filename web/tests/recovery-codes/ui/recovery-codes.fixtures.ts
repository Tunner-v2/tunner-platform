import {randomUUID} from 'node:crypto';
import {expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import {setup} from '../../passkeys/fixtures';
import proposal from '../../../apps/user/recovery-codes.content.proposal.json' with {type:'json'};
import step from '../../../apps/user/step-up.content.proposal.json' with {type:'json'};
import totp from '../../../apps/user/totp.content.proposal.json' with {type:'json'};
import type {RecoveryCodeKind,RecoveryCodeOperation} from '../../../apps/user/recovery-codes.api';

// PUBLIC MOCKS ONLY. No native owner/profile/cryptography, real codes or file authority exists here.
export const publicCodes=['A'.repeat(32),'B'.repeat(32)];
const published=(values:Record<string,string>)=>Object.fromEntries(Object.entries(values).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'PUBLIC-RECOVERY',versionRef:'PUBLIC-TEXT-V1',publicationRef:'NOT-PRODUCTION'}]));
const impactKey='public.recovery.impact',factor='33333333-3333-4333-8333-333333333333';
export async function recoveryFixture(page:Page,options:{loseStart?:boolean;loseBody?:boolean;wrongDelivery?:boolean;ownerFailure?:'passkey'|'totp';missingImpact?:boolean;wrongProof?:boolean}={}){
 const account=await setup(page,{recoveryCodesAvailable:true});
 const state={operation:null as RecoveryCodeOperation|null,nextOwner:randomUUID(),version:1,proofRequest:randomUUID(),starts:[] as {ownerRequestId:string;expectedVersion:number;kind:RecoveryCodeKind}[],startReads:0,resultReads:0,cancels:0,optionReads:0,proofOptionReads:0,publicationUnavailable:false,commits:0,canGenerate:true,canReplace:true,missingImpact:options.missingImpact===true,passwords:0,passkeys:0,totps:0,passkeyContinues:0,totpContinues:0,nativeAttempt:'',ceremony:'',nativeStatus:'PENDING',totpAttempt:'',totpStatus:'PROOF_ACTIVE',failedOwner:false};
 const content=()=>{const all=published({...proposal.content,[impactKey]:'PUBLIC OWNER IMPACT — exact test publication; no production policy.'});if(state.missingImpact&&state.operation)delete all[impactKey];return all;};
 const metadata=()=>({csrfToken:'PUBLIC-RECOVERY-CSRF',ownerRequestId:state.nextOwner,expectedVersion:state.version,policyVersion:'PUBLIC-POLICY-V1',formatProfileVersion:'NATIVE_HEX32_V1',deliveryProfileVersion:'PUBLIC-DELIVERY-V1',codesPerSet:2,maximumCodesPerSet:4,canGenerate:state.canGenerate,canReplace:state.canReplace,activeSetCount:state.commits,remainingCodeCount:state.commits*2,locale:'en',content:content(),recovery:state.operation?{...state.operation,impact:{...state.operation.impact,canContinue:state.operation.status==='PENDING_PROOF'}}:null});
 const completed=()=>({status:'COMPLETED',signInRequired:false,operationReference:state.operation!.operationReference});
 const nativeRecovery=()=>({requestId:state.proofRequest,attemptId:state.nativeAttempt,ceremonyId:state.ceremony,operationReference:state.operation!.operationReference,status:state.nativeStatus,expiresAt:'2030-01-01T00:00:00Z',publicKey:null});
 const totpRecovery=()=>({requestId:state.proofRequest,attemptId:state.totpAttempt,authenticatorReference:factor,status:state.totpStatus,expiresAt:'2030-01-01T00:00:00Z'});
 const stepContent=published({...step.content,...totp.content});
 const proofOptions=()=>state.operation?.status==='COMPLETED'?{status:'COMPLETED',requestId:state.proofRequest,operationReference:state.operation.operationReference,totpRecovery:state.totpAttempt?{...totpRecovery(),status:'CONSUMED'}:null}:{status:'PENDING',requestId:state.proofRequest,operationReference:options.wrongProof?randomUUID().replaceAll('-',''):state.operation!.operationReference,csrfToken:'PUBLIC-PROOF-CSRF',passwordMaximumLength:128,passwordAvailable:true,passkeyAvailable:true,maximumCredentialJsonBytes:131072,passkeyRecovery:state.nativeAttempt?nativeRecovery():null,totpAvailable:true,totpAuthenticators:[{authenticatorReference:factor,version:1,state:'ACTIVE',createdAt:'2026-11-01T00:00:00Z',activatedAt:'2026-11-01T00:00:00Z',displayPosition:1}],totpRecovery:state.totpAttempt?totpRecovery():null,locale:'en',content:stepContent};
 function winner(){const op=state.operation!;const delivery={kind:'RECOVERY_CODES_FIRST_DELIVERY_V1',ownerRequestId:options.wrongDelivery?state.proofRequest:op.ownerRequestId,stepUpRequestId:state.proofRequest,operationReference:op.operationReference,setReference:op.setReference,ownerContextDigest:op.ownerContextDigest,deliveryProfileVersion:op.deliveryProfileVersion,formatProfileVersion:op.formatProfileVersion,codes:publicCodes};state.commits++;state.version++;state.nativeStatus='CONSUMED';state.totpStatus='CONSUMED';state.operation={...op,status:'COMPLETED',codesUnavailable:true,impact:{...op.impact,canContinue:false}};return{...completed(),recoveryCodeFirstDelivery:delivery};}
 await page.route('**/account/recovery-codes/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().method()==='POST'?route.request().postDataJSON():{};
  if(path.endsWith('/options')){state.optionReads++;if(state.publicationUnavailable&&state.operation)return route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}});return route.fulfill({json:metadata()});}
  if(path.endsWith('/generate/start')||path.endsWith('/replace/start')){
   const kind:RecoveryCodeKind=path.endsWith('/generate/start')?'GENERATE':'REPLACE';expect(body).toEqual({ownerRequestId:state.nextOwner,expectedVersion:state.version});state.starts.push({...body,kind});
   state.operation={status:'PENDING_PROOF',ownerRequestId:body.ownerRequestId,operationReference:randomUUID().replaceAll('-',''),setReference:randomUUID(),ownerContextDigest:'A'.repeat(64),deliveryProfileVersion:'PUBLIC-DELIVERY-V1',formatProfileVersion:'NATIVE_HEX32_V1',codesPerSet:2,maximumCodesPerSet:4,expiresAt:'2030-01-01T00:00:00Z',expectedVersion:body.expectedVersion,kind,impact:{impactKey,factsDigest:'B'.repeat(64),publicationVersion:'PUBLIC-BUNDLE-V1',publicationDigest:'C'.repeat(64),publicationValidity:{from:'2026-01-01T00:00:00Z',includesFrom:true,until:null,includesUntil:false},canContinue:false,priorSetCount:state.commits,remainingCodeCount:state.commits*2,sessionEffect:'NO_SESSION_MUTATION',replacementMechanism:'ATOMIC_RETIRE_EXACT_EXPECTED_SETS_AT_ISSUE_COMMIT_V1'},codesUnavailable:false};state.nextOwner=randomUUID();
   if(options.loseStart){options.loseStart=false;return route.abort('connectionreset');}return route.fulfill({json:state.operation});
  }
  if(path.endsWith('/start-result')){state.startReads++;expect(body).toEqual(state.starts[0]);return route.fulfill({json:state.operation});}
  expect(body).toEqual({ownerRequestId:state.operation!.ownerRequestId,operationReference:state.operation!.operationReference});
  if(path.endsWith('/cancel')){state.cancels++;if(state.operation!.status!=='COMPLETED')state.operation={...state.operation!,status:'REJECTED',impact:{...state.operation!.impact,canContinue:false}};}
  else if(path.endsWith('/result'))state.resultReads++;else return route.fulfill({status:404,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}});
  return route.fulfill({json:state.operation});
 });
 await page.route('**/authentication/step-up/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().method()==='POST'?route.request().postDataJSON():{};
  if(path==='/authentication/step-up/options'){state.proofOptionReads++;return route.fulfill({json:proofOptions()});}
  expect(body.requestId).toBe(state.proofRequest);
  if(path.endsWith('/passkey/options')){state.nativeAttempt=body.attemptId;state.ceremony=randomUUID();return route.fulfill({json:{...nativeRecovery(),publicKey:{challenge:'c3ludGhldGlj',rpId:'localhost',userVerification:'required'}}});}
  const method=path.includes('/passkey')?'passkey':path.includes('/totp')?'totp':'password',continuation=path.endsWith('/continue');
  if(method==='password'){state.passwords++;expect(typeof body.password).toBe('string');}
  if(method==='passkey'){expect(body.attemptId).toBe(state.nativeAttempt);expect(body.ceremonyId).toBe(state.ceremony);if(continuation){state.passkeyContinues++;expect(state.nativeStatus).toBe('PROOF_ISSUED');}else{state.passkeys++;expect(body.credential).toBeTruthy();}}
  if(method==='totp'){expect(body.authenticatorReference).toBe(factor);if(continuation){state.totpContinues++;expect(body.attemptId).toBe(state.totpAttempt);expect(state.totpStatus).toBe('PROOF_ACTIVE');}else{state.totps++;state.totpAttempt=body.attemptId;expect(body.code).toBe('000123');}}
  if(options.ownerFailure===method&&!state.failedOwner){state.failedOwner=true;state.nativeStatus='PROOF_ISSUED';state.totpStatus='PROOF_ACTIVE';return route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}});}
  const value=winner();if(options.loseBody){options.loseBody=false;return route.abort('connectionreset');}
  return route.fulfill({json:{...value,...(method==='passkey'?{requestId:state.proofRequest,attemptId:state.nativeAttempt,ceremonyId:state.ceremony}:{})}});
 });
 return {state,account};
}
export async function beginRecovery(page:Page,kind:'GENERATE'|'REPLACE'){
 await page.goto('/account/security');await page.getByRole('button',{name:kind==='GENERATE'?'Generate codes':'Replace codes',exact:true}).click();
 if(kind==='REPLACE'){const dialog=page.getByRole('dialog');await expect(dialog).toContainText('PUBLIC OWNER IMPACT');await dialog.getByRole('button',{name:'Continue',exact:true}).click();}
 await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();
}
export async function completeRecoveryProof(page:Page,method:'password'|'passkey'|'totp'){
 const dialog=page.getByRole('dialog');if(method==='password'){await dialog.getByRole('button',{name:'Other method',exact:true}).click();await dialog.getByLabel('Password',{exact:true}).fill('public-nonfunctional-password');await dialog.getByRole('button',{name:'Confirm',exact:true}).click();}
 else if(method==='passkey')await dialog.getByRole('button',{name:'Passkey',exact:true}).click();
 else{await dialog.getByRole('button',{name:'Authenticator',exact:true}).click();await dialog.getByLabel('Verification code',{exact:true}).fill('000123');await dialog.getByRole('button',{name:'Confirm',exact:true}).click();}
}
