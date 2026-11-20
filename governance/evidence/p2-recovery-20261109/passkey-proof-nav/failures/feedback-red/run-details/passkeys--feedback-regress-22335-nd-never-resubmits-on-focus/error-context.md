# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: passkeys.spec.ts >> @feedback-regression Uncertain Login focuses only exact read-only recovery and never resubmits on focus
- Location: tests\passkeys\passkeys.spec.ts:176:1

# Error details

```
Error: expect(locator).toBeFocused() failed

Locator:  getByRole('button', { name: 'Check sign-in status', exact: true })
Expected: focused
Received: inactive
Timeout:  5000ms

Call log:
  - Expect "toBeFocused" with timeout 5000ms
  - waiting for getByRole('button', { name: 'Check sign-in status', exact: true })
    14 × locator resolved to <button type="button" class="tn-primary-button" data-appearance="outline" data-presentation="standard">…</button>
       - unexpected value "inactive"

```

```yaml
- button "Check sign-in status"
```

# Test source

```ts
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
  112 |     expect(box!.y+box!.height,name+' bottom is inside the visible dialog').toBeLessThanOrEqual(container!.y+container!.height-1);
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
> 187 |   await expect(recovery).toBeFocused();
      |                          ^ Error: expect(locator).toBeFocused() failed
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
  213 |   await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));
  214 |   await expect(heading).toBeFocused();
  215 |   await page.screenshot({path:testInfo.outputPath('aborted-login-new-route-focus.png'),fullPage:true});
  216 |   expect(state.assertions).toBe(0);
  217 |   expect((await nativeCounts(page)).gets).toBe(1);
  218 |   await expect(page.getByRole('button',{name:'Sign in with passkey',exact:true})).toHaveCount(0);
  219 | });
  220 |
```
