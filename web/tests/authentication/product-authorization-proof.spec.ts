import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import stepCopy from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import totpCopy from '../../apps/user/totp.content.proposal.json' with {type:'json'};
import {nativeFixture} from '../passkeys/fixtures';

// Actual Product/shared React controllers with synthetic server publications and proof receipts.
// No cryptographic proof, production policy, native consumption or issuance is established by this fixture.
type Method='password'|'passkey'|'totp'|'federation';
const requestId='12121212-3434-4567-8989-121212121212',attemptId='23232323-4545-4678-8989-232323232323',ceremonyId='34343434-5656-4789-8989-343434343434';
const factorId='45454545-6767-4890-8989-454545454545',linkId='56565656-7878-4901-8989-565656565656',operationReference='original-product-review-operation';
const productCopy={
 'brand.tunner':'Tunner','authorization.heading':'Continue to {productName}','authorization.description':'Review what you’ll share before you continue.',
 'authorization.product':'Product','authorization.account':'Signed in to Tunner','authorization.permissions':'{productName} will receive',
 'authorization.assurance':'Sign-in requirements','authorization.callback':'Return to','authorization.allow':'Allow','authorization.deny':'Deny',
 'authorization.switchAccount':'Use another account','authorization.verify':'Continue sign-in','authorization.working':'Please wait…',
 'authorization.changed':'This request has changed or expired. Return to the product to start again.',
 'authorization.unavailable':'We couldn’t complete this request. Return to the product and try again.',
 'authorization.proofRequired':'Additional sign-in verification is needed before you can continue.','authorization.loginRequired':'Sign in again before you continue.',
 'authorization.boundary':'Tunner handles your identity. Product roles, workspaces, and permissions remain inside {productName}.',
 'authorization.footer.privacy':'Privacy Policy','authorization.footer.terms':'Terms of Service','authorization.footer.support':'Contact Support'
};
function published(values:Record<string,string>){return Object.fromEntries(Object.entries(values).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'synthetic-proof-variant',versionRef:'synthetic-proof-version',publicationRef:'SYNTHETIC-TEST-PUBLICATION-ONLY'}]));}
async function setup(page:Page,method:Method='password',restored=false){
 await nativeFixture(page);
 const state={prepared:false,operationReference,proofPosts:0,optionsReads:0,productReads:0,nativeEntries:0,decisionPosts:[] as Array<Record<string,unknown>>,proofBodies:[] as Array<Record<string,unknown>>,nativeAttempt:restored?attemptId:''};
 const ready=()=>({status:'PROOF_READY',operationReference:state.operationReference});
 const scopes=published({openid:'Your Tunner identity'}),content=published(productCopy),proofContent=published({...stepCopy.content,...totpCopy.content,...totpCopy.step_up_content});
 const nativeRecovery=()=>({requestId,attemptId:state.nativeAttempt,ceremonyId,operationReference:state.operationReference,status:restored?'PROOF_ISSUED':'PENDING',expiresAt:'2099-01-01T00:00:00Z',publicKey:null});
 const federation={requestId,attemptId,provider:'Google',linkReference:linkId,status:'VERIFIED',expiresAt:'2099-01-01T00:00:00Z',errorCode:null};
 const factor={authenticatorReference:factorId,version:1,state:'ACTIVE',createdAt:'2026-01-01T00:00:00Z',activatedAt:'2026-01-01T00:00:00Z',displayPosition:1};
 const pending=()=>({status:'PENDING',requestId,operationReference:state.operationReference,csrfToken:'synthetic-proof-csrf',passwordMaximumLength:128,passwordAvailable:method==='password',locale:'en',content:proofContent,
  ...(method==='passkey'?{passkeyAvailable:true,maximumCredentialJsonBytes:131072,passkeyRecovery:state.nativeAttempt?nativeRecovery():null}:{}),
  ...(method==='totp'?{totpAvailable:true,totpAuthenticators:[factor],totpRecovery:restored?{requestId,attemptId,authenticatorReference:factorId,status:'PROOF_ACTIVE',expiresAt:'2099-01-01T00:00:00Z'}:null}:{}),
  ...(method==='federation'?{federation:{choices:[],recovery:federation}}:{})});
 await page.route('**/product-authorization/options',route=>{state.productReads++;return route.fulfill({json:{reference:state.prepared?'fresh-review-reference':'original-review-reference',csrfToken:'synthetic-review-csrf',canAllow:state.prepared,interactionStatus:state.prepared?'Satisfied':'RequiresFreshProof',stepUpAvailable:!state.prepared,operationReference:state.operationReference,locale:'en',content,productName:'Product A',accountLabel:'Alex Example',registeredReturnOrigin:'https://product-a.example',requestedAssuranceLabel:state.prepared?'This request’s verification requirements are met.':'Additional sign-in verification is required.',issuedClaimNames:state.prepared?['sub']:[],scopeDescriptions:scopes,termsUri:'https://fixture.example/terms',privacyUri:'https://fixture.example/privacy',supportUri:null}});});
 await page.route('**/authentication/step-up/options',route=>{state.optionsReads++;return route.fulfill({json:state.prepared?ready():pending()});});
 await page.route('**/authentication/session',route=>route.fulfill({json:{authenticated:true,csrfToken:'synthetic-proof-csrf'}}));
 await page.route('**/product-authorization/decision',route=>{state.decisionPosts.push(route.request().postDataJSON());expect(route.request().headers()['x-tunner-csrf']).toBe('synthetic-review-csrf');return route.fulfill({json:{resumePath:'/connect/authorize'}});});
 await page.route('**/connect/authorize',route=>{state.nativeEntries++;return route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="en"><title>Native fixture</title><body><h1>Explicit native authorization fixture</h1></body></html>'});});
 await page.route('**/authentication/step-up/passkey/options',route=>{const body=route.request().postDataJSON();expect(body.requestId).toBe(requestId);state.nativeAttempt=body.attemptId;return route.fulfill({json:{...nativeRecovery(),publicKey:{challenge:'c3ludGhldGlj',rpId:'localhost',userVerification:'required'}}});});
 await page.route('**/authentication/step-up/federation/result',route=>route.fulfill({json:state.prepared?ready():{requestId,operationReference:state.operationReference,federation}}));
 return {state,ready,pending,nativeRecovery};
}
function proofPath(method:Method,restored=false){return '/authentication/step-up/'+(method==='federation'?'federation/continue':method==='password'?'password':method+(restored?'/continue':''));}
async function open(page:Page){await page.goto('/product-authorization');await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeDisabled();await page.getByRole('button',{name:'Continue sign-in',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();}
async function submit(page:Page,method:Method,restored=false){
 const dialog=page.getByRole('dialog');
 if(method==='password'){await dialog.getByRole('button',{name:'Other method',exact:true}).click();await dialog.getByLabel('Password',{exact:true}).fill('synthetic existing password');await dialog.getByRole('button',{name:'Confirm',exact:true}).click();}
 else if(method==='passkey'&&!restored)await dialog.getByRole('button',{name:'Passkey',exact:true}).click();
 else if(method==='totp'&&!restored){await dialog.getByRole('button',{name:/^Authenticator/}).click();await dialog.getByLabel('Verification code',{exact:true}).fill('123456');await dialog.getByRole('button',{name:'Confirm',exact:true}).click();}
 else await dialog.getByRole('button',{name:'Continue this verification',exact:true}).click();
}
async function noLeaks(page:Page){expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});}

for(const method of ['password','passkey','totp','federation'] as const)for(const lost of [false,true])test('Product shared '+method+' proof '+(lost?'lost response':'ready receipt')+' never authorizes until explicit Allow',async({page})=>{
 const fixture=await setup(page,method);
 await page.route('**'+proofPath(method),async route=>{fixture.state.proofPosts++;const body=route.request().postDataJSON();fixture.state.proofBodies.push(body);expect(body.requestId).toBe(requestId);fixture.state.prepared=true;
  if(lost){await route.abort('connectionreset');return;}
  await route.fulfill({json:{...fixture.ready(),...(method==='passkey'?{requestId,attemptId:fixture.state.nativeAttempt,ceremonyId}:{})}});
 });
 await open(page);await submit(page,method);
 if(lost&&method==='federation'){await expect(page.getByRole('dialog').getByRole('button',{name:'Check verification status',exact:true})).toBeEnabled();await page.getByRole('dialog').getByRole('button',{name:'Check verification status',exact:true}).click();}
 await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();expect(fixture.state.proofPosts).toBe(1);expect(fixture.state.decisionPosts).toHaveLength(0);expect(fixture.state.nativeEntries).toBe(0);await expect(page).toHaveURL('http://127.0.0.1:5189/product-authorization');await noLeaks(page);
 await page.getByRole('button',{name:'Allow',exact:true}).click();await expect(page.getByRole('heading',{name:'Explicit native authorization fixture'})).toBeVisible();expect(fixture.state.decisionPosts).toEqual([{reference:'fresh-review-reference',decision:'ALLOW'}]);expect(fixture.state.proofPosts).toBe(1);expect(fixture.state.nativeEntries).toBe(1);await noLeaks(page);
});

for(const method of ['passkey','totp','federation'] as const)test('Product shared '+method+' protected pending proof continues without starting another proof',async({page})=>{
 const fixture=await setup(page,method,true);
 await page.route('**'+proofPath(method,true),route=>{fixture.state.proofPosts++;fixture.state.proofBodies.push(route.request().postDataJSON());fixture.state.prepared=true;return route.fulfill({json:{...fixture.ready(),...(method==='passkey'?{requestId,attemptId,ceremonyId}:{})}});});
 await open(page);await submit(page,method,true);await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();expect(fixture.state.proofPosts).toBe(1);expect(fixture.state.proofBodies[0]).toMatchObject({requestId});expect(fixture.state.decisionPosts).toHaveLength(0);expect(fixture.state.nativeEntries).toBe(0);
 if(method==='passkey')expect(await page.evaluate(()=>(window as unknown as {passkeyFixture:{gets:number}}).passkeyFixture.gets)).toBe(0);
 await noLeaks(page);
});

for(const invalid of ['wrongOperation','missingOperation','unexpectedDelivery','sessionAssertion','completed'] as const)test('Product shared password rejects '+invalid+' without promoting a session or authorizing',async({page})=>{
 const fixture=await setup(page);
 await page.route('**/authentication/step-up/password',route=>{fixture.state.proofPosts++;const raw:Record<string,unknown>={...fixture.ready()};if(invalid==='wrongOperation')raw.operationReference='another-operation';if(invalid==='missingOperation')delete raw.operationReference;if(invalid==='unexpectedDelivery')raw.recoveryCodeFirstDelivery={codes:['SYNTHETIC-NOT-DELIVERABLE']};if(invalid==='sessionAssertion')raw.signInRequired=false;if(invalid==='completed'){raw.status='COMPLETED';raw.signInRequired=false;}return route.fulfill({json:raw});});
 await open(page);await submit(page,'password');
 if(invalid==='completed'){await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('alert')).toHaveText(productCopy['authorization.changed']);}
 else await expect(page.getByRole('dialog').getByRole('alert')).toHaveText(stepCopy.content['stepUp.error.unavailable']);
 await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeDisabled();expect(fixture.state.prepared).toBe(false);expect(fixture.state.proofPosts).toBe(1);expect(fixture.state.decisionPosts).toHaveLength(0);expect(fixture.state.nativeEntries).toBe(0);await expect(page.getByText('SYNTHETIC-NOT-DELIVERABLE')).toHaveCount(0);await noLeaks(page);
});

test('Product proof refresh rejects a changed owner even after an exact shared ready receipt',async({page})=>{
 const fixture=await setup(page);await page.route('**/authentication/step-up/password',route=>{fixture.state.proofPosts++;fixture.state.prepared=true;const raw=fixture.ready();fixture.state.operationReference='replacement-product-request';return route.fulfill({json:raw});});
 await open(page);await submit(page,'password');await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('alert')).toHaveText(productCopy['authorization.changed']);await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeDisabled();expect(fixture.state.decisionPosts).toHaveLength(0);expect(fixture.state.nativeEntries).toBe(0);await noLeaks(page);
});

test('Product proof cancellation preserves the exact owner and reopening uses current server context',async({page})=>{
 const fixture=await setup(page);await open(page);await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('button',{name:'Continue sign-in',exact:true})).toBeFocused();await page.getByRole('button',{name:'Continue sign-in',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();expect(fixture.state.optionsReads).toBe(2);expect(fixture.state.proofPosts).toBe(0);expect(fixture.state.decisionPosts).toHaveLength(0);await noLeaks(page);
});

test('Ordinary shared options reject proof-ready while exact Product opt-in accepts readiness only',async({page})=>{
 const fixture=await setup(page);await page.goto('/product-authorization');await expect(page.getByRole('button',{name:'Continue sign-in',exact:true})).toBeEnabled();fixture.state.prepared=true;
 const result=await page.evaluate(async expected=>{const path='/registration/step-up.tsx';const api=await import(/* @vite-ignore */path);let ordinaryRejected=false;try{await api.loadStepUpOptions();}catch{ordinaryRejected=true;}const ready=await api.loadStepUpOptions(undefined,expected);return {ordinaryRejected,ready};},operationReference);
 expect(result).toEqual({ordinaryRejected:true,ready:{status:'PROOF_READY',operationReference}});expect(fixture.state.decisionPosts).toHaveLength(0);await noLeaks(page);
});

test('Shared outcome parser rejects ready for ordinary owners and never consumes first-delivery on readiness',async({page})=>{
 await setup(page);await page.goto('/product-authorization');await expect(page.getByRole('button',{name:'Continue sign-in',exact:true})).toBeEnabled();
 const result=await page.evaluate(async input=>{const path='/registration/step-up.completion.ts';const api=await import(/* @vite-ignore */path);let deliveries=0;const receiver={stepUpRequestId:input.requestId,operationReference:input.operationReference,consume:()=>{deliveries++;}};const raw={status:'PROOF_READY',operationReference:input.operationReference};const rejected=[];
  for(const [value,expected,allowed] of [[raw,input.operationReference,false],[raw,undefined,true],[{...raw,operationReference:'other'},input.operationReference,true],[{...raw,recoveryCodeFirstDelivery:{}},input.operationReference,true],[{...raw,signInRequired:false},input.operationReference,true]]){try{api.parseStepUpOutcome(value,input.requestId,expected,receiver,allowed);rejected.push(false);}catch{rejected.push(true);}}
  const beforeCompletion=deliveries;const ready=api.parseStepUpOutcome(raw,input.requestId,input.operationReference,receiver,true);const completed=api.parseStepUpOutcome({status:'COMPLETED',operationReference:input.operationReference,signInRequired:false,recoveryCodeFirstDelivery:{}},input.requestId,input.operationReference,receiver);return {rejected,beforeCompletion,deliveries,ready,completed};
 },{requestId,operationReference});
 expect(result.rejected).toEqual([true,true,true,true,true]);expect(result.beforeCompletion).toBe(0);expect(result.deliveries).toBe(1);expect(result.ready).toEqual({status:'PROOF_READY',operationReference});expect(result.completed).toEqual({status:'COMPLETED',operationReference,signInRequired:false});
});

for(const width of [1440,390,320])test('Product shared proof retains existing chooser keyboard and axe at '+width,async({page},testInfo)=>{
 await page.setViewportSize({width,height:width===1440?1024:844});const fixture=await setup(page);await open(page);const dialog=page.getByRole('dialog');await expect(dialog.getByRole('heading',{name:'Confirm it’s you'})).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(dialog.getByRole('button',{name:'Cancel',exact:true})).toBeFocused();await page.keyboard.press('Tab');await expect(dialog.getByRole('button',{name:'Other method',exact:true})).toBeFocused();
 await page.evaluate(()=>document.fonts.ready);expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);const screenshot=testInfo.outputPath('product-shared-proof-'+width+'.png');await page.screenshot({path:screenshot,fullPage:true});await testInfo.attach('existing-shared-proof-'+width,{path:screenshot,contentType:'image/png'});expect(fixture.state.decisionPosts).toHaveLength(0);await noLeaks(page);
});
