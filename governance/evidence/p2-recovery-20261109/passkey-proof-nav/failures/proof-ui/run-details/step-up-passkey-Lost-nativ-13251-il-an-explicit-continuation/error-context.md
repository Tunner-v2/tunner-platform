# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: step-up-passkey.spec.ts >> Lost native options retain the original proof request and attempt until an explicit continuation
- Location: tests\passkeys\step-up-passkey.spec.ts:21:1

# Error details

```
Error: expect(received).toHaveLength(expected)

Expected length: 2
Received length: 1
Received array:  [{"attemptId": "e6a594ba-d3e9-4ea1-b497-506531ea2dc1", "requestId": "a8331149-9465-4a45-a7ac-df97895aa572"}]
```

# Page snapshot

```yaml
- generic [ref=e3]:
  - generic [ref=e4]:
    - banner [ref=e5]:
      - generic [ref=e6]: Tunner
      - generic [ref=e7]: ACCOUNT CENTER
      - generic [ref=e8]: Search account settings
      - img "Notifications" [ref=e9]: •
      - generic [ref=e10]: Your account
    - generic [ref=e11]:
      - navigation "Account navigation" [ref=e12]:
        - paragraph [ref=e13]: ACCOUNT
        - generic [ref=e14]: Overview
        - generic [ref=e16]: Profile & identity
        - link "Security" [ref=e18] [cursor=pointer]:
          - /url: /account/security
        - generic [ref=e20]: Sessions & devices
        - generic [ref=e22]: Billing & payments
        - generic [ref=e24]: Notifications
        - generic [ref=e26]: Products
        - generic [ref=e28]: Account activity
        - generic [ref=e30]:
          - paragraph [ref=e31]: NEED HELP?
          - paragraph [ref=e32]: Account, billing, or security issue?
      - main [ref=e33]:
        - paragraph [ref=e34]: SECURITY
        - heading "Passkeys & two-step verification" [level=1] [ref=e35]
        - paragraph [ref=e36]: Choose secure ways to confirm it’s you when you sign in or make sensitive account changes.
        - generic [ref=e37]:
          - paragraph [ref=e38]: YOUR SIGN-IN METHODS
          - heading "Your passkeys are ready to use" [level=2] [ref=e39]
          - paragraph [ref=e40]: You can use a passkey to sign in. Review your passkeys below.
        - generic [ref=e41]:
          - generic [ref=e42]:
            - heading "Your verification methods" [level=2] [ref=e43]
            - paragraph [ref=e44]: Review the passkeys you can use to sign in and verify it is you.
            - group "Passkey" [ref=e45]:
              - generic [ref=e46]:
                - paragraph [ref=e47]: Passkey
                - paragraph [ref=e48]:
                  - text: Added
                  - time [ref=e49]: Nov 09, 2026
                - paragraph [ref=e50]: "Last used: Not used yet"
              - generic [ref=e51]: ACTIVE
              - button "Manage" [disabled] [ref=e52]
            - group "Password" [ref=e54]:
              - generic [ref=e55]:
                - paragraph [ref=e56]: Password
                - paragraph [ref=e57]: Available as a sign-in method
            - paragraph [ref=e58]: Other sign-in methods are not shown here.
            - button "Add passkey" [disabled] [ref=e60]
          - complementary [ref=e62]:
            - heading "About passkeys" [level=2] [ref=e63]
            - paragraph [ref=e64]: Passkeys let you sign in using your device lock, fingerprint, or face recognition.
            - generic [ref=e65]:
              - strong [ref=e66]: You stay in control
              - paragraph [ref=e67]: Your browser will ask you to confirm with device security.
            - paragraph [ref=e68]: Your device or password manager handles the confirmation.
        - generic [ref=e69]:
          - button "Check status" [ref=e70] [cursor=pointer]
          - button "Continue setup" [ref=e72] [cursor=pointer]
  - dialog [ref=e74]:
    - generic [ref=e75]:
      - heading "Confirm it’s you" [level=1] [ref=e76]
      - paragraph [ref=e77]: For your security, verify your identity before continuing.
      - generic [ref=e78]:
        - status [ref=e79]: We are checking this verification. Please wait before trying again.
        - generic [ref=e80]:
          - button "Close" [disabled] [ref=e81]
          - button "Check verification status" [disabled] [ref=e83]
          - button "Continue this verification" [disabled] [ref=e85]
```

# Test source

```ts
  1  | import {expect,test} from '@playwright/test';
  2  | import AxeBuilder from '@axe-core/playwright';
  3  | import {beginNative,counts,proofDialog,reviewEnrollment,setupProof} from './step-up-passkey.fixtures';
  4  |
  5  | test('Operation passkey proof invokes native get and authorizes only the exact enrollment; no password is required',async({page})=>{
  6  |   const {owner,native,requestId,reference}=await setupProof(page,{passwordAvailable:false});await reviewEnrollment(page);const dialog=proofDialog(page);await expect(dialog.getByRole('button',{name:'Other method',exact:true})).toBeDisabled();await beginNative(page);await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(native.executions).toBe(1);expect(native.assertions).toBe(1);expect(owner.enrollments).toBe(0);expect((await counts(page)).gets).toBe(1);expect(native.submissions[0]).toMatchObject({requestId,attemptId:native.attemptId,ceremonyId:native.ceremonyId});expect(reference()).toBe(owner.operation!.ceremonyId.replaceAll('-',''));await expect(page.getByText('Changes saved successfully')).toHaveCount(0);await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(page.getByText('Changes saved successfully')).toBeVisible();expect(owner.enrollments).toBe(1);expect((await counts(page)).creates).toBe(1);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  7  | });
  8  |
  9  | test('The shared chooser advertises only trusted available native capability and browser support',async({page})=>{
  10 |   const {native}=await setupProof(page,{available:false});await reviewEnrollment(page);await expect(proofDialog(page).getByRole('button',{name:'Passkey',exact:true})).toBeDisabled();expect((await counts(page)).gets).toBe(0);native.available=true;await page.reload();await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Passkey',exact:true})).toBeEnabled();await page.evaluate(()=>Object.defineProperty(window,'PublicKeyCredential',{configurable:true,value:undefined}));await proofDialog(page).getByRole('button',{name:'Passkey',exact:true}).click();expect(native.options).toHaveLength(0);expect(native.executions).toBe(0);
  11 | });
  12 |
  13 | test('An unsupported browser keeps the trusted native method unavailable and the password alternative usable',async({page})=>{
  14 |   const {native}=await setupProof(page);await page.addInitScript(()=>Object.defineProperty(window,'PublicKeyCredential',{configurable:true,value:undefined}));await page.goto('/account/security/sign-in-methods');await page.getByRole('button',{name:'Manage',exact:true}).click();await proofDialog(page).getByRole('button',{name:'Remove passkey',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Passkey',exact:true})).toBeDisabled();await expect(proofDialog(page).getByText('This browser does not support this method. Choose another available method.')).toBeVisible();await expect(proofDialog(page).getByRole('button',{name:'Other method',exact:true})).toBeEnabled();expect(native.options).toHaveLength(0);
  15 | });
  16 |
  17 | test('Native cancellation keeps the same pending challenge for an explicit retry and allows another method',async({page})=>{
  18 |   const {native}=await setupProof(page);await reviewEnrollment(page);await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='cancel');await beginNative(page);await expect(proofDialog(page).getByText('Verification was not completed. You can try again or choose another available method.')).toBeVisible();const attempt=native.attemptId;await proofDialog(page).getByRole('button',{name:'Back',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Other method',exact:true})).toBeEnabled();await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='success');await beginNative(page);await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(native.options).toHaveLength(2);expect(native.options[1]!.attemptId).toBe(attempt);expect(native.assertions).toBe(1);expect((await counts(page)).gets).toBe(2);
  19 | });
  20 |
  21 | test('Lost native options retain the original proof request and attempt until an explicit continuation',async({page})=>{
> 22 |   const {native}=await setupProof(page);let first=true;await page.route('**/authentication/step-up/passkey/options',async route=>{if(!first){await route.fallback();return;}first=false;native.options.push(route.request().postDataJSON());await route.abort('connectionreset');});await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByText('We are checking this verification. Please wait before trying again.')).toBeVisible();expect((await counts(page)).gets).toBe(0);await proofDialog(page).getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(native.options).toHaveLength(2);expect(native.options[0]).toEqual(native.options[1]);expect(native.executions).toBe(1);
     |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     ^ Error: expect(received).toHaveLength(expected)
  23 | });
  24 |
  25 | test('Lost completed assertion reconciles the exact owner without another native get or mutation',async({page})=>{
  26 |   const {native,execute}=await setupProof(page);await page.route('**/authentication/step-up/passkey',async route=>{native.submissions.push(route.request().postDataJSON());native.assertions++;execute();await route.fulfill({status:200,contentType:'application/json',body:'{'});});await reviewEnrollment(page);await beginNative(page);await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(native.executions).toBe(1);expect(native.assertions).toBe(1);expect((await counts(page)).gets).toBe(1);
  27 | });
  28 |
  29 | test('Protected PROOF_ISSUED reload continues the same proof without native assertion or new IDs',async({page})=>{
  30 |   const {native,owner,requestId}=await setupProof(page);await page.route('**/authentication/step-up/passkey',async route=>{native.submissions.push(route.request().postDataJSON());native.assertions++;native.status='PROOF_ISSUED';await route.abort('connectionreset');});await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByRole('button',{name:'Continue this verification',exact:true})).toBeVisible();expect(native.executions).toBe(0);const original={requestId,attemptId:native.attemptId,ceremonyId:native.ceremonyId};await page.reload();await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Continue this verification',exact:true})).toBeVisible();expect((await counts(page)).gets).toBe(0);await proofDialog(page).getByRole('button',{name:'Check verification status',exact:true}).click();expect(native.executions).toBe(0);await proofDialog(page).getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(native.continuations).toBe(1);expect(native.submissions.at(-1)).toEqual(original);expect(owner.operation!.status).toBe('AUTHORIZED');expect(native.options).toHaveLength(1);expect((await counts(page)).gets).toBe(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  31 | });
  32 |
  33 | test('Closing an unresolved assertion preserves owner and native binding instead of cancelling the operation',async({page})=>{
  34 |   const {native,owner}=await setupProof(page);let cancels=0;page.on('request',request=>{if(request.url().endsWith('/account/passkeys/cancel'))cancels++;});await page.route('**/authentication/step-up/passkey',route=>route.abort('connectionreset'));await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();const original={requestId:owner.operation!.requestId,ceremonyId:owner.operation!.ceremonyId,attemptId:native.attemptId};await proofDialog(page).getByRole('button',{name:'Close',exact:true}).click();await expect(proofDialog(page)).toHaveCount(0);expect(cancels).toBe(0);await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeDisabled();await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();expect(native.attemptId).toBe(original.attemptId);expect(owner.operation).toMatchObject({requestId:original.requestId,ceremonyId:original.ceremonyId,status:'PENDING'});expect(native.executions).toBe(0);
  35 | });
  36 |
  37 | for(const mismatch of ['requestId','attemptId','ceremonyId','operationReference'] as const)test('A completion with mismatched '+mismatch+' cannot complete the owner',async({page})=>{
  38 |   const fixture=await setupProof(page);await page.route('**/authentication/step-up/passkey',route=>route.fulfill({json:{...fixture.completed(),[mismatch]:'99999999-9999-4999-8999-999999999999'}}));await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByText('We are checking this verification. Please wait before trying again.')).toBeVisible();expect(fixture.native.executions).toBe(0);expect(fixture.owner.operation!.status).toBe('PENDING');await expect(page.getByText('Changes saved successfully')).toHaveCount(0);
  39 | });
  40 |
  41 | test('Mismatched protected recovery cannot replace the original native attempt after an uncertain assertion',async({page})=>{
  42 |   const fixture=await setupProof(page);await page.route('**/authentication/step-up/passkey',route=>route.abort('connectionreset'));await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();const original=fixture.native.attemptId;fixture.native.attemptId='99999999-9999-4999-8999-999999999999';await proofDialog(page).getByRole('button',{name:'Check verification status',exact:true}).click();await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();expect(fixture.native.executions).toBe(0);expect(fixture.native.options).toHaveLength(1);expect(fixture.native.options[0]!.attemptId).toBe(original);await expect(page.getByText('Changes saved successfully')).toHaveCount(0);
  43 | });
  44 |
  45 | for(const mismatch of ['requestId','attemptId','operationReference'] as const)test('Mismatched native options '+mismatch+' cannot invoke the browser ceremony',async({page})=>{
  46 |   const fixture=await setupProof(page);await page.route('**/authentication/step-up/passkey/options',route=>{const body=route.request().postDataJSON();fixture.native.attemptId=body.attemptId;fixture.native.ceremonyId='88888888-8888-4888-8888-888888888888';return route.fulfill({json:{...fixture.recovery(),[mismatch]:'99999999-9999-4999-8999-999999999999',publicKey:{challenge:'c3ludGhldGlj',userVerification:'required'}}});});await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();expect((await counts(page)).gets).toBe(0);expect(fixture.native.executions).toBe(0);
  47 | });
  48 |
  49 | for(const exact of [true,false])test('Only exact correlated rejection resolves a submitted native attempt; matching='+exact,async({page})=>{
  50 |   const fixture=await setupProof(page);await page.route('**/authentication/step-up/passkey',route=>route.fulfill({status:401,json:{error_code:'AUTHENTICATION_FAILED',...fixture.recovery(),ceremony_status:'REJECTED',...(exact?{}:{attemptId:'99999999-9999-4999-8999-999999999999'})}}));await reviewEnrollment(page);await beginNative(page);if(exact){await expect(proofDialog(page).getByText('Your session or this request changed. Return to the action and try again.').first()).toBeVisible();await expect(proofDialog(page).getByRole('button',{name:'Continue this verification',exact:true})).toHaveCount(0);}else await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();expect(fixture.native.executions).toBe(0);expect(fixture.owner.operation!.status).toBe('PENDING');
  51 | });
  52 |
  53 | test('Terminal self-revocation from native proof uses the original owner read-only result handle',async({page})=>{
  54 |   const {native,owner,execute}=await setupProof(page,{terminal:true,passwordAvailable:false});await page.route('**/authentication/step-up/passkey',async route=>{native.submissions.push(route.request().postDataJSON());native.assertions++;execute();await route.abort('connectionreset');});await page.goto('/account/security/sign-in-methods');await page.getByRole('button',{name:'Manage',exact:true}).click();await proofDialog(page).getByRole('button',{name:'Remove passkey',exact:true}).click();await beginNative(page);await expect(page.getByRole('heading',{name:'This passkey was removed. Sign in again to continue.'})).toBeVisible();expect(owner.authenticated).toBe(false);expect(native.executions).toBe(1);await expect(page.getByText('Changes saved successfully')).toHaveCount(0);expect((await counts(page)).gets).toBe(1);
  55 | });
  56 |
  57 | for(const width of [1440,390,320])test('Shared native proof feedback retains the approved overlay at '+width+'px',async({page},testInfo)=>{
  58 |   await page.setViewportSize({width,height:900});await setupProof(page);await page.route('**/authentication/step-up/passkey',route=>route.abort('connectionreset'));await reviewEnrollment(page);await beginNative(page);await expect(proofDialog(page).getByRole('button',{name:'Check verification status',exact:true})).toBeVisible();expect(await proofDialog(page).evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);await expect(proofDialog(page).getByRole('heading',{name:'Confirm it’s you'})).toBeFocused();await page.screenshot({path:testInfo.outputPath('native-proof-'+width+'.png'),fullPage:true});const audit=await new AxeBuilder({page}).analyze();await testInfo.attach('native-proof-accessibility',{body:JSON.stringify(audit),contentType:'application/json'});expect(audit.violations).toEqual([]);
  59 | });
  60 |
```
