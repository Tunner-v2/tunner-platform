# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: passkeys-flow.spec.ts >> real native passkeys complete approved onboarding, sign-in, proof, enrollment and self-removal
- Location: tests\browser-testing\passkeys-flow.spec.ts:35:1

# Error details

```
Error: cdpSession.send: Protocol error (WebAuthn.addVirtualAuthenticator): Chrome only supports one internal authenticator per environment
```

# Page snapshot

```yaml
- generic [ref=f7e4]:
  - banner [ref=f7e5]:
    - generic [ref=f7e6]: Tunner
    - generic [ref=f7e7]: ACCOUNT CENTER
    - generic [ref=f7e8]: Search account settings
    - img "Notifications" [ref=f7e9]: •
    - generic [ref=f7e10]: Your account
  - generic [ref=f7e11]:
    - navigation "Account navigation" [ref=f7e12]:
      - paragraph [ref=f7e13]: ACCOUNT
      - generic [ref=f7e14]: Overview
      - generic [ref=f7e16]: Profile & identity
      - link "Security" [ref=f7e18] [cursor=pointer]:
        - /url: /account/security
      - generic [ref=f7e20]: Sessions & devices
      - generic [ref=f7e22]: Billing & payments
      - generic [ref=f7e24]: Notifications
      - generic [ref=f7e26]: Products
      - generic [ref=f7e28]: Account activity
      - generic [ref=f7e30]:
        - paragraph [ref=f7e31]: NEED HELP?
        - paragraph [ref=f7e32]: Account, billing, or security issue?
    - main [ref=f7e33]:
      - paragraph [ref=f7e34]: SECURITY
      - heading "Passkeys & two-step verification" [level=1] [ref=f7e35]
      - paragraph [ref=f7e36]: Choose secure ways to confirm it’s you when you sign in or make sensitive account changes.
      - generic [ref=f7e37]:
        - paragraph [ref=f7e38]: YOUR SIGN-IN METHODS
        - heading "Your passkeys are ready to use" [level=2] [ref=f7e39]
        - paragraph [ref=f7e40]: You can use a passkey to sign in. Review your passkeys below.
      - generic [ref=f7e41]:
        - generic [ref=f7e42]:
          - heading "Your verification methods" [level=2] [ref=f7e43]
          - paragraph [ref=f7e44]: Review the passkeys you can use to sign in and verify it is you.
          - group "Passkey" [ref=f7e45]:
            - generic [ref=f7e46]:
              - paragraph [ref=f7e47]: Passkey
              - paragraph [ref=f7e48]:
                - text: Added
                - time [ref=f7e49]: Nov 10, 2026
              - paragraph [ref=f7e50]:
                - text: "Last used:"
                - time [ref=f7e51]: Nov 10, 2026
            - generic [ref=f7e52]: ACTIVE
            - button "Manage" [disabled] [ref=f7e53]
          - group "Password" [ref=f7e55]:
            - generic [ref=f7e56]:
              - paragraph [ref=f7e57]: Password
              - paragraph [ref=f7e58]: Available as a sign-in method
          - paragraph [ref=f7e59]: Other sign-in methods are not shown here.
          - button "Add passkey" [disabled] [ref=f7e61]
        - complementary [ref=f7e63]:
          - heading "About passkeys" [level=2] [ref=f7e64]
          - paragraph [ref=f7e65]: Passkeys let you sign in using your device lock, fingerprint, or face recognition.
          - generic [ref=f7e66]:
            - strong [ref=f7e67]: You stay in control
            - paragraph [ref=f7e68]: Your browser will ask you to confirm with device security.
          - paragraph [ref=f7e69]: Your device or password manager handles the confirmation.
      - status [ref=f7e70]: A passkey request is in progress. Check its status before trying again.
      - button "Check status" [ref=f7e72] [cursor=pointer]
  - dialog [ref=f7e74]:
    - generic [ref=f7e75]:
      - heading "Add a passkey" [active] [level=2] [ref=f7e76]
      - paragraph [ref=f7e77]: Passkeys let you sign in using your device lock, fingerprint, or face recognition.
      - generic [ref=f7e78]:
        - heading "Your browser" [level=3] [ref=f7e79]
        - paragraph [ref=f7e80]: Current browser
        - paragraph [ref=f7e81]: Your browser will ask you to confirm with device security.
      - generic [ref=f7e82]:
        - button "Not now" [ref=f7e83] [cursor=pointer]
        - button "Continue setup" [ref=f7e85] [cursor=pointer]
```

# Test source

```ts
  1  | import {expect,test} from '@playwright/test';
  2  | import type {CDPSession,Page} from '@playwright/test';
  3  | import {registerVerifiedAccount} from './fixtures';
  4  |
  5  | async function authenticator(cdp:CDPSession){
> 6  |   return (await cdp.send('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',ctap2Version:'ctap2_1',transport:'internal',
     |                     ^ Error: cdpSession.send: Protocol error (WebAuthn.addVirtualAuthenticator): Chrome only supports one internal authenticator per environment
  7  |     hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true,
  8  |     defaultBackupEligibility:false,defaultBackupState:false}})).authenticatorId;
  9  | }
  10 | async function activeCount(page:Page){
  11 |   const response=await page.request.get('/account/passkeys');expect(response.ok()).toBe(true);
  12 |   const methods=await response.json() as {credentials:Array<{state:string}>};
  13 |   return methods.credentials.filter(credential=>credential.state==='ACTIVE').length;
  14 | }
  15 | async function beginEnrollment(page:Page){
  16 |   await page.getByRole('button',{name:'Add passkey',exact:true}).click();
  17 |   const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
  18 |   await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();
  19 |   await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();
  20 |   const operationReference=(await(await options).json()).operationReference as string;
  21 |   expect(typeof operationReference).toBe('string');return operationReference;
  22 | }
  23 | async function nativeProof(page:Page,operationReference:string){
  24 |   const response=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/passkey')&&r.request().method()==='POST');
  25 |   await page.getByRole('dialog').getByRole('button',{name:'Passkey',exact:true}).click();
  26 |   const completed=await response;expect(completed.status()).toBe(200);
  27 |   const result=await completed.json() as {status:string;requestId:string;attemptId:string;ceremonyId:string;operationReference:string};
  28 |   const sent=completed.request().postDataJSON() as {requestId:string;attemptId:string;ceremonyId:string};
  29 |   expect(result.status).toBe('COMPLETED');
  30 |   expect({requestId:result.requestId,attemptId:result.attemptId,ceremonyId:result.ceremonyId})
  31 |     .toEqual({requestId:sent.requestId,attemptId:sent.attemptId,ceremonyId:sent.ceremonyId});
  32 |   expect(result.operationReference).toBe(operationReference);
  33 | }
  34 |
  35 | test('real native passkeys complete approved onboarding, sign-in, proof, enrollment and self-removal',async({page,request},testInfo)=>{
  36 |   test.setTimeout(180000);
  37 |   const status=await request.get('/browser-testing/status');expect(status.ok()).toBe(true);
  38 |   expect((await status.json()).mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');
  39 |   const cdp=await page.context().newCDPSession(page);await cdp.send('WebAuthn.enable',{enableUI:false});
  40 |   const first=await authenticator(cdp);let created=0,asserted=0;
  41 |   // Never export private authenticator state or retain protocol event payloads.
  42 |   cdp.on('WebAuthn.credentialAdded',()=>created++);cdp.on('WebAuthn.credentialAsserted',()=>asserted++);
  43 |   const account=await registerVerifiedAccount(page,request);
  44 |   await page.getByRole('link',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  45 |   await page.getByLabel('Email address').fill(account.email);await page.getByLabel('Password',{exact:true}).fill(account.password);
  46 |   await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);
  47 |   await page.getByRole('link',{name:'Account security',exact:true}).click();await expect(page).toHaveURL(/\/account\/security$/);
  48 |   await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await page.reload();
  49 |   await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  50 |   await expect.poll(()=>activeCount(page)).toBe(0);
  51 |   await beginEnrollment(page);
  52 |   const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:'Passkey',exact:true})).toBeDisabled();
  53 |   await dialog.getByRole('button',{name:'Other method',exact:true}).click();
  54 |   await dialog.getByLabel('Password',{exact:true}).fill(account.password);await dialog.getByRole('button',{name:'Confirm',exact:true}).click();
  55 |   await expect(dialog.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(created).toBe(0);
  56 |   expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  57 |   await page.reload();await page.getByRole('button',{name:'Check status',exact:true}).click();
  58 |   await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  59 |   expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  60 |   await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  61 |   await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();await expect.poll(()=>activeCount(page)).toBe(1);expect(created).toBe(1);
  62 |
  63 |   await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  64 |   await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(asserted).toBe(1);
  65 |   await page.reload();await page.getByRole('link',{name:'Account security',exact:true}).click();
  66 |   await page.getByRole('link',{name:'Review passkeys',exact:true}).click();
  67 |   const enrollmentReference=await beginEnrollment(page);await nativeProof(page,enrollmentReference);
  68 |   await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  69 |   expect(created).toBe(1);expect(await activeCount(page)).toBe(1);
  70 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:false});const second=await authenticator(cdp);
  71 |   await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  72 |   await expect.poll(()=>activeCount(page)).toBe(2);expect(created).toBe(2);
  73 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:second,enabled:false});
  74 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:true});
  75 |
  76 |   // Both removals use the surviving first native authenticator, including its own removal.
  77 |   for(const remaining of [1,0]){
  78 |     const groups=page.getByRole('group',{name:'Passkey',exact:true});
  79 |     const target=remaining===1?groups.last():groups.filter({has:page.getByText('ACTIVE',{exact:true})});
  80 |     await target.getByRole('button',{name:'Manage',exact:true}).click();
  81 |     const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
  82 |     await page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click();
  83 |     const operationReference=(await(await options).json()).operationReference as string;
  84 |     expect(typeof operationReference).toBe('string');await nativeProof(page,operationReference);
  85 |     await expect.poll(()=>activeCount(page)).toBe(remaining);
  86 |     await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();
  87 |     await expect(page.getByRole('dialog')).toHaveCount(0);
  88 |   }
  89 |   expect(asserted).toBe(4);expect(created).toBe(2);
  90 |   await page.reload();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  91 |   await page.screenshot({path:testInfo.outputPath('real-native-passkeys-removed.png'),fullPage:true});
  92 |   expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash})))
  93 |     .toEqual({local:0,session:0,search:'',hash:''});
  94 |   await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();
  95 |   await page.goto('/account/security/sign-in-methods');await expect(page).toHaveURL(/\/login$/);
  96 |   await cdp.send('WebAuthn.disable');await cdp.detach();
  97 | });
  98 |
```
