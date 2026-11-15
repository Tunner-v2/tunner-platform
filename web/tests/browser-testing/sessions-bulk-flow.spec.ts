import {expect,test} from '@playwright/test';
import type {BrowserContext,Page} from '@playwright/test';
import type {SessionBulkRevocationContext,SessionBulkRevocationOptions,SessionBulkRevocationResult} from '../../apps/user/sessions.bulk.api';
import stepUpProposal from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import {registerVerifiedAccount,runtime} from './fixtures';

// Use the existing trusted local certificate and real isolated BFF/SMTP fixtures.
test.use({ignoreHTTPSErrors:false});
const origin='https://localhost:'+runtime.ApplicationPort;
const root='/account/sessions/bulk-revocation';
type FrozenOriginal=SessionBulkRevocationContext&{preparedAt:string};

async function signIn(page:Page,email:string,password:string){
 await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back',exact:true})).toBeVisible();
 await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill(password);
 const signingIn=page.waitForResponse(value=>value.url().endsWith('/authentication/sign-in')&&value.request().method()==='POST');
 await page.getByRole('button',{name:'Sign in',exact:true}).click();expect((await signingIn).status()).toBe(200);
 await expect(page).toHaveURL(/\/signed-in$/);await expect(page.getByRole('heading',{name:'You’re signed in',exact:true})).toBeVisible();
}

/** Read this context's non-bearer reference before freezing the bulk manifest. Never touch frozen targets afterward. */
async function ownReference(page:Page){
 const current=await page.request.get('/authentication/session');expect(current.status()).toBe(200);
 const authenticated=await current.json() as {csrfToken:string};let continuation:string|undefined;
 for(let index=0;index<4;index++){
  const response=await page.request.post('/account/sessions/list',{headers:{Origin:origin,'X-Tunner-CSRF':authenticated.csrfToken},data:continuation?{continuation}:{}});
  expect(response.status()).toBe(200);
  const found=await response.json() as {sessions:Array<{reference:string;isCurrent:boolean}>;hasMore:boolean;continuation:string|null};
  const own=found.sessions.find(value=>value.isCurrent);if(own){expect(own.reference).toMatch(/^[0-9a-f-]{36}$/);return own.reference;}
  if(!found.hasMore||!found.continuation)break;continuation=found.continuation;
 }
 throw new Error('The real current-session reference was unavailable within this bounded fixture.');
}

function assertOriginal(actual:FrozenOriginal,expected:FrozenOriginal){
 expect(actual.requestId).toBe(expected.requestId);expect(actual.operationReference).toBe(expected.operationReference);
 expect(actual.targets).toEqual(expected.targets);expect(actual.preparedAt).toBe(expected.preparedAt);expect(actual.expiresAt).toBe(expected.expiresAt);
}
function assertReceipt(actual:SessionBulkRevocationResult,original:FrozenOriginal){
 expect(actual.status).toBe('REVOKED');expect(actual.requestId).toBe(original.requestId);expect(actual.operationReference).toBe(original.operationReference);
 expect(actual.targets).toHaveLength(original.targets.length);expect(new Set(actual.targets.map(value=>value.reference)).size).toBe(original.targets.length);
 const versions=new Map(original.targets.map(value=>[value.reference,BigInt(value.version)+1n]));
 for(const target of actual.targets){expect(versions.get(target.reference)).toBe(BigInt(target.version));expect(Number.isFinite(Date.parse(target.revokedAt))).toBe(true);}
}

test('real bulk sign-out preserves its original two targets through cancellation, a later sign-in and lost result delivery',async({page,request,browser},info)=>{
 test.setTimeout(120000);
 const credentials=await registerVerifiedAccount(page,request);await signIn(page,credentials.email,credentials.password);
 const actorReference=await ownReference(page);
 const contexts:BrowserContext[]=[];
 try{
  const targets:Array<{page:Page;reference:string}>=[];
  for(let index=0;index<2;index++){
   const context=await browser.newContext({baseURL:origin,ignoreHTTPSErrors:false});contexts.push(context);
   const target=await context.newPage();await signIn(target,credentials.email,credentials.password);targets.push({page:target,reference:await ownReference(target)});
  }
  const targetReferences=targets.map(value=>value.reference).sort();expect(new Set(targetReferences).size).toBe(2);expect(targetReferences).not.toContain(actorReference);
  const previews:unknown[]=[],activations:unknown[]=[],cancellations:unknown[]=[],resultBodies:unknown[]=[];
  let proofReads=0,proofWrites=0;
  page.on('request',value=>{
   if(value.url().endsWith('/authentication/step-up/options')&&value.method()==='GET')proofReads++;
   if(value.method()!=='POST')return;
   if(value.url().endsWith(root+'/preview'))previews.push(value.postDataJSON());
   if(value.url().endsWith(root+'/activate'))activations.push(value.postDataJSON());
   if(value.url().endsWith(root+'/cancel'))cancellations.push(value.postDataJSON());
   if(value.url().endsWith(root+'/result'))resultBodies.push(value.postDataJSON());
   if(value.url().endsWith('/authentication/step-up/password'))proofWrites++;
  });
  await page.setViewportSize({width:1440,height:1100});await page.getByRole('link',{name:'Account security',exact:true}).click();
  await expect(page).toHaveURL(/\/account\/security$/);
  const publication=page.waitForResponse(value=>value.url().endsWith(root+'/options')&&value.request().method()==='GET');
  await page.getByRole('link',{name:/^sessions\s*&\s*devices$/i}).click();await expect(page).toHaveURL(/\/account\/sessions$/);
  const published=await publication;expect(published.status()).toBe(200);const options=await published.json() as SessionBulkRevocationOptions;
  const text=(suffix:string)=>{const value=options.content['account.sessions.bulk.'+suffix];expect(value?.state).toBe('PUBLISHED');expect(value?.locale).toBe(options.locale);expect(Boolean(value?.text.trim())).toBe(true);return value!.text;};
  expect(text('cancel')).toBe('Keep signed in');
  const action=page.getByRole('button',{name:text('action'),exact:true});await expect(action).toBeEnabled();

  const firstPreview=page.waitForResponse(value=>value.url().endsWith(root+'/preview')&&value.request().method()==='POST');
  await action.click();const firstResponse=await firstPreview;expect(firstResponse.status()).toBe(200);const first=await firstResponse.json() as FrozenOriginal;
  expect(first.status).toBe('DRAFT');expect(first.activated).toBe(false);expect(first.targets.map(value=>value.reference).sort()).toEqual(targetReferences);
  let review=page.getByRole('dialog',{name:text('review.title'),exact:true});await expect(review).toBeVisible();
  await expect(review.locator('.sessions-bulk-set [data-session-reference]')).toHaveCount(2);
  expect(activations).toEqual([]);expect(proofReads).toBe(0);expect(proofWrites).toBe(0);
  const cancelling=page.waitForResponse(value=>value.url().endsWith(root+'/cancel')&&value.request().method()==='POST');
  await review.getByRole('button',{name:text('cancel'),exact:true}).click();const cancelled=await cancelling;expect(cancelled.status()).toBe(200);
  expect(await cancelled.json()).toEqual({requestId:first.requestId,status:'CANCELLED'});await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(cancellations).toEqual([{requestId:first.requestId}]);expect(activations).toEqual([]);expect(proofReads).toBe(0);expect(proofWrites).toBe(0);expect(resultBodies).toEqual([]);
  expect((await page.request.get('/authentication/session')).status()).toBe(200);
  for(const target of targets)expect((await target.page.request.get('/authentication/session')).status()).toBe(200);

  // These are server discovery requests, with no browser-selected targets or listing cursor.
  // The existing profile fits this account on one listing page; SQL and controlled UI tests cover forced pagination separately.
  const secondPreview=page.waitForResponse(value=>value.url().endsWith(root+'/preview')&&value.request().method()==='POST');
  await action.click();const secondResponse=await secondPreview;expect(secondResponse.status()).toBe(200);const original=await secondResponse.json() as FrozenOriginal;
  expect(previews).toEqual([{},{}]);expect(original.requestId).not.toBe(first.requestId);expect(original.status).toBe('DRAFT');expect(original.activated).toBe(false);
  expect(original.targets).toHaveLength(2);expect(original.targets.map(value=>value.reference).sort()).toEqual(targetReferences);
  expect(Number.isFinite(Date.parse(original.preparedAt))).toBe(true);expect(Number.isFinite(Date.parse(original.expiresAt))).toBe(true);
  for(const target of original.targets)expect(target.version).toMatch(/^[1-9][0-9]*$/);
  review=page.getByRole('dialog',{name:text('review.title'),exact:true});await expect(review).toBeVisible();
  const displayed=await review.locator('.sessions-bulk-set [data-session-reference]').evaluateAll(elements=>elements.map(element=>element.getAttribute('data-session-reference')).sort());
  expect(displayed).toEqual(targetReferences);
  const preparation=review.locator('time[datetime]');await expect(preparation).toHaveCount(1);
  expect(Date.parse((await preparation.getAttribute('datetime'))!)).toBe(Date.parse(original.preparedAt));
  await page.screenshot({path:info.outputPath('bulk-review-two-original-targets.png'),fullPage:false});

  // This fourth session is created after the original server-selected manifest, and must remain outside its effect.
  const laterContext=await browser.newContext({baseURL:origin,ignoreHTTPSErrors:false});contexts.push(laterContext);
  const later=await laterContext.newPage();await signIn(later,credentials.email,credentials.password);const laterReference=await ownReference(later);
  expect(targetReferences).not.toContain(laterReference);expect(original.targets.map(value=>value.reference)).not.toContain(laterReference);
  const activating=page.waitForResponse(value=>value.url().endsWith(root+'/activate')&&value.request().method()==='POST');
  await review.getByRole('button',{name:text('confirm'),exact:true}).click();const activatedResponse=await activating;expect(activatedResponse.status()).toBe(200);
  const activated=await activatedResponse.json() as FrozenOriginal;assertOriginal(activated,original);expect(activated.status).toBe('PENDING');expect(activated.activated).toBe(true);
  expect(activations).toEqual([{requestId:original.requestId}]);expect(previews).toEqual([{},{}]);
  const protectedOriginal=(await page.context().cookies()).find(value=>value.name==='__Host-Tunner.SessionBulkRevocation');
  expect(Boolean(protectedOriginal)).toBe(true);expect(protectedOriginal?.httpOnly).toBe(true);expect(protectedOriginal?.secure).toBe(true);expect(protectedOriginal?.sameSite).toBe('Strict');
  const originalExpiry=protectedOriginal!.expires;
  const proof=page.getByRole('dialog');await expect(proof.getByRole('button',{name:'Other method',exact:true})).toBeVisible();
  await proof.getByRole('button',{name:'Other method',exact:true}).click();

  // Forward every request to the actual owner first. Only delivery is lost; no outcome or receipt is fabricated.
  let loseResultDelivery=true,lostResults=0,lostSuccessfulProof=0;const receipts:SessionBulkRevocationResult[]=[];
  await page.route('**/account/sessions/bulk-revocation/result',async route=>{
   const actual=await route.fetch();expect(actual.status()).toBe(200);const receipt=await actual.json() as SessionBulkRevocationResult;
   assertReceipt(receipt,original);expect(receipt.targets.map(value=>value.reference)).not.toContain(laterReference);receipts.push(receipt);
   if(loseResultDelivery){lostResults++;await route.abort();}else await route.fulfill({response:actual});
  });
  await page.route('**/authentication/step-up/password',async route=>{
   const actual=await route.fetch();expect(actual.status()).toBe(200);const completed=await actual.json() as {status:string;operationReference:string;signInRequired:boolean};
   expect(completed.status).toBe('COMPLETED');expect(completed.operationReference).toBe(original.operationReference);expect(completed.signInRequired).toBe(false);
   lostSuccessfulProof++;await route.abort();
  });
  await proof.getByLabel('Password',{exact:true}).fill(credentials.password);await proof.getByRole('button',{name:'Confirm',exact:true}).click();
  const unknown=page.getByRole('dialog',{name:text('unknown'),exact:true});
  const changedProof=page.getByRole('dialog').getByText(stepUpProposal.content['stepUp.error.changed'],{exact:true});
  // Completed bulk authority may return shared options to another owner. Dismiss only that view, without cancelling the activated original.
  await expect(unknown.or(changedProof)).toBeVisible();if(await changedProof.isVisible())await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();
  await expect(unknown).toBeVisible();expect(lostSuccessfulProof).toBe(1);expect(lostResults).toBeGreaterThan(0);expect(proofWrites).toBe(1);
  expect(previews).toEqual([{},{}]);expect(activations).toEqual([{requestId:original.requestId}]);expect(cancellations).toEqual([{requestId:first.requestId}]);
  await page.unroute('**/authentication/step-up/password');await page.screenshot({path:info.outputPath('bulk-result-delivery-unknown.png'),fullPage:false});
  const readsBeforeReload=proofReads,resultsBeforeReload=resultBodies.length;
  const recovering=page.waitForResponse(value=>value.url().endsWith(root+'/context')&&value.request().method()==='GET');
  await page.reload();const recoveredResponse=await recovering;expect(recoveredResponse.status()).toBe(200);
  const recovered=await recoveredResponse.json() as FrozenOriginal;assertOriginal(recovered,original);expect(recovered.status).toBe('COMPLETED');expect(recovered.activated).toBe(true);
  const check=page.getByRole('button',{name:text('check'),exact:true});await expect(check).toBeVisible();
  expect(proofReads).toBe(readsBeforeReload);expect(proofWrites).toBe(1);expect(resultBodies).toHaveLength(resultsBeforeReload);
  expect(previews).toEqual([{},{}]);expect(activations).toEqual([{requestId:original.requestId}]);expect(cancellations).toEqual([{requestId:first.requestId}]);
  expect((await page.context().cookies()).find(value=>value.name==='__Host-Tunner.SessionBulkRevocation')?.expires).toBe(originalExpiry);
  loseResultDelivery=false;await check.click();await expect(page.getByRole('dialog',{name:text('completed'),exact:true})).toBeVisible();
  expect(receipts.length).toBeGreaterThan(0);expect(receipts.at(-1)).toEqual(receipts[0]);
  expect(resultBodies.every(value=>JSON.stringify(value)===JSON.stringify({requestId:original.requestId}))).toBe(true);
  expect(previews).toEqual([{},{}]);expect(activations).toEqual([{requestId:original.requestId}]);expect(proofWrites).toBe(1);
  const targetStatuses=await Promise.all(targets.map(async target=>(await target.page.request.get('/authentication/session')).status()));
  expect(targetStatuses).toEqual([401,401]);const actorStatus=(await page.request.get('/authentication/session')).status(),laterStatus=(await later.request.get('/authentication/session')).status();
  expect(actorStatus).toBe(200);expect(laterStatus).toBe(200);
  expect(await page.evaluate(()=>({pathname:location.pathname,query:location.search,fragment:location.hash,local:localStorage.length,session:sessionStorage.length,
   visibleOriginal:document.cookie.includes('__Host-Tunner.SessionBulkRevocation'),visibleProof:document.cookie.includes('__Host-Tunner.StepUp')}))).toEqual({pathname:'/account/sessions',query:'',fragment:'',local:0,session:0,visibleOriginal:false,visibleProof:false});
  await page.screenshot({path:info.outputPath('bulk-two-revoked-actor-and-later-preserved.png'),fullPage:false});
  await info.attach('original-bulk-causal-outcome',{contentType:'application/json',body:JSON.stringify({original:{requestId:original.requestId,operationReference:original.operationReference,
   preparedAt:original.preparedAt,expiresAt:original.expiresAt,targets:original.targets},receipt:receipts.at(-1),actor:{reference:actorReference,status:actorStatus},
   laterExcluded:{reference:laterReference,status:laterStatus},targetStatuses,previewCount:previews.length,activationCount:activations.length,cancellationCount:cancellations.length,proofWrites},null,2)});
  await page.unroute('**/account/sessions/bulk-revocation/result');
 }finally{await Promise.all(contexts.map(context=>context.close()));}
});
