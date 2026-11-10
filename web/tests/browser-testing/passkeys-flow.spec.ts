import {expect,test} from '@playwright/test';
import type {CDPSession,Page} from '@playwright/test';
import {registerVerifiedAccount} from './fixtures';

async function authenticator(cdp:CDPSession){
  return (await cdp.send('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',ctap2Version:'ctap2_1',transport:'internal',
    hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true,
    defaultBackupEligibility:false,defaultBackupState:false}})).authenticatorId;
}
async function activeCount(page:Page){
  const response=await page.request.get('/account/passkeys');expect(response.ok()).toBe(true);
  const methods=await response.json() as {credentials:Array<{state:string}>};
  return methods.credentials.filter(credential=>credential.state==='ACTIVE').length;
}
async function beginEnrollment(page:Page){
  await page.getByRole('button',{name:'Add passkey',exact:true}).click();
  const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
  await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();
  await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();
  const operationReference=(await(await options).json()).operationReference as string;
  expect(typeof operationReference).toBe('string');return operationReference;
}
async function nativeProof(page:Page,operationReference:string){
  const response=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/passkey')&&r.request().method()==='POST');
  await page.getByRole('dialog').getByRole('button',{name:'Passkey',exact:true}).click();
  const completed=await response;expect(completed.status()).toBe(200);
  const result=await completed.json() as {status:string;requestId:string;attemptId:string;ceremonyId:string;operationReference:string};
  const sent=completed.request().postDataJSON() as {requestId:string;attemptId:string;ceremonyId:string};
  expect(result.status).toBe('COMPLETED');
  expect({requestId:result.requestId,attemptId:result.attemptId,ceremonyId:result.ceremonyId})
    .toEqual({requestId:sent.requestId,attemptId:sent.attemptId,ceremonyId:sent.ceremonyId});
  expect(result.operationReference).toBe(operationReference);
}

test('real native passkeys complete approved onboarding, sign-in, proof, enrollment and self-removal',async({page,request},testInfo)=>{
  test.setTimeout(180000);
  const status=await request.get('/browser-testing/status');expect(status.ok()).toBe(true);
  expect((await status.json()).mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');
  const cdp=await page.context().newCDPSession(page);await cdp.send('WebAuthn.enable',{enableUI:false});
  const first=await authenticator(cdp);let created=0,asserted=0;
  // Never export private authenticator state or retain protocol event payloads.
  cdp.on('WebAuthn.credentialAdded',()=>created++);cdp.on('WebAuthn.credentialAsserted',()=>asserted++);
  const account=await registerVerifiedAccount(page,request);
  await page.getByRole('link',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel('Email address').fill(account.email);await page.getByLabel('Password',{exact:true}).fill(account.password);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);
  await page.getByRole('link',{name:'Account security',exact:true}).click();await expect(page).toHaveURL(/\/account\/security$/);
  await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await page.reload();
  await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  await expect.poll(()=>activeCount(page)).toBe(0);
  await beginEnrollment(page);
  const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:'Passkey',exact:true})).toBeDisabled();
  await dialog.getByRole('button',{name:'Other method',exact:true}).click();
  await dialog.getByLabel('Password',{exact:true}).fill(account.password);await dialog.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(dialog.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(created).toBe(0);
  expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  await page.reload();await page.getByRole('button',{name:'Check status',exact:true}).click();
  await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();await expect.poll(()=>activeCount(page)).toBe(1);expect(created).toBe(1);

  await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(asserted).toBe(1);
  await page.reload();await page.getByRole('link',{name:'Account security',exact:true}).click();
  await page.getByRole('link',{name:'Review passkeys',exact:true}).click();
  const enrollmentReference=await beginEnrollment(page);await nativeProof(page,enrollmentReference);
  await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  expect(created).toBe(1);expect(await activeCount(page)).toBe(1);
  await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:false});const second=await authenticator(cdp);
  await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  await expect.poll(()=>activeCount(page)).toBe(2);expect(created).toBe(2);
  await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:second,enabled:false});
  await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:true});

  // Both removals use the surviving first native authenticator, including its own removal.
  for(const remaining of [1,0]){
    const groups=page.getByRole('group',{name:'Passkey',exact:true});
    const target=remaining===1?groups.last():groups.filter({has:page.getByText('ACTIVE',{exact:true})});
    await target.getByRole('button',{name:'Manage',exact:true}).click();
    const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
    await page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click();
    const operationReference=(await(await options).json()).operationReference as string;
    expect(typeof operationReference).toBe('string');await nativeProof(page,operationReference);
    await expect.poll(()=>activeCount(page)).toBe(remaining);
    await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }
  expect(asserted).toBe(4);expect(created).toBe(2);
  await page.reload();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  await page.screenshot({path:testInfo.outputPath('real-native-passkeys-removed.png'),fullPage:true});
  expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash})))
    .toEqual({local:0,session:0,search:'',hash:''});
  await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();
  await page.goto('/account/security/sign-in-methods');await expect(page).toHaveURL(/\/login$/);
  await cdp.send('WebAuthn.disable');await cdp.detach();
});
