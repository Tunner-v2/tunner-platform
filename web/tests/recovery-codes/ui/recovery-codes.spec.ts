import {test,expect} from '@playwright/test';
import {setup} from '../../passkeys/fixtures';
import {beginRecovery,completeRecoveryProof,publicCodes,recoveryFixture} from './recovery-codes.fixtures';

// Actual React/User route with PUBLIC mocked BFF/native values. Never native owner/file acceptance.
test.use({baseURL:'http://127.0.0.1:5241'});

for(const kind of ['GENERATE','REPLACE'] as const)for(const method of ['password','passkey','totp'] as const){
 test(kind+' captures only its winning '+method+' response before safe completion',async({page})=>{
  const {state}=await recoveryFixture(page);await beginRecovery(page,kind);expect(state.starts).toHaveLength(1);expect(state.starts[0]!.kind).toBe(kind);
  await completeRecoveryProof(page,method);await expect(page.getByRole('dialog').getByRole('heading',{name:'Backup codes',exact:true})).toBeVisible();await expect(page.locator('.recovery-codes-grid code')).toHaveText(publicCodes);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);
  await page.getByRole('button',{name:'Done',exact:true}).click();await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);await expect(page).toHaveURL('http://127.0.0.1:5241/account/security');expect(state.starts).toHaveLength(1);
 });
}

for(const method of ['passkey','totp'] as const){
 test(method+' original active-proof continuation is the first winning delivery after owner failure',async({page})=>{
  const {state}=await recoveryFixture(page,{ownerFailure:method});await beginRecovery(page,'GENERATE');await completeRecoveryProof(page,method);await page.getByRole('dialog').getByRole('button',{name:'Continue this verification',exact:true}).click();
  await expect(page.locator('.recovery-codes-grid code')).toHaveText(publicCodes);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);expect(method==='passkey'?state.passkeys:state.totps).toBe(1);expect(method==='passkey'?state.passkeyContinues:state.totpContinues).toBe(1);
 });
}

test('lost Start reads its exact old request despite a new options request ID',async({page})=>{
 const {state}=await recoveryFixture(page,{loseStart:true});await page.goto('/account/security');await page.getByRole('button',{name:'Generate codes',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('We couldn’t confirm the result');await page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('Confirm it’s you');expect(state.starts).toHaveLength(1);expect(state.startReads).toBe(1);expect(state.commits).toBe(0);
});

test('committed response body loss stays neutral and never fetches or regenerates codes',async({page})=>{
 const {state}=await recoveryFixture(page,{loseBody:true});await beginRecovery(page,'GENERATE');await completeRecoveryProof(page,'password');await expect(page.getByRole('heading',{name:'These codes are unavailable',exact:true})).toBeVisible();await expect(page.getByRole('dialog')).not.toContainText('already shown');await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);expect(state.passwords).toBe(1);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);expect(state.resultReads).toBeGreaterThan(0);
});

test('wrong owner delivery binding is discarded and safe status cannot restore it',async({page})=>{
 const {state}=await recoveryFixture(page,{wrongDelivery:true});await beginRecovery(page,'GENERATE');await completeRecoveryProof(page,'password');await expect(page.getByRole('heading',{name:'These codes are unavailable',exact:true})).toBeVisible();await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);
});

test('missing current impact publication blocks proof without replaying Start',async({page})=>{
 const {state}=await recoveryFixture(page,{missingImpact:true});await page.goto('/account/security');await page.getByRole('button',{name:'Replace codes',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('We couldn’t confirm the result');expect(state.starts).toHaveLength(1);expect(state.passwords+state.passkeys+state.totps).toBe(0);await expect(page.getByRole('button',{name:'Continue',exact:true})).toHaveCount(0);
 state.missingImpact=false;await page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('PUBLIC OWNER IMPACT');await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeEnabled();expect(state.starts).toHaveLength(1);
});

test('publication withdrawal after replacement review blocks proof without changing the held original',async({page})=>{
 const {state}=await recoveryFixture(page);await page.goto('/account/security');await page.getByRole('button',{name:'Replace codes',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('PUBLIC OWNER IMPACT');const original=state.operation!.operationReference;state.publicationUnavailable=true;await page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await expect(page.getByRole('dialog')).not.toContainText('PUBLIC OWNER IMPACT');expect(state.proofOptionReads).toBe(0);expect(state.commits).toBe(0);expect(state.starts).toHaveLength(1);expect(state.operation!.operationReference).toBe(original);
});

test('a changed shared proof operation never becomes an accepted owner verification',async({page})=>{
 const {state}=await recoveryFixture(page,{wrongProof:true});await page.goto('/account/security');await page.getByRole('button',{name:'Generate codes',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('We couldn’t confirm the result');await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);expect(state.commits).toBe(0);expect(state.passwords+state.passkeys+state.totps).toBe(0);expect(state.starts).toHaveLength(1);
});

test('cancel refreshes current same-path action facts before sibling actions reenable',async({page})=>{
 const {state}=await recoveryFixture(page);await page.goto('/account/security');await page.getByRole('button',{name:'Replace codes',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('PUBLIC OWNER IMPACT');state.canGenerate=false;state.canReplace=false;await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('button',{name:'Generate codes',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Replace codes',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeEnabled();expect(state.optionReads).toBeGreaterThanOrEqual(3);expect(state.cancels).toBe(1);expect(state.starts).toHaveLength(1);
});

test('pagehide clears the volatile view without unmount and persisted pageshow cannot reveal it',async({page})=>{
 const {state}=await recoveryFixture(page);await beginRecovery(page,'GENERATE');await completeRecoveryProof(page,'password');await expect(page.locator('.recovery-codes-grid code')).toHaveText(publicCodes);
 await page.evaluate(()=>window.dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true})));await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);await page.evaluate(()=>window.dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true})));await expect(page.locator('.recovery-codes-card')).toBeVisible();await page.locator('.recovery-codes-card').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('heading',{name:'These codes are unavailable',exact:true})).toBeVisible();await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);
});

test('reload and route history retain safe status rather than the previous display',async({page})=>{
 const {state}=await recoveryFixture(page);await page.goto('/account/security');await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await expect(page).toHaveURL(/\/account\/security\/sign-in-methods$/);await page.goBack();await page.getByRole('button',{name:'Generate codes',exact:true}).click();await completeRecoveryProof(page,'password');await expect(page.locator('.recovery-codes-grid code')).toHaveText(publicCodes);await page.reload();await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);await page.locator('.recovery-codes-card').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('heading',{name:'These codes are unavailable',exact:true})).toBeVisible();expect(state.starts).toHaveLength(1);expect(state.commits).toBe(1);
});

for(const explicit of [false,true])test('disabled-host '+(explicit?'explicit false':'absent')+' capability preserves the old page and makes zero recovery calls',async({page})=>{
 await setup(page,explicit?{recoveryCodesAvailable:false}:{});let reads=0;await page.route('**/account/recovery-codes/**',route=>{reads++;return route.fulfill({status:404,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}});});await page.goto('/account/security');await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeVisible();await page.waitForLoadState('networkidle');await expect(page.locator('.recovery-codes-card')).toHaveCount(0);expect(reads).toBe(0);
});

test('one explicit public transport request uses only the held payload and revokes its opaque URL on departure',async({page})=>{
 await page.addInitScript(()=>{const scope=window as unknown as {publicDownload:{creates:number;clicks:number;revokes:number;filename:string;url:string;blob:Blob|null}};scope.publicDownload={creates:0,clicks:0,revokes:0,filename:'',url:'',blob:null};URL.createObjectURL=blob=>{scope.publicDownload.creates++;scope.publicDownload.blob=blob as Blob;return 'blob:'+location.origin+'/public-nonfunctional-capability';};URL.revokeObjectURL=()=>{scope.publicDownload.revokes++;};HTMLAnchorElement.prototype.click=function(this:HTMLAnchorElement){scope.publicDownload.clicks++;scope.publicDownload.filename=this.download;scope.publicDownload.url=this.href;};});
 const {state}=await recoveryFixture(page);await beginRecovery(page,'GENERATE');await completeRecoveryProof(page,'password');await page.getByRole('button',{name:'Download',exact:true}).click();await expect(page.getByRole('status')).toContainText('Tunner cannot confirm that the file was saved.');await page.getByRole('button',{name:'Download',exact:true}).dispatchEvent('click');
 const request=await page.evaluate(async()=>{const r=(window as unknown as {publicDownload:{creates:number;clicks:number;filename:string;url:string;blob:Blob}}).publicDownload;return{creates:r.creates,clicks:r.clicks,filename:r.filename,opaque:r.url.startsWith('blob:'),text:await r.blob.text()};});expect(request).toEqual({creates:1,clicks:1,filename:'tunner-backup-codes.txt',opaque:true,text:publicCodes.join('\n')+'\n'});await page.evaluate(()=>window.dispatchEvent(new Event('blur')));await expect(page.locator('.recovery-codes-grid code')).toHaveText(publicCodes);
 await page.evaluate(()=>window.dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true})));await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);expect(await page.evaluate(()=>(window as unknown as {publicDownload:{revokes:number}}).publicDownload.revokes)).toBe(1);expect(state.commits).toBe(1);expect(state.starts).toHaveLength(1);
});
