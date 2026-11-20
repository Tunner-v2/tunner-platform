import {randomUUID} from 'node:crypto';
import type {Page} from '@playwright/test';
import copy from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import {activeCredential,setup} from './fixtures';

// This server/browser fixture does not perform WebAuthn cryptographic verification.
export async function setupProof(page:Page,{available=true,passwordAvailable=true,terminal=false}={}){
  const owner=await setup(page,{credentials:[activeCredential()],passwordAvailable,effect:terminal?'RevokeCredentialSessions':'PreserveExistingSessions'});
  const requestId=randomUUID();
  const native={available,recovery:true,status:'PENDING',attemptId:'',ceremonyId:'',executions:0,assertions:0,continuations:0,options:[] as Array<Record<string,unknown>>,submissions:[] as Array<Record<string,unknown>>};
  const reference=()=>owner.operation!.ceremonyId.replaceAll('-','');
  const recovery=()=>({requestId,attemptId:native.attemptId,ceremonyId:native.ceremonyId,operationReference:reference(),status:native.status,expiresAt:new Date(Date.now()+300000).toISOString(),publicKey:null});
  const content=Object.fromEntries(Object.entries(copy.content).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'synthetic-proof-variant',versionRef:'synthetic-proof-version',publicationRef:'SYNTHETIC-LOCAL-TEST-ONLY'}]));
  function execute(){native.executions++;owner.proofs++;native.status='CONSUMED';if(owner.operation?.kind==='ENROLL')owner.operation.status='AUTHORIZED';else if(owner.operation){owner.operation.status='COMPLETED';const credential=owner.credentials.find(value=>value.credentialReference===owner.operation!.credentialReference)!;credential.state='REVOKED';credential.version++;credential.revokedAt='2026-11-10T15:00:00+00:00';if(terminal){owner.authenticated=false;owner.terminal=true;}}}
  const completed=()=>({status:'COMPLETED',requestId,attemptId:native.attemptId,ceremonyId:native.ceremonyId,operationReference:reference(),signInRequired:owner.terminal});
  await page.route('**/authentication/step-up/options',route=>route.fulfill({json:{status:'PENDING',requestId,operationReference:reference(),csrfToken:'fixture-account-csrf',passwordMaximumLength:128,passwordAvailable,passkeyAvailable:native.available,maximumCredentialJsonBytes:native.available?131072:null,passkeyRecovery:native.recovery&&native.attemptId?recovery():null,locale:'en',content}}));
  await page.route('**/authentication/step-up/passkey/options',route=>{const body=route.request().postDataJSON();native.options.push(body);if(body.requestId!==requestId)return route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}});if(!native.attemptId){native.attemptId=body.attemptId;native.ceremonyId=randomUUID();}if(body.attemptId!==native.attemptId)return route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}});return route.fulfill({json:{...recovery(),publicKey:{challenge:'c3ludGhldGlj',rpId:'localhost',userVerification:'required'}}});});
  for(const action of ['passkey','passkey/continue'])await page.route('**/authentication/step-up/'+action,route=>{const body=route.request().postDataJSON();native.submissions.push(body);if(body.requestId!==requestId||body.attemptId!==native.attemptId||body.ceremonyId!==native.ceremonyId)return route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}});if(action==='passkey'){native.assertions++;if(!body.credential)return route.fulfill({status:400,json:{error_code:'VALIDATION_ERROR'}});}else{native.continuations++;if(native.status!=='PROOF_ISSUED')return route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}});}execute();return route.fulfill({json:completed()});});
  return {owner,native,requestId,reference,recovery,completed,execute};
}

export async function reviewEnrollment(page:Page){await page.goto('/account/security/sign-in-methods');await page.getByRole('button',{name:'Add passkey',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();}
export const proofDialog=(page:Page)=>page.getByRole('dialog');
export const beginNative=(page:Page)=>proofDialog(page).getByRole('button',{name:'Passkey',exact:true}).click();
export const counts=(page:Page)=>page.evaluate(()=>(window as unknown as {passkeyFixture:{gets:number;creates:number}}).passkeyFixture);
