# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: passkeys-flow.spec.ts >> real native passkeys complete approved onboarding, sign-in, proof, enrollment and self-removal
- Location: tests\browser-testing\passkeys-flow.spec.ts:36:1

# Error details

```
Test timeout of 180000ms exceeded.
```

```
Error: page.waitForResponse: Test timeout of 180000ms exceeded.
```

# Page snapshot

```yaml
- generic [ref=f7e3]:
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
                - paragraph [ref=f7e52]:
                  - text: Removed
                  - time [ref=f7e53]: Nov 10, 2026
              - generic [ref=f7e54]: REMOVED
            - group "Passkey" [ref=f7e55]:
              - generic [ref=f7e56]:
                - paragraph [ref=f7e57]: Passkey
                - paragraph [ref=f7e58]:
                  - text: Added
                  - time [ref=f7e59]: Nov 10, 2026
                - paragraph [ref=f7e60]: "Last used: Not used yet"
              - generic [ref=f7e61]: ACTIVE
              - button "Manage" [disabled] [ref=f7e62]
            - group "Password" [ref=f7e64]:
              - generic [ref=f7e65]:
                - paragraph [ref=f7e66]: Password
                - paragraph [ref=f7e67]: Available as a sign-in method
            - paragraph [ref=f7e68]: Other sign-in methods are not shown here.
            - button "Add passkey" [disabled] [ref=f7e70]
          - complementary [ref=f7e72]:
            - heading "About passkeys" [level=2] [ref=f7e73]
            - paragraph [ref=f7e74]: Passkeys let you sign in using your device lock, fingerprint, or face recognition.
            - generic [ref=f7e75]:
              - strong [ref=f7e76]: You stay in control
              - paragraph [ref=f7e77]: Your browser will ask you to confirm with device security.
            - paragraph [ref=f7e78]: Your device or password manager handles the confirmation.
        - generic [ref=f7e79]:
          - button "Check status" [ref=f7e80] [cursor=pointer]
          - button "Continue setup" [ref=f7e82] [cursor=pointer]
  - dialog [ref=f7e84]:
    - generic [ref=f7e85]:
      - heading "Confirm it’s you" [active] [level=1] [ref=f7e86]
      - paragraph [ref=f7e87]: For your security, verify your identity before continuing.
      - generic [ref=f7e88]:
        - status [ref=f7e89]: Verification was not completed. You can try again or choose another available method.
        - generic [ref=f7e90]:
          - button "Back" [ref=f7e91] [cursor=pointer]
          - button "Cancel" [ref=f7e93] [cursor=pointer]
```

# Test source

```ts
  1  | import {expect,test} from '@playwright/test';
  2  | import type {CDPSession,Page} from '@playwright/test';
  3  | import {registerVerifiedAccount} from './fixtures';
  4  |
  5  | async function authenticator(cdp:CDPSession){
  6  |   // Chromium permits multiple virtual roaming authenticators, but only one internal authenticator.
  7  |   return (await cdp.send('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',ctap2Version:'ctap2_1',transport:'usb',
  8  |     hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true,
  9  |     defaultBackupEligibility:false,defaultBackupState:false}})).authenticatorId;
  10 | }
  11 | async function activeCount(page:Page){
  12 |   const response=await page.request.get('/account/passkeys');expect(response.ok()).toBe(true);
  13 |   const methods=await response.json() as {credentials:Array<{state:string}>};
  14 |   return methods.credentials.filter(credential=>credential.state==='ACTIVE').length;
  15 | }
  16 | async function beginEnrollment(page:Page){
  17 |   await page.getByRole('button',{name:'Add passkey',exact:true}).click();
  18 |   const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
  19 |   await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();
  20 |   await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();
  21 |   const operationReference=(await(await options).json()).operationReference as string;
  22 |   expect(typeof operationReference).toBe('string');return operationReference;
  23 | }
  24 | async function nativeProof(page:Page,operationReference:string){
> 25 |   const response=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/passkey')&&r.request().method()==='POST');
     |                       ^ Error: page.waitForResponse: Test timeout of 180000ms exceeded.
  26 |   await page.getByRole('dialog').getByRole('button',{name:'Passkey',exact:true}).click();
  27 |   const completed=await response;expect(completed.status()).toBe(200);
  28 |   const result=await completed.json() as {status:string;requestId:string;attemptId:string;ceremonyId:string;operationReference:string};
  29 |   const sent=completed.request().postDataJSON() as {requestId:string;attemptId:string;ceremonyId:string};
  30 |   expect(result.status).toBe('COMPLETED');
  31 |   expect({requestId:result.requestId,attemptId:result.attemptId,ceremonyId:result.ceremonyId})
  32 |     .toEqual({requestId:sent.requestId,attemptId:sent.attemptId,ceremonyId:sent.ceremonyId});
  33 |   expect(result.operationReference).toBe(operationReference);
  34 | }
  35 |
  36 | test('real native passkeys complete approved onboarding, sign-in, proof, enrollment and self-removal',async({page,request},testInfo)=>{
  37 |   test.setTimeout(180000);
  38 |   const status=await request.get('/browser-testing/status');expect(status.ok()).toBe(true);
  39 |   expect((await status.json()).mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');
  40 |   const cdp=await page.context().newCDPSession(page);await cdp.send('WebAuthn.enable',{enableUI:false});
  41 |   const first=await authenticator(cdp);let created=0,asserted=0;
  42 |   // Never export private authenticator state or retain protocol event payloads.
  43 |   cdp.on('WebAuthn.credentialAdded',()=>created++);cdp.on('WebAuthn.credentialAsserted',()=>asserted++);
  44 |   const account=await registerVerifiedAccount(page,request);
  45 |   await page.getByRole('link',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  46 |   await page.getByLabel('Email address').fill(account.email);await page.getByLabel('Password',{exact:true}).fill(account.password);
  47 |   await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);
  48 |   await page.getByRole('link',{name:'Account security',exact:true}).click();await expect(page).toHaveURL(/\/account\/security$/);
  49 |   await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await page.reload();
  50 |   await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  51 |   await expect.poll(()=>activeCount(page)).toBe(0);
  52 |   await beginEnrollment(page);
  53 |   const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:'Passkey',exact:true})).toBeDisabled();
  54 |   await dialog.getByRole('button',{name:'Other method',exact:true}).click();
  55 |   await dialog.getByLabel('Password',{exact:true}).fill(account.password);await dialog.getByRole('button',{name:'Confirm',exact:true}).click();
  56 |   await expect(dialog.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(created).toBe(0);
  57 |   expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  58 |   await page.reload();await page.getByRole('button',{name:'Check status',exact:true}).click();
  59 |   await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  60 |   expect(await activeCount(page)).toBe(0);await expect(page.getByText('Changes saved successfully',{exact:true})).toHaveCount(0);
  61 |   await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  62 |   await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();await expect.poll(()=>activeCount(page)).toBe(1);expect(created).toBe(1);
  63 |
  64 |   await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
  65 |   await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(asserted).toBe(1);
  66 |   await page.reload();await page.getByRole('link',{name:'Account security',exact:true}).click();
  67 |   await page.getByRole('link',{name:'Review passkeys',exact:true}).click();
  68 |   const enrollmentReference=await beginEnrollment(page);await nativeProof(page,enrollmentReference);
  69 |   await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();
  70 |   expect(created).toBe(1);expect(await activeCount(page)).toBe(1);
  71 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:false});const second=await authenticator(cdp);
  72 |   await page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click();
  73 |   await expect.poll(()=>activeCount(page)).toBe(2);expect(created).toBe(2);
  74 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:second,enabled:false});
  75 |   await cdp.send('WebAuthn.setAutomaticPresenceSimulation',{authenticatorId:first,enabled:true});
  76 |
  77 |   // Both removals use the surviving first native authenticator, including its own removal.
  78 |   for(const remaining of [1,0]){
  79 |     const groups=page.getByRole('group',{name:'Passkey',exact:true});
  80 |     const target=remaining===1?groups.last():groups.filter({has:page.getByText('ACTIVE',{exact:true})});
  81 |     await target.getByRole('button',{name:'Manage',exact:true}).click();
  82 |     const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));
  83 |     await page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click();
  84 |     const operationReference=(await(await options).json()).operationReference as string;
  85 |     expect(typeof operationReference).toBe('string');await nativeProof(page,operationReference);
  86 |     await expect.poll(()=>activeCount(page)).toBe(remaining);
  87 |     await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();
  88 |     await expect(page.getByRole('dialog')).toHaveCount(0);
  89 |   }
  90 |   expect(asserted).toBe(4);expect(created).toBe(2);
  91 |   await page.reload();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  92 |   await page.screenshot({path:testInfo.outputPath('real-native-passkeys-removed.png'),fullPage:true});
  93 |   expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash})))
  94 |     .toEqual({local:0,session:0,search:'',hash:''});
  95 |   await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();
  96 |   await page.goto('/account/security/sign-in-methods');await expect(page).toHaveURL(/\/login$/);
  97 |   await cdp.send('WebAuthn.disable');await cdp.detach();
  98 | });
  99 |
```
