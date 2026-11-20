# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: passkeys.spec.ts >> @feedback-regression Remove error keeps every action within the desktop dialog without scrolling
- Location: tests\passkeys\passkeys.spec.ts:97:1

# Error details

```
Error: Close bottom is inside the visible dialog

expect(received).toBeLessThanOrEqual(expected)

Expected: <= 714
Received:    729
```

# Page snapshot

```yaml
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
      - status [ref=e69]: A passkey request is in progress. Check its status before trying again.
      - generic [ref=e70]:
        - button "Check status" [ref=e71] [cursor=pointer]
        - button "Try again" [ref=e73] [cursor=pointer]
  - dialog [ref=e75]:
    - generic [ref=e76]:
      - heading "Remove this passkey?" [active] [level=2] [ref=e77]
      - paragraph [ref=e78]: You will no longer be able to sign in with this passkey.
      - paragraph [ref=e79]: Tunner sessions signed in with this passkey will end. If this includes your current session, you will need to sign in again.
      - generic [ref=e80]:
        - paragraph [ref=e81]:
          - text: Added
          - time [ref=e82]: Nov 09, 2026
        - paragraph [ref=e83]: "Last used: Not used yet"
      - status [ref=e84]: We are checking whether this passkey was removed. Please wait before trying again.
      - alert [ref=e85]: Passkeys are not available right now. Please try again later.
      - generic [ref=e86]:
        - button "Close" [ref=e87] [cursor=pointer]
        - button "Check status" [ref=e89] [cursor=pointer]
```

# Test source

```ts
  12  |   test('Approved methods/Add composition, actual facts, keyboard and accessibility at '+width,async({page})=>{
  13  |     await page.setViewportSize({width,height:1100});await setup(page);await page.goto(paths.methods);await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();await expect(page.getByText('No passkey added yet',{exact:true})).toBeVisible();
  14  |     expect(await page.locator('.passkeys-header').boundingBox()).toMatchObject({x:0,y:0,width});
  15  |     await expect(page.getByText(/Farhan|Taylor|PROTECTED|STRONG|SMS backup|View codes|Authenticator app enabled/)).toHaveCount(0);await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeEnabled();
  16  |     await page.evaluate(()=>document.fonts.ready);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  17  |     await page.screenshot({path:'test-results/passkeys/methods-'+width+'.png',fullPage:true});await page.getByRole('button',{name:'Add passkey',exact:true}).click();await expect(page).toHaveURL(new RegExp(paths.methods+'$'));
  18  |     const dialog=page.getByRole('dialog');await expect(dialog.getByRole('heading',{name:'Add a passkey'})).toBeFocused();const box=await dialog.boundingBox();expect(box?.width).toBe(Math.min(520,width-32));expect(box?.height).toBe(410);await expect(dialog.getByText('Current browser',{exact:true})).toBeVisible();await expect(dialog.getByText(/Chrome|Windows/)).toHaveCount(0);
  19  |     await page.keyboard.press('Tab');await expect(dialog.getByRole('button',{name:'Not now'})).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(dialog.getByRole('button',{name:'Add passkey',exact:true})).toBeFocused();await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeFocused();
  20  |     await page.getByRole('button',{name:'Add passkey',exact:true}).click();expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);await page.screenshot({path:'test-results/passkeys/add-'+width+'.png'});await noBrowserStorage(page);
  21  |   });
  22  | }
  23  |
  24  | test('Canonical security/method routes survive direct entry, refresh and browser history; modal stays local',async({page})=>{
  25  |   await setup(page);await page.goto(paths.security);await expect(page.getByRole('heading',{name:'Protect your Tunner account'})).toBeVisible();await expect(page.getByText('Security activity is unavailable here.')).toBeVisible();await page.getByRole('link',{name:'Review passkeys'}).click();await expect(page).toHaveURL(new RegExp(paths.methods+'$'));await page.reload();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();await page.goBack();await expect(page.getByRole('heading',{name:'Protect your Tunner account'})).toBeVisible();await page.goForward();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();
  26  |   await page.goto(paths.methods+'?fixture=not-sensitive#section');await expect(page).toHaveURL(new RegExp(paths.methods+'$'));await noBrowserStorage(page);
  27  | });
  28  | test('Unauthenticated direct entry is guarded and missing policy/publication never becomes an empty ready account',async({page})=>{
  29  |   const state=await setup(page,{authenticated:false});await page.goto(paths.methods);await expect(page).toHaveURL(/\/login$/);await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();state.authenticated=true;state.missingPolicy=true;await page.goto(paths.methods);await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.getByRole('button',{name:'Add passkey'})).toHaveCount(0);state.missingPolicy=false;state.missingContent=true;await page.reload();await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');
  30  | });
  31  | test('Enrollment stays inactive after step-up; native create and exact committed result own activation',async({page})=>{
  32  |   const state=await setup(page);await page.goto(paths.methods);await page.getByRole('button',{name:'Add passkey',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).evaluate(element=>{(element as HTMLButtonElement).click();(element as HTMLButtonElement).click();});await expect(page.getByRole('heading',{name:'Confirm it’s you'})).toBeVisible();expect(state.starts).toHaveLength(1);expect((await nativeCounts(page)).creates).toBe(0);expect(state.credentials).toHaveLength(0);
  33  |   await passwordProof(page);await expect(page.getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(state.proofs).toBe(1);expect(state.enrollments).toBe(0);expect((await nativeCounts(page)).creates).toBe(0);await expect(page.getByText('Changes saved successfully')).toHaveCount(0);
  34  |   await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(page.getByText('Changes saved successfully',{exact:true})).toBeVisible();expect(state.enrollments).toBe(1);expect((await nativeCounts(page)).creates).toBe(1);await expect(page.getByText('ACTIVE',{exact:true})).toBeVisible();await noBrowserStorage(page);
  35  | });
  36  | test('Native cancellation cancels only its exact inactive enrollment and the next deliberate request uses a new UUID',async({page})=>{
  37  |   const state=await setup(page);await page.goto(paths.methods);await authorizeEnrollment(page);await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='cancel');await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(page.getByText('Passkey setup was cancelled. No passkey was added.')).toBeVisible();expect(state.credentials).toHaveLength(0);expect(state.enrollments).toBe(0);const previous=state.starts[0]!.body.requestId;await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();await page.getByRole('button',{name:'Add passkey',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();await expect(page.getByRole('heading',{name:'Confirm it’s you'})).toBeVisible();expect(state.starts[1]!.body.requestId).not.toBe(previous);
  38  | });
  39  | test('Lost enrollment response checks the exact original result without another mutation or native create',async({page})=>{
  40  |   const state=await setup(page,{loseEnrollment:true});await page.goto(paths.methods);await authorizeEnrollment(page);await page.getByRole('button',{name:'Continue setup',exact:true}).click();await expect(page.getByRole('dialog').getByText('We are checking whether this passkey was added. Please wait before trying again.')).toBeVisible();await expect(page.getByText('Changes saved successfully')).toHaveCount(0);const original=state.operation!;await page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByText('Changes saved successfully')).toBeVisible();expect(state.operation?.ceremonyId).toBe(original.ceremonyId);expect(state.enrollments).toBe(1);expect(state.starts).toHaveLength(1);expect((await nativeCounts(page)).creates).toBe(1);
  41  | });
  42  | test('Reload recovers original authorized metadata from server context without browser storage or new request',async({page})=>{
  43  |   const state=await setup(page);await page.goto(paths.methods);await authorizeEnrollment(page);const original=state.operation!;await page.reload();await expect(page.getByText('A passkey request is in progress. Check its status before trying again.')).toBeVisible();await page.getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true})).toBeVisible();expect(state.starts).toHaveLength(1);expect(state.operation?.requestId).toBe(original.requestId);expect((await nativeCounts(page)).creates).toBe(0);await noBrowserStorage(page);
  44  | });
  45  | test('A different tab’s selected proof owner cannot be authenticated under the displayed enrollment review',async({page})=>{
  46  |   const state=await setup(page,{wrongProofOwner:true});await page.goto(paths.methods);await page.getByRole('button',{name:'Add passkey',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();await expect(page.getByRole('alert')).toHaveText('This request has changed or expired. Refresh to review your current options.');await expect(page.getByRole('heading',{name:'Confirm it’s you'})).toHaveCount(0);expect(state.proofs).toBe(0);expect(state.enrollments).toBe(0);
  47  | });
  48  | for(const effect of ['PreserveExistingSessions','RevokeCredentialSessions'] as const){for(const lost of [false,true]){
  49  |   test('Revocation uses trusted '+effect+' and exact terminal recovery; lost proof='+lost,async({page})=>{
  50  |     const state=await setup(page,{credentials:[activeCredential()],effect,loseProof:lost});await page.goto(paths.methods);await page.getByRole('button',{name:'Manage',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog.getByText(effect==='PreserveExistingSessions'?'Your existing Tunner sessions will remain signed in.':'Tunner sessions signed in with this passkey will end. If this includes your current session, you will need to sign in again.')).toBeVisible();await dialog.getByRole('button',{name:'Remove passkey',exact:true}).click();await passwordProof(page);
  51  |     if(effect==='RevokeCredentialSessions'){await expect(page.getByRole('heading',{name:'This passkey was removed. Sign in again to continue.'})).toBeVisible();expect(state.authenticated).toBe(false);await expect(page.getByText('Changes saved successfully')).toHaveCount(0);await page.getByRole('link',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/login$/);await expect(page.getByText('This passkey was removed. Sign in again to continue.')).toBeVisible();}
  52  |     else{await expect(page.getByText('Changes saved successfully')).toBeVisible();await expect(page.getByText('REMOVED',{exact:true})).toBeVisible();expect(state.authenticated).toBe(true);}
  53  |     expect(state.proofs).toBe(1);expect(state.starts).toHaveLength(1);expect(state.credentials[0]!.state).toBe('REVOKED');await noBrowserStorage(page);
  54  |   });
  55  | }}
  56  | test('An ended session without exact terminal proof cannot claim that removal succeeded',async({page})=>{
  57  |   await setup(page,{credentials:[activeCredential()],effect:'RevokeCredentialSessions',loseProof:true});await page.route('**/account/passkeys/terminal-result',route=>route.fulfill({status:404,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}}));await page.goto(paths.methods);await page.getByRole('button',{name:'Manage',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click();await passwordProof(page);await expect(page.getByRole('heading',{name:'Your session has ended. Sign in again to continue.'})).toBeVisible();await expect(page.getByText('This passkey was removed. Sign in again to continue.')).toHaveCount(0);
  58  | });
  59  | for(const lost of [false,true]){test('Primary passkey assertion uses native get and exact original result; lost body='+lost,async({page})=>{
  60  |   const state=await setup(page,{authenticated:false,loseSignIn:lost});await page.goto('/login');await page.getByRole('checkbox',{name:'Remember me'}).check();await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();expect(state.assertions).toBe(1);expect((await nativeCounts(page)).gets).toBe(1);if(lost)expect(state.resultReads).toBe(1);await noBrowserStorage(page);
  61  | });}
  62  | test('An unrelated surviving session/result is not accepted after an uncertain primary assertion',async({page})=>{
  63  |   await setup(page,{authenticated:true,loseSignIn:true,unrelatedResult:true});await page.goto('/login');await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page.getByText('We are checking whether this sign-in completed. Please wait before trying again.')).toBeVisible();await expect(page).toHaveURL(/\/login$/);await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();await noBrowserStorage(page);
  64  | });
  65  | test('Lost challenge options explicitly recover the same request/ceremony before native assertion',async({page})=>{
  66  |   const state=await setup(page,{authenticated:false});let first=true;const requests:string[]=[];await page.route('**/authentication/passkeys/options',async route=>{const body=route.request().postDataJSON();requests.push(body.requestId);if(first){first=false;state.loginOperation={requestId:body.requestId,ceremonyId:'12345678-1234-4234-9234-123456789abc',kind:'SIGN_IN',status:'PENDING',credentialReference:null,expectedVersion:0,expiresAt:new Date(Date.now()+300000).toISOString(),resultHandle:null,publicKey:null} satisfies PasskeyOperation;await route.fulfill({status:200,contentType:'application/json',body:'{'});return;}await route.fallback();});await page.goto('/login');await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page.getByText('We are checking whether this sign-in completed. Please wait before trying again.')).toBeVisible();await page.getByRole('button',{name:'Continue this sign-in'}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(requests.length).toBe(2);expect(new Set(requests).size).toBe(1);expect((await nativeCounts(page)).gets).toBe(1);
  67  | });
  68  | test('Unsupported browser and known native cancellation remain truthful and preserve password alternative',async({page})=>{
  69  |   await setup(page,{authenticated:false});await page.goto('/login');await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='cancel');await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page.getByText('We could not complete passkey sign-in. Try again or choose another sign-in method.')).toBeVisible();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeEnabled();await page.addInitScript(()=>Object.defineProperty(window,'PublicKeyCredential',{configurable:true,value:undefined}));await page.reload();await expect(page.getByRole('button',{name:'Sign in with passkey',exact:true})).toBeDisabled();await expect(page.getByText('This browser does not support passkeys. Use another available sign-in method.')).toBeVisible();
  70  | });
  71  |
  72  | // Regression-first cases: these are anchored and run RED against the unmodified primary candidate.
  73  | test('@regression SIGN_IN hard reload retains the protected original IDs and remember setting',async({page})=>{
  74  |   const state=await setup(page,{authenticated:false,exposeSignInRecovery:true});const resultBodies:Array<Record<string,unknown>>=[];const optionBodies:Array<Record<string,unknown>>=[];
  75  |   page.on('request',request=>{if(request.method()==='POST'&&request.url().endsWith('/authentication/passkeys/result'))resultBodies.push(request.postDataJSON());if(request.method()==='POST'&&request.url().endsWith('/authentication/passkeys/options'))optionBodies.push(request.postDataJSON());});
  76  |   await page.route('**/authentication/passkeys/sign-in',route=>route.abort('connectionreset'));
  77  |   await page.goto('/login');await page.getByRole('checkbox',{name:'Remember me'}).check();await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page.getByText('We are checking whether this sign-in completed. Please wait before trying again.')).toBeVisible();const original={...state.loginOperation!};expect(state.loginRememberMe).toBe(true);
  78  |   await page.reload();await expect(page.getByText('We are checking whether this sign-in completed. Please wait before trying again.')).toBeVisible();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Sign in with passkey',exact:true})).toHaveCount(0);
  79  |   await page.getByRole('button',{name:'Check sign-in status',exact:true}).click();await expect.poll(()=>resultBodies.at(-1)).toEqual({requestId:original.requestId,ceremonyId:original.ceremonyId});expect(optionBodies).toHaveLength(1);await noBrowserStorage(page);
  80  | });
  81  | test('@regression Committed revocation-start503 and later429 retain the exact immutable start snapshot',async({page})=>{
  82  |   const credential=activeCredential();const state=await setup(page,{credentials:[credential],startFailureStatus:503});await page.goto(paths.methods);await page.getByRole('button',{name:'Manage',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click();const original={...state.operation!};
  83  |   await expect(page.getByRole('dialog').getByText('We are checking whether this passkey was removed. Please wait before trying again.')).toBeVisible();state.recovery=false;await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();
  84  |   const rateLimit=(route:Route)=>{state.starts.push({path:'revocation',body:route.request().postDataJSON()});return route.fulfill({status:429,json:{error_code:'RATE_OR_ABUSE_LIMITED'}});};await page.route('**/account/passkeys/revocation/start',rateLimit);await page.getByRole('button',{name:'Try again',exact:true}).click();await expect(page.getByRole('alert')).toHaveText('Too many attempts. Please try again later.');
  85  |   expect(state.starts).toHaveLength(2);for(const entry of state.starts)expect(entry.body).toEqual({requestId:original.requestId,credentialReference:credential.credentialReference,expectedVersion:credential.version});await expect(page.getByRole('button',{name:'Add passkey',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Manage',exact:true})).toBeDisabled();
  86  |   await page.unroute('**/account/passkeys/revocation/start',rateLimit);state.recovery=true;const beforeReads=state.resultReads;await page.getByRole('button',{name:'Check status',exact:true}).click();await expect.poll(()=>state.resultReads).toBe(beforeReads+1);expect(state.operation?.requestId).toBe(original.requestId);expect(state.operation?.ceremonyId).toBe(original.ceremonyId);expect(state.proofs).toBe(0);await noBrowserStorage(page);
  87  | });
  88  | test('@regression Unresolved startA cannot adopt another tab completedB during canonical SPA navigation',async({page})=>{
  89  |   const state=await setup(page,{loseStart:true});const readResults:Array<Record<string,unknown>>=[];page.on('request',request=>{if(request.method()==='POST'&&request.url().endsWith('/account/passkeys/result'))readResults.push(request.postDataJSON());});await page.goto(paths.security);await page.getByRole('button',{name:'Add passkey',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click();await expect(page.getByRole('dialog').getByText('We are checking whether this passkey was added. Please wait before trying again.')).toBeVisible();const original={...state.operation!};await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();
  90  |   const otherCredential=activeCredential();state.operation={...original,requestId:'99999999-9999-4999-8999-999999999999',ceremonyId:'88888888-8888-4888-8888-888888888888',status:'COMPLETED',credentialReference:otherCredential.credentialReference,resultHandle:'synthetic-other-tab-read-only-handle'};state.credentials.push(otherCredential);state.recovery=true;
  91  |   await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await expect(page).toHaveURL(new RegExp(paths.methods+'$'));await page.getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('alert')).toHaveText('We couldn’t complete the request. Please try again.');await expect(page.getByText('Changes saved successfully')).toHaveCount(0);expect(readResults.some(body=>body.requestId===state.operation?.requestId||body.ceremonyId===state.operation?.ceremonyId)).toBe(false);expect(state.starts).toHaveLength(1);await noBrowserStorage(page);
  92  | });
  93  | test('Password row is rendered only from the current owner availability fact, with no invented management action',async({page})=>{
  94  |   const state=await setup(page,{passwordAvailable:true});await page.goto(paths.methods);const password=page.getByRole('group',{name:'Password',exact:true});await expect(password).toBeVisible();await expect(password.getByText('Available as a sign-in method',{exact:true})).toBeVisible();await expect(password.getByRole('button')).toHaveCount(0);state.passwordAvailable=false;await page.reload();await expect(page.getByRole('heading',{name:'Passkeys & two-step verification'})).toBeVisible();await expect(page.getByRole('group',{name:'Password',exact:true})).toHaveCount(0);await noBrowserStorage(page);
  95  | });
  96  |
  97  | test('@feedback-regression Remove error keeps every action within the desktop dialog without scrolling',async({page},testInfo)=>{
  98  |   const state=await setup(page,{credentials:[activeCredential()],startFailureStatus:503,effect:'RevokeCredentialSessions'});
  99  |   await page.goto(paths.methods);
  100 |   await page.getByRole('button',{name:'Manage',exact:true}).click();
  101 |   const dialog=page.getByRole('dialog');
  102 |   expect(await dialog.boundingBox()).toMatchObject({width:500,height:330});
  103 |   await dialog.getByRole('button',{name:'Remove passkey',exact:true}).click();
  104 |   await expect(dialog.getByRole('alert')).toHaveText('Passkeys are not available right now. Please try again later.');
  105 |   await expect(dialog.getByRole('heading',{name:'Remove this passkey?',exact:true})).toBeFocused();
  106 |   await page.screenshot({path:testInfo.outputPath('remove-error-desktop.png'),fullPage:true});
  107 |   const container=await dialog.boundingBox();
  108 |   expect(container).not.toBeNull();
  109 |   for(const name of ['Close','Check status']){
  110 |     const box=await dialog.getByRole('button',{name,exact:true}).boundingBox();
  111 |     expect(box).not.toBeNull();
> 112 |     expect(box!.y+box!.height,name+' bottom is inside the visible dialog').toBeLessThanOrEqual(container!.y+container!.height-1);
      |                                                                            ^ Error: Close bottom is inside the visible dialog
  113 |     expect(box!.y,name+' top is inside the visible dialog').toBeGreaterThanOrEqual(container!.y+1);
  114 |   }
  115 |   expect(await dialog.evaluate(element=>element.scrollHeight<=element.clientHeight+1)).toBe(true);
  116 |   expect(state.starts).toHaveLength(1);
  117 |   expect(state.proofs).toBe(0);
  118 |   expect(state.credentials[0]!.state).toBe('ACTIVE');
  119 | });
  120 |
  121 | for(const kind of ['ENROLL','REVOKE'] as const)test('@feedback-regression '+kind+' unknown Close focuses existing recovery without replacing the request',async({page},testInfo)=>{
  122 |   const state=await setup(page,{credentials:kind==='REVOKE'?[activeCredential()]:[],startFailureStatus:503});
  123 |   const resultBodies:Array<Record<string,unknown>>=[];
  124 |   page.on('request',request=>{if(request.url().endsWith('/account/passkeys/result'))resultBodies.push(request.postDataJSON());});
  125 |   await page.goto(paths.methods);
  126 |   const trigger=page.getByRole('button',{name:kind==='ENROLL'?'Add passkey':'Manage',exact:true});
  127 |   await trigger.click();
  128 |   const dialog=page.getByRole('dialog');
  129 |   await dialog.getByRole('button',{name:kind==='ENROLL'?'Add passkey':'Remove passkey',exact:true}).click();
  130 |   await expect(dialog.getByRole('alert')).toHaveText('Passkeys are not available right now. Please try again later.');
  131 |   const original={...state.operation!};
  132 |   await dialog.getByRole('heading').first().focus();
  133 |   await page.keyboard.press('Tab');
  134 |   await expect(dialog.getByRole('button',{name:'Close',exact:true})).toBeFocused();
  135 |   await page.keyboard.press('Enter');
  136 |   await expect(dialog).toHaveCount(0);
  137 |   await page.screenshot({path:testInfo.outputPath(kind.toLowerCase()+'-unknown-closed.png'),fullPage:true});
  138 |   const recovery=page.getByRole('button',{name:'Check status',exact:true});
  139 |   await expect(recovery).toBeFocused();
  140 |   await expect(recovery).toBeEnabled();
  141 |   await expect(trigger).toBeDisabled();
  142 |   expect(state.starts).toHaveLength(1);
  143 |   expect(state.operation).toEqual(original);
  144 |   expect(state.proofs).toBe(0);
  145 |   expect(state.enrollments).toBe(0);
  146 |   const reads=state.resultReads;
  147 |   await page.keyboard.press('Enter');
  148 |   await expect.poll(()=>state.resultReads).toBe(reads+1);
  149 |   expect(resultBodies.at(-1)).toEqual({requestId:original.requestId,ceremonyId:original.ceremonyId});
  150 |   expect(state.starts).toHaveLength(1);
  151 |   expect(state.operation?.ceremonyId).toBe(original.ceremonyId);
  152 |   await noBrowserStorage(page);
  153 | });
  154 |
  155 | for(const feedback of ['native-cancel','http-429'] as const)test('@feedback-regression Login '+feedback+' restores its available action without another attempt',async({page},testInfo)=>{
  156 |   const state=await setup(page,{authenticated:false});
  157 |   const optionsBodies:Array<Record<string,unknown>>=[];
  158 |   page.on('request',request=>{if(request.url().endsWith('/authentication/passkeys/options'))optionsBodies.push(request.postDataJSON());});
  159 |   if(feedback==='http-429')await page.route('**/authentication/passkeys/options',route=>route.fulfill({status:429,json:{error_code:'RATE_OR_ABUSE_LIMITED'}}));
  160 |   await page.goto('/login');
  161 |   if(feedback==='native-cancel')await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='cancel');
  162 |   const action=page.getByRole('button',{name:'Sign in with passkey',exact:true});
  163 |   await action.click();
  164 |   if(feedback==='native-cancel')await expect(page.getByText('We could not complete passkey sign-in. Try again or choose another sign-in method.',{exact:true})).toBeVisible();
  165 |   else await expect(page.getByRole('alert')).toHaveText('Too many attempts. Please try again later.');
  166 |   await expect(action).toBeEnabled();
  167 |   await page.screenshot({path:testInfo.outputPath('login-'+feedback+'-focus.png'),fullPage:true});
  168 |   await expect(action).toBeFocused();
  169 |   await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeEnabled();
  170 |   expect(optionsBodies).toHaveLength(1);
  171 |   expect(state.assertions).toBe(0);
  172 |   expect((await nativeCounts(page)).gets).toBe(feedback==='native-cancel'?1:0);
  173 |   await noBrowserStorage(page);
  174 | });
  175 |
  176 | test('@feedback-regression Uncertain Login focuses only exact read-only recovery and never resubmits on focus',async({page},testInfo)=>{
  177 |   const state=await setup(page,{authenticated:false,loseSignIn:true,unrelatedResult:true});
  178 |   const resultBodies:Array<Record<string,unknown>>=[];
  179 |   page.on('request',request=>{if(request.url().endsWith('/authentication/passkeys/result'))resultBodies.push(request.postDataJSON());});
  180 |   await page.goto('/login');
  181 |   await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();
  182 |   await expect(page.getByText('We are checking whether this sign-in completed. Please wait before trying again.',{exact:true})).toBeVisible();
  183 |   const original={...state.loginOperation!};
  184 |   const recovery=page.getByRole('button',{name:'Check sign-in status',exact:true});
  185 |   await expect(recovery).toBeEnabled();
  186 |   await page.screenshot({path:testInfo.outputPath('login-uncertain-recovery-focus.png'),fullPage:true});
  187 |   await expect(recovery).toBeFocused();
  188 |   await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();
  189 |   expect(state.assertions).toBe(1);
  190 |   expect((await nativeCounts(page)).gets).toBe(1);
  191 |   const reads=state.resultReads;
  192 |   await page.keyboard.press('Enter');
  193 |   await expect.poll(()=>state.resultReads).toBe(reads+1);
  194 |   await expect(recovery).toBeEnabled();
  195 |   await expect(recovery).toBeFocused();
  196 |   expect(resultBodies.at(-1)).toEqual({requestId:original.requestId,ceremonyId:original.ceremonyId});
  197 |   expect(state.loginOperation?.requestId).toBe(original.requestId);
  198 |   expect(state.assertions).toBe(1);
  199 |   expect((await nativeCounts(page)).gets).toBe(1);
  200 |   await noBrowserStorage(page);
  201 | });
  202 |
  203 | test('@feedback-regression Aborted Login ceremony cannot steal new-route focus or submit credentials',async({page},testInfo)=>{
  204 |   const state=await setup(page,{authenticated:true});
  205 |   await page.goto('/login');
  206 |   await page.evaluate(()=>(window as unknown as {passkeyFixture:{mode:string}}).passkeyFixture.mode='hold');
  207 |   await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();
  208 |   await expect.poll(async()=>(await nativeCounts(page)).gets).toBe(1);
  209 |   await page.evaluate(()=>{history.pushState({},'', '/signed-in');dispatchEvent(new PopStateEvent('popstate'));});
  210 |   await expect(page).toHaveURL(/\/signed-in$/);
  211 |   const heading=page.getByRole('heading',{name:'You’re signed in',exact:true});
  212 |   await expect(heading).toBeFocused();
```
