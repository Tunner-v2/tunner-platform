import {expect,test} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import type {DeviceItem,DeviceOptions,DeviceTrustRemovalContext,DeviceTrustRemovalResult} from '../../apps/user/devices.api';
import stepUpProposal from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import {registerVerifiedAccount,runtime} from './fixtures';

// This journey uses the already trusted localhost certificate and the real isolated BFF/SMTP fixtures.
test.use({ignoreHTTPSErrors:false});
async function signIn(page:Page,email:string,password:string){
 await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back',exact:true})).toBeVisible();
 await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill(password);
 const signingIn=page.waitForResponse(value=>value.url().endsWith('/authentication/sign-in')&&value.request().method()==='POST');
 await page.getByRole('button',{name:'Sign in',exact:true}).click();expect((await signingIn).status()).toBe(200);
 await expect(page).toHaveURL(/\/signed-in$/);await expect(page.getByRole('heading',{name:'You’re signed in',exact:true})).toBeVisible();
}

test('real Device navigation and exact trust removal survive response loss without signing out the current session',async({page,request,browser},info)=>{
 test.setTimeout(90000);
 const anonymous=await browser.newContext({baseURL:'https://localhost:'+runtime.ApplicationPort,ignoreHTTPSErrors:false});
 try{
  const guarded=await anonymous.newPage();await guarded.goto('/account/devices');await expect(guarded).toHaveURL(/\/login$/);
  await guarded.goto('/account/devices/11111111-1111-4111-8111-111111111111');await expect(guarded).toHaveURL(/\/login$/);
 }finally{await anonymous.close();}

 const credentials=await registerVerifiedAccount(page,request);await signIn(page,credentials.email,credentials.password);
 await page.setViewportSize({width:1440,height:1100});await page.getByRole('link',{name:'Account security',exact:true}).click();
 await expect(page).toHaveURL(/\/account\/security$/);await page.getByRole('link',{name:/^sessions\s*&\s*devices$/i}).click();
 await expect(page).toHaveURL(/\/account\/sessions$/);const devicesLink=page.getByRole('link',{name:'Review devices',exact:true});
 await expect(devicesLink).toHaveAttribute('href','/account/devices');await devicesLink.click();
 await expect(page).toHaveURL(/\/account\/devices$/);await expect(page.getByRole('heading',{name:'Devices',exact:true})).toBeFocused();
 const observed=page.locator('.devices-row').filter({hasText:'Observed with your current session'});await expect(observed).toHaveCount(1);
 const reference=await observed.getAttribute('data-device-reference');expect(reference).toMatch(/^[0-9a-f-]{36}$/);
 const detailsPath='/account/devices/'+reference;
 await expect(observed).toContainText('Chromium');await expect(page.getByText(/Karachi|Windows|Android|IP address/)).toHaveCount(0);
 expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath('devices-list-1440.png'),fullPage:true});
 await observed.getByRole('button',{name:'View details',exact:true}).click();await expect(page).toHaveURL(new RegExp(detailsPath+'$'));
 await expect(page.getByRole('heading',{name:'Device details',exact:true})).toBeFocused();await expect(page.getByText(reference!,{exact:true})).toBeVisible();
 await page.reload();await expect(page.getByRole('heading',{name:'Device details',exact:true})).toBeFocused();
 await page.getByRole('button',{name:'Back to devices',exact:true}).click();await expect(page).toHaveURL(/\/account\/devices$/);
 await page.goBack();await expect(page).toHaveURL(new RegExp(detailsPath+'$'));await expect(page.getByText(reference!,{exact:true})).toBeVisible();
 await page.goForward();await expect(page.getByRole('heading',{name:'Devices',exact:true})).toBeFocused();
 const optionRead=page.waitForResponse(value=>value.url().endsWith('/account/devices/options')&&value.request().method()==='GET');
 const detailRead=page.waitForResponse(value=>value.url().endsWith('/account/devices/details')&&value.request().method()==='POST');
 await page.locator('[data-device-reference="'+reference+'"]').getByRole('button',{name:'View details',exact:true}).click();
 const optionsResponse=await optionRead,detailsResponse=await detailRead;expect(optionsResponse.status()).toBe(200);expect(detailsResponse.status()).toBe(200);
 const options=await optionsResponse.json() as DeviceOptions,target=(await detailsResponse.json() as {device:DeviceItem}).device;
 expect(target.reference).toBe(reference);expect(target.observedForCurrentSession).toBe(true);expect(target.effectiveTrustState).toBe('TRUSTED');
 expect(typeof target.version).toBe('string');expect(typeof target.trustVersion).toBe('string');expect(target.characteristics).toEqual({BrowserFamily:'Chromium'});
 expect(Object.keys(options.content)).toHaveLength(78);

 const prepares:unknown[]=[],resultBodies:unknown[]=[];let proofWrites=0,proofReads=0,signOutWrites=0;
 page.on('request',value=>{
  if(value.url().endsWith('/authentication/step-up/options')&&value.method()==='GET')proofReads++;
  if(value.method()!=='POST')return;
  if(value.url().endsWith('/account/devices/prepare'))prepares.push(value.postDataJSON());
  if(value.url().endsWith('/account/devices/result'))resultBodies.push(value.postDataJSON());
  if(value.url().endsWith('/authentication/step-up/password'))proofWrites++;
  if(value.url().endsWith('/authentication/sign-out'))signOutWrites++;
 });
 await page.getByRole('button',{name:'Remove trust',exact:true}).click();
 let confirmation=page.getByRole('dialog',{name:'Remove trusted device?',exact:true});await expect(confirmation).toBeVisible();
 await expect(confirmation).toContainText('Removing trust does not sign it out.');expect(prepares).toEqual([]);expect(proofReads).toBe(0);
 await confirmation.getByRole('button',{name:'Keep trusted',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
 expect(prepares).toEqual([]);expect(proofReads).toBe(0);await expect(page.getByRole('button',{name:'Remove trust',exact:true})).toBeFocused();
 await page.getByRole('button',{name:'Remove trust',exact:true}).click();confirmation=page.getByRole('dialog',{name:'Remove trusted device?',exact:true});
 await page.setViewportSize({width:390,height:844});await expect(confirmation.getByRole('button',{name:'Remove trust',exact:true})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath('devices-confirm-390.png'),fullPage:false});
 await page.setViewportSize({width:1440,height:1100});
 const preparing=page.waitForResponse(value=>value.url().endsWith('/account/devices/prepare')&&value.request().method()==='POST');
 await confirmation.getByRole('button',{name:'Remove trust',exact:true}).click();const prepared=await preparing;expect(prepared.status()).toBe(200);
 const original=await prepared.json() as DeviceTrustRemovalContext;
 expect(prepares).toEqual([{requestId:options.requestId,reference,expectedVersion:target.version,expectedTrustVersion:target.trustVersion,confirmed:true}]);
 expect(original.requestId).toBe(options.requestId);expect(original.target).toEqual({reference,version:target.version,trustVersion:target.trustVersion});
 expect(original.sessionEffect).toBe('PRESERVE_EXISTING_SESSIONS');expect(original.status).toBe('PENDING');expect(original.operationReference.length).toBeGreaterThan(0);
 const protectedOriginal=(await page.context().cookies()).find(value=>value.name==='__Host-Tunner.DeviceOperation');
 expect(Boolean(protectedOriginal)).toBe(true);expect(protectedOriginal?.httpOnly).toBe(true);expect(protectedOriginal?.secure).toBe(true);expect(protectedOriginal?.sameSite).toBe('Strict');
 const originalExpiry=protectedOriginal!.expires;
 const proof=page.getByRole('dialog');await expect(proof.getByRole('button',{name:'Other method',exact:true})).toBeVisible();
 await proof.getByRole('button',{name:'Other method',exact:true}).click();const password=proof.getByLabel('Password',{exact:true});
 await password.fill('Deliberately wrong synthetic credential');await proof.getByRole('button',{name:'Confirm',exact:true}).click();
 await expect(password).toHaveAttribute('aria-invalid','true');await expect(password).toHaveValue('');await expect(password).toBeFocused();

 // Both interceptors forward to the real BFF first; they lose delivery, never fabricate an applied outcome.
 let lostSuccessfulProof=0,loseResultDelivery=true,lostResults=0;const receipts:DeviceTrustRemovalResult[]=[];
 await page.route('**/account/devices/result',async route=>{
  const actual=await route.fetch();expect(actual.status()).toBe(200);const found=await actual.json() as DeviceTrustRemovalResult;
  expect(found.requestId).toBe(original.requestId);expect(found.operationReference).toBe(original.operationReference);expect(found.status).toBe('REVOKED');
  expect(found.sessionEffect).toBe('PRESERVE_EXISTING_SESSIONS');expect(found.target.reference).toBe(reference);expect(found.target.version).toBe(original.target.version);
  expect(found.target.priorTrustVersion).toBe(original.target.trustVersion);expect(BigInt(found.target.trustVersion)).toBe(BigInt(original.target.trustVersion)+1n);
  expect(Number.isFinite(Date.parse(found.revokedAt))).toBe(true);receipts.push(found);
  if(loseResultDelivery){lostResults++;await route.abort();}else await route.fulfill({response:actual});
 });
 await page.route('**/authentication/step-up/password',async route=>{
  const actual=await route.fetch();expect(actual.status()).toBe(200);const completed=await actual.json() as {status:string;operationReference:string};
  expect(completed.status).toBe('COMPLETED');expect(completed.operationReference).toBe(original.operationReference);lostSuccessfulProof++;await route.abort();
 });
 await password.fill(credentials.password);await proof.getByRole('button',{name:'Confirm',exact:true}).click();
 const unknown=page.getByRole('dialog',{name:'Trust removal is not confirmed',exact:true});
 const changedProof=page.getByRole('dialog').getByText(stepUpProposal.content['stepUp.error.changed'],{exact:true});
 // A completed Device owner may hand shared options back to the independent owner. Dismiss that view only.
 await expect(unknown.or(changedProof)).toBeVisible();if(await changedProof.isVisible())await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();
 await expect(unknown).toBeVisible();
 expect(lostSuccessfulProof).toBe(1);expect(lostResults).toBeGreaterThan(0);expect(prepares).toHaveLength(1);expect(proofWrites).toBe(2);
 await page.screenshot({path:info.outputPath('devices-unknown-1440.png'),fullPage:false});
 await page.unroute('**/authentication/step-up/password');const readsBeforeReload=proofReads,resultsBeforeReload=resultBodies.length;
 await page.reload();await expect(page.getByRole('heading',{name:'Device details',exact:true})).toBeFocused();
 const recovery=page.locator('.devices-recovery');await expect(recovery.getByRole('button',{name:'Check this request',exact:true})).toBeVisible();
 expect(prepares).toHaveLength(1);expect(proofWrites).toBe(2);expect(proofReads).toBe(readsBeforeReload);expect(resultBodies).toHaveLength(resultsBeforeReload);
 expect((await page.context().cookies()).find(value=>value.name==='__Host-Tunner.DeviceOperation')?.expires).toBe(originalExpiry);
 loseResultDelivery=false;await recovery.getByRole('button',{name:'Check this request',exact:true}).click();
 await expect(page.getByRole('dialog',{name:'Trust removed',exact:true})).toBeVisible();expect(receipts.length).toBeGreaterThan(0);
 expect(resultBodies.every(value=>JSON.stringify(value)===JSON.stringify({requestId:original.requestId}))).toBe(true);
 expect(prepares).toHaveLength(1);expect(proofWrites).toBe(2);expect(signOutWrites).toBe(0);
 expect((await page.request.get('/authentication/session')).status()).toBe(200);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath('devices-result-390.png'),fullPage:false});
 await page.getByRole('dialog').getByRole('button',{name:'Back to devices',exact:true}).click();await expect(page).toHaveURL(/\/account\/devices$/);
 await expect(page.getByRole('heading',{name:'Devices',exact:true})).toBeFocused();const revoked=page.locator('[data-device-reference="'+reference+'"]');
 await expect(revoked).toContainText('Trust removed');await expect(revoked.getByRole('button',{name:'Remove trust',exact:true})).toHaveCount(0);
 expect((await page.request.get('/authentication/session')).status()).toBe(200);expect(signOutWrites).toBe(0);
 expect(await page.evaluate(()=>({query:location.search,fragment:location.hash,local:localStorage.length,session:sessionStorage.length,visibleOriginal:document.cookie.includes('__Host-Tunner.DeviceOperation'),visibleInstallation:document.cookie.includes('__Host-Tunner.DeviceInstallation')}))).toEqual({query:'',fragment:'',local:0,session:0,visibleOriginal:false,visibleInstallation:false});
 await page.unroute('**/account/devices/result');
});
