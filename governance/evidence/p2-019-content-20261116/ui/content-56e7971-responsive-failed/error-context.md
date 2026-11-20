# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: content.spec.ts >> responsive Content workspace and dialog support tablet laptop and narrow reflow without writes
- Location: tests\content-admin\content.spec.ts:104:1

# Error details

```
Error: expect(received).toEqual(expected) // deep equality

- Expected  -  1
+ Received  + 42

- Array []
+ Array [
+   Object {
+     "cardLeft": 543,
+     "cardRight": 703,
+     "className": "",
+     "left": 678.28125,
+     "right": 748.5625,
+     "tag": "BUTTON",
+   },
+   Object {
+     "cardLeft": 543,
+     "cardRight": 703,
+     "className": "tn-input",
+     "left": 584,
+     "right": 746.015625,
+     "tag": "INPUT",
+   },
+   Object {
+     "cardLeft": 543,
+     "cardRight": 703,
+     "className": "tn-input",
+     "left": 584,
+     "right": 746.015625,
+     "tag": "INPUT",
+   },
+   Object {
+     "cardLeft": 543,
+     "cardRight": 703,
+     "className": "authentication-reveal",
+     "left": 703.015625,
+     "right": 745.015625,
+     "tag": "BUTTON",
+   },
+   Object {
+     "cardLeft": 543,
+     "cardRight": 703,
+     "className": "",
+     "left": 681.171875,
+     "right": 746.015625,
+     "tag": "BUTTON",
+   },
+ ]
```

# Page snapshot

```yaml
- generic [ref=f2e4]:
  - link "Skip to content" [ref=f2e5] [cursor=pointer]:
    - /url: "#content-main"
  - generic [ref=f2e6]:
    - complementary [ref=f2e7]:
      - generic [ref=f2e8]:
        - generic [ref=f2e9]: Tunner
        - paragraph [ref=f2e10]: Operations workspace
      - navigation [ref=f2e11]:
        - link "Content" [ref=f2e12] [cursor=pointer]:
          - /url: /admin/content
      - generic [ref=f2e13]:
        - strong [ref=f2e14]: Content workspace
        - paragraph [ref=f2e15]: Admin workspace · User sign-in remains separate
    - generic [ref=f2e16]:
      - banner [ref=f2e17]:
        - generic [ref=f2e18]:
          - strong [ref=f2e19]: Content management
          - paragraph [ref=f2e20]: Review and publish approved wording for each context.
      - region "Admin workspace · User sign-in remains separate" [ref=f2e21]
      - main [ref=f2e23]:
        - generic [ref=f2e24]:
          - generic [ref=f2e25]:
            - paragraph [ref=f2e26]: Content / User sign-in
            - heading "Content workspace" [level=1] [ref=f2e27]
            - paragraph [ref=f2e28]: Review and publish approved wording for each context.
          - navigation "Content" [ref=f2e29]:
            - link "Content workspace" [ref=f2e30] [cursor=pointer]:
              - /url: /admin/content
            - link "Review & publish" [ref=f2e31] [cursor=pointer]:
              - /url: /admin/content/review
            - link "Version history" [ref=f2e32] [cursor=pointer]:
              - /url: /admin/content/history
        - generic [ref=f2e33]:
          - region [ref=f2e34]:
            - generic [ref=f2e35]:
              - heading "Content library" [level=2] [ref=f2e36]
              - generic [ref=f2e37]:
                - generic [ref=f2e38]: Surface
                - combobox "Surface" [ref=f2e39]:
                  - option "Choose a surface"
                  - option "User sign-in" [selected]
              - generic [ref=f2e40]:
                - generic [ref=f2e41]:
                  - generic [ref=f2e42]: Search
                  - textbox "Search" [ref=f2e43]
                  - generic [ref=f2e44]: Find a purpose or semantic key.
                - button "Search" [ref=f2e45] [cursor=pointer]
            - list [ref=f2e47]:
              - listitem [ref=f2e48]:
                - button "Sign-in introduction login.heading" [active] [ref=f2e49] [cursor=pointer]:
                  - strong [ref=f2e50]: Sign-in introduction
                  - generic [ref=f2e51]: login.heading
          - generic [ref=f2e52]:
            - generic [ref=f2e53]:
              - generic [ref=f2e54]:
                - generic [ref=f2e55]:
                  - heading "Sign-in introduction" [level=2] [ref=f2e56]
                  - paragraph [ref=f2e57]: login.heading
                - generic [ref=f2e58]:
                  - generic [ref=f2e59]: Approved
                  - text: · 1
              - generic [ref=f2e60]:
                - generic [ref=f2e61]: Context
                - combobox "Context" [ref=f2e62]:
                  - option "Choose a context"
                  - option "en · Sign in" [selected]
              - generic [ref=f2e63]:
                - generic [ref=f2e64]: Candidate
                - combobox "Candidate" [ref=f2e65]:
                  - option "New draft"
                  - option "Approved · 1 · 00000001" [selected]
              - generic [ref=f2e66]:
                - generic [ref=f2e67]:
                  - generic [ref=f2e68]: Wording
                  - textbox "Wording" [ref=f2e69]: Welcome to Tunner
                  - generic [ref=f2e70]: Keep required variables intact. Changes remain drafts until published.
                - generic [ref=f2e71]:
                  - generic [ref=f2e72]: Change reason
                  - textbox "Change reason" [ref=f2e73]: Clarify the sign-in introduction
                  - generic [ref=f2e74]: Explain why this wording needs to change.
                - button "Save draft" [ref=f2e76] [cursor=pointer]
              - generic [ref=f2e78]:
                - paragraph [ref=f2e79]: Keep required variables intact. Changes remain drafts until published.
                - button "Preview & compare" [ref=f2e80] [cursor=pointer]
            - region [ref=f2e82]:
              - generic [ref=f2e83]:
                - heading "User context preview" [level=2] [ref=f2e84]
                - generic [ref=f2e85]: Candidate
              - generic [ref=f2e87]:
                - paragraph [ref=f2e88]:
                  - strong [ref=f2e89]: DRAFT
                  - text: · login.heading
                - generic [ref=f2e91]:
                  - generic [ref=f2e92]: Tunner
                  - generic [ref=f2e95]:
                    - generic [ref=f2e96]:
                      - heading "Welcome to Tunner" [level=1] [ref=f2e97]
                      - paragraph [ref=f2e98]: Enter your credentials to access your account
                    - generic [ref=f2e99]:
                      - generic [ref=f2e100]:
                        - button "Google" [disabled] [ref=f2e101]
                        - button "Microsoft" [disabled] [ref=f2e103]
                      - generic [ref=f2e105]: Or continue with
                    - generic [ref=f2e107]:
                      - generic [ref=f2e108]:
                        - generic [ref=f2e109]:
                          - generic [ref=f2e110]: Email address
                          - textbox "Email address" [disabled] [ref=f2e111]:
                            - /placeholder: you@example.com
                        - generic [ref=f2e112]:
                          - generic [ref=f2e113]:
                            - generic [ref=f2e114]: Password
                            - textbox "Password" [disabled] [ref=f2e115]:
                              - /placeholder: Enter your password
                          - button "Show password" [disabled] [ref=f2e116] [cursor=pointer]
                        - generic [ref=f2e117]:
                          - generic [ref=f2e118]:
                            - checkbox "Remember me" [disabled] [ref=f2e119]
                            - generic [ref=f2e120]: Remember me
                          - button "Forgot password?" [disabled] [ref=f2e121]
                      - generic [ref=f2e122]:
                        - button "Sign in" [disabled] [ref=f2e123]
                        - paragraph [ref=f2e125]:
                          - text: Don’t have an account?
                          - generic [ref=f2e126]: Create account
                  - generic [ref=f2e127]:
                    - generic [ref=f2e128]: Privacy Policy
                    - generic [ref=f2e129]: •
                    - generic [ref=f2e130]: Terms of Service
```

# Test source

```ts
  19  |   if(path==='/content/definitions')return reply({items:[{key:'login.heading',purpose:'Sign-in introduction',contentClass:'fixture-public-copy',mandatory:true,allowFallback:true,requiresApproval:true,variables:[],requiredVariables:[]}],nextOffset:null});
  20  |   if(path==='/content/proposals/search')return reply({items:[proposal()],nextOffset:null});
  21  |   if(path==='/content/history')return reply({items:[publication()],nextOffset:null});
  22  |   if(path==='/content/proposals/preview')return reply({proposal:proposal(),text:wording,changeReason:reason,contentClass:'fixture-public-copy',variables:[]});
  23  |   if(path==='/content/proposals/context-preview')return options.contextualPreview?reply({screen:'LOGIN',draft:{proposal:proposal(),text:wording,changeReason:reason,contentClass:'fixture-public-copy',variables:[]},published:{locale:'en',termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:null,content:Object.fromEntries(Object.entries(userCopy).map(([key,text])=>[key,{key,locale:'en',variantRef:variant,versionRef:'fixture-published-login-v1',publicationRef:'fixture-published-login-p1',state:'PUBLISHED',text}]))}}):reply({error_code:'DEPENDENCY_UNAVAILABLE'},503);
  24  |   if(path==='/content/proposals/diff')return reply({proposal:proposal(),comparedVersionReference:versionId,beforeText:'Sign in to Tunner',afterText:wording,beforeDigest:'fixture-before',afterDigest:'fixture-after',changed:true});
  25  |   writes.push({path,body:body!});const key=String(body!.idempotencyKey);const previous=receipts.get(key);if(previous)return reply({...previous as object,replayed:true});
  26  |   if(path==='/content/proposals/update'||path==='/content/proposals'){wording=String(body!.text);reason=String(body!.changeReason);revision++;state='DRAFT';}else if(path.endsWith('/validate')){revision++;state='VALIDATED';}else if(path.endsWith('/approve')){revision++;state='APPROVED';}else state='PUBLISHED';
  27  |   const response={result:path.endsWith('/publish')||path==='/content/rollback'?publication():proposal(),operationId:'fixture-stable-operation',replayed:false};receipts.set(key,response);
  28  |   if(options.unknownOnce&&!lost){lost=true;return route.abort('failed');}return reply(response);
  29  |  });
  30  |  return {writes,get guards(){return guards;},get revision(){return revision;}};
  31  | }
  32  | async function select(page:Page,candidate=true){await page.goto('/admin/content');await page.getByLabel('Surface',{exact:true}).selectOption('fixture.identity.login');await page.getByRole('button',{name:'Sign-in introduction login.heading'}).click();await page.getByLabel('Context',{exact:true}).selectOption('fixture-login-en');await expect(page.getByLabel('Candidate',{exact:true})).toBeEnabled();if(candidate){await page.getByLabel('Candidate',{exact:true}).selectOption(proposalId);await expect(page.getByLabel('Wording',{exact:true})).toHaveValue('Welcome to Tunner');}}
  33  | async function review(page:Page){await page.getByRole('button',{name:'Preview & compare',exact:true}).click();await expect(page.getByRole('heading',{level:1,name:'Review & publish'})).toBeVisible();await expect(page.getByRole('button',{name:'Publish',exact:true})).toBeEnabled();}
  34  | for(const viewport of [{width:1440,height:1024},{width:390,height:844}])test(`guided workspace, review and confirmation at ${viewport.width}`,async({page},info)=>{
  35  |  await page.setViewportSize(viewport);const server=await fixture(page);await select(page);await page.screenshot({path:info.outputPath(`workspace-${viewport.width}.png`),fullPage:true});
  36  |  await review(page);await page.getByLabel('Compare with published version').selectOption(versionId);await expect(page.getByText('Sign in to Tunner',{exact:true})).toBeVisible();await page.screenshot({path:info.outputPath(`review-${viewport.width}.png`),fullPage:true});
  37  |  await page.getByRole('button',{name:'Publish',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.getByRole('button',{name:'Confirm',exact:true}).click();await expect(page.getByLabel('Effective from',{exact:true})).toBeFocused();await expect(page.locator(':invalid')).toHaveCount(0);
  38  |  await page.getByLabel('Effective from',{exact:true}).fill('2099-01-01T00:00');await page.getByLabel('Start boundary').selectOption('inclusive');await page.getByLabel('End condition').selectOption('none');await page.screenshot({path:info.outputPath(`confirmation-${viewport.width}.png`),fullPage:true});
  39  |  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  40  |  await dialog.getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('button',{name:'Publish',exact:true})).toBeFocused();expect(server.writes).toHaveLength(0);
  41  |  await page.getByRole('button',{name:'Publish',exact:true}).click();await page.getByLabel('Effective from',{exact:true}).fill('2099-01-01T00:00');await page.getByLabel('Start boundary').selectOption('exclusive');await page.getByLabel('End condition').selectOption('none');await page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(1);expect(server.writes[0]!.body).toMatchObject({proposalReference:proposalId,expectedRevision:1,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.000Z',includesFrom:false,until:null,includesUntil:false}});
  42  | });
  43  | test('draft validation focuses the shared invalid field and sends persisted reason',async({page})=>{const server=await fixture(page);await select(page,false);await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.getByLabel('Wording',{exact:true})).toBeFocused();expect(server.writes).toHaveLength(0);await page.getByLabel('Wording',{exact:true}).fill('A clearer introduction');await page.getByLabel('Change reason',{exact:true}).fill('Explain the entry point');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(1);expect(server.writes[0]!.body).toMatchObject({text:'A clearer introduction',changeReason:'Explain the entry point'});});
  44  | test('unknown response retries the identical caller key and reloads original receipt',async({page})=>{const server=await fixture(page,{unknownOnce:true});await select(page);await page.getByLabel('Wording',{exact:true}).fill('Revised introduction');await page.getByRole('button',{name:'Save draft',exact:true}).dblclick();await expect(page.getByRole('button',{name:'Retry same request'})).toBeVisible();expect(server.writes).toHaveLength(1);await page.getByRole('button',{name:'Retry same request'}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes).toHaveLength(2);expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);});
  45  | test('deep links re-guard without URL or storage disclosure and history restores exact source',async({page})=>{
  46  |  const server=await fixture(page);await page.goto('/admin/content/review?proposal=discard#discard');await expect(page).toHaveURL(/\/admin\/content\/review$/);await expect(page.getByText(labels.needsContext,{exact:true})).toHaveCount(2);
  47  |  await select(page);await review(page);const before=server.guards;await page.getByRole('link',{name:'Version history',exact:true}).click();await expect.poll(()=>server.guards).toBeGreaterThan(before);await page.goBack();await expect(page).toHaveURL(/\/admin\/content\/review$/);await expect(page.getByRole('heading',{level:1,name:'Review & publish'})).toBeVisible();await page.goForward();await expect(page).toHaveURL(/\/admin\/content\/history$/);await expect(page.getByRole('heading',{level:1,name:'Version history'})).toBeVisible();await page.getByRole('button',{name:'Restore this version'}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.getByLabel('Effective from',{exact:true}).fill('2099-01-01T00:00');await page.getByLabel('Start boundary').selectOption('inclusive');await page.getByLabel('End condition').selectOption('none');await page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(1);expect(server.writes[0]).toMatchObject({path:'/content/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId}});expect(page.url()).not.toContain(proposalId);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);await page.reload();await expect(page.getByText(labels.needsContext,{exact:true})).toBeVisible();
  48  | });
  49  | test('published proposal state never fabricates a missing server capability',async({page})=>{const server=await fixture(page,{publishAllowed:false});await select(page);await page.getByRole('button',{name:'Preview & compare',exact:true}).click();await expect(page.getByRole('button',{name:'Publish',exact:true})).toBeDisabled();expect(server.writes).toHaveLength(0);});
  50  | test('missing whole published chrome fails closed without User cookie APIs',async({page})=>{const requests:string[]=[];page.on('request',request=>requests.push(new URL(request.url()).pathname));await fixture(page,{missingPresentation:true});await page.goto('/admin/content');await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('heading')).toHaveCount(0);expect(requests.filter(path=>path.startsWith('/authentication'))).toEqual([]);});
  51  | test('current route and exact preview qualification gate keyboard writes while reads are delayed',async({page})=>{
  52  |  const server=await fixture(page);await select(page);await page.getByLabel('Wording',{exact:true}).fill('Preserved wording during qualification');await page.getByRole('link',{name:'Version history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] tbody tr')).toHaveCount(1);
  53  |  let releaseWorkspace!:()=>void,releasePreview!:()=>void,sawWorkspace!:()=>void,sawPreview!:()=>void;
  54  |  const workspaceGate=new Promise<void>(resolve=>{releaseWorkspace=resolve;}),previewGate=new Promise<void>(resolve=>{releasePreview=resolve;}),workspaceRequested=new Promise<void>(resolve=>{sawWorkspace=resolve;}),previewRequested=new Promise<void>(resolve=>{sawPreview=resolve;});
  55  |  await page.route('**/content/workspace',async route=>{sawWorkspace();await workspaceGate;await route.fallback();});
  56  |  await page.route('**/content/proposals/preview',async route=>{sawPreview();await previewGate;await route.fallback();});
  57  |  try{
  58  |   await page.getByRole('link',{name:'Content workspace',exact:true}).click();await workspaceRequested;await expect(page.locator('main')).toHaveAttribute('aria-busy','true');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);
  59  |   releaseWorkspace();await previewRequested;const save=page.getByRole('button',{name:'Save draft',exact:true});await expect(save).toBeDisabled();await expect(page.getByLabel('Wording',{exact:true})).toHaveValue('Preserved wording during qualification');await expect(page.locator('main')).toHaveAttribute('aria-busy','true');
  60  |   await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(server.writes).toHaveLength(0);
  61  |   releasePreview();await expect(save).toBeEnabled();await save.focus();await expect(save).toBeFocused();await page.keyboard.press('Enter');await expect.poll(()=>server.writes.length).toBe(1);expect(server.writes[0]).toMatchObject({path:'/content/proposals/update',body:{proposalReference:proposalId,expectedRevision:1,text:'Preserved wording during qualification'}});
  62  |  }finally{releaseWorkspace();releasePreview();}
  63  | });
  64  | 
  65  | for(const viewport of [{width:1440,height:1024},{width:390,height:844}])test(`readonly User context preview preserves draft and published provenance at ${viewport.width}`,async({page},info)=>{
  66  |  await page.setViewportSize(viewport);
  67  |  const requests:string[]=[];page.on('request',request=>requests.push(new URL(request.url()).pathname));
  68  |  const server=await fixture(page,{contextualPreview:true});await select(page);
  69  |  const preview=page.locator('.content-user-preview');await expect(preview).toBeVisible();
  70  |  await expect(preview).toHaveAttribute('data-preview-screen','LOGIN');
  71  |  await expect(preview.getByRole('heading',{level:1})).toHaveText('Welcome to Tunner');
  72  |  const draft=preview.locator('h1 [data-content-state="DRAFT"]');
  73  |  await expect(draft).toHaveAttribute('data-content-key','login.heading');
  74  |  await expect(draft).toHaveAttribute('data-proposal-reference',proposalId);
  75  |  await expect(draft).toHaveAttribute('data-proposal-revision','1');
  76  |  await expect(draft).not.toHaveAttribute('data-content-publication');
  77  |  await expect(draft).not.toHaveAttribute('data-content-version');
  78  |  await expect(preview.locator('[data-content-state="DRAFT"][data-content-publication]')).toHaveCount(0);
  79  |  const published=preview.locator('[data-content-key="login.description"]');
  80  |  await expect(published).toHaveText(userCopy['login.description']!);
  81  |  await expect(published).toHaveAttribute('data-content-publication','fixture-published-login-p1');
  82  |  await expect(published).toHaveAttribute('data-content-version','fixture-published-login-v1');
  83  |  await expect(preview.locator('[data-tunner-surface="user"][data-tunner-theme="light"]')).toBeVisible();
  84  |  await expect(preview.locator('.authentication-card')).toHaveCSS('border-radius','16px');
  85  |  await expect(preview.getByRole('button',{name:'Sign in',exact:true})).toHaveCSS('background-color','rgb(79, 70, 229)');
  86  |  await expect(preview.locator('.authentication-glow.indigo')).toHaveAttribute('src',/login-bg-indigo\.svg(?:\?|$)/);
  87  |  await expect(preview.locator('.authentication-glow.violet')).toHaveAttribute('src',/login-bg-violet\.svg(?:\?|$)/);
  88  |  await expect(preview.locator('.authentication-providers img')).toHaveAttribute('src',/login-google\.svg(?:\?|$)/);
  89  |  await expect(preview.locator('.authentication-reveal img')).toHaveAttribute('src',/login-eye-off\.svg(?:\?|$)/);
  90  |  await expect.poll(()=>preview.locator('img').evaluateAll(images=>images.every(image=>image instanceof HTMLImageElement&&image.complete&&image.naturalWidth>0))).toBe(true);
  91  |  await expect(preview.locator('button:not(:disabled),input:not(:disabled),a[href],input[name],input[type="password"],input[autocomplete="username"],input[autocomplete="current-password"],style')).toHaveCount(0);
  92  |  expect(await preview.locator('input').evaluateAll(inputs=>inputs.every(input=>input instanceof HTMLInputElement&&input.readOnly&&input.autocomplete==='off'))).toBe(true);
  93  |  await page.getByRole('button',{name:'Preview & compare',exact:true}).focus();await page.keyboard.press('Tab');
  94  |  expect(await preview.evaluate(element=>element.contains(document.activeElement))).toBe(false);
  95  |  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  96  |  const bounds=await preview.locator('.authentication-card').boundingBox();expect(bounds).not.toBeNull();expect(bounds!.width).toBeLessThanOrEqual(Math.min(440,viewport.width));
  97  |  await preview.screenshot({path:info.outputPath(`user-preview-${viewport.width}.png`)});
  98  |  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  99  |  expect(server.writes).toHaveLength(0);
  100 |  expect(requests.filter(path=>/^\/(authentication|registration|federation|passkeys|totp|step-up)(?:\/|$)/.test(path))).toEqual([]);
  101 |  expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  102 | });
  103 | 
  104 | test('responsive Content workspace and dialog support tablet laptop and narrow reflow without writes',async({page},info)=>{
  105 |  for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}]){
  106 |   await test.step(`${viewport.width}x${viewport.height}`,async()=>{
  107 |    try{
  108 |     await page.goto('about:blank');await page.unroute('**/content/**');await page.setViewportSize(viewport);
  109 |     const server=await fixture(page,{contextualPreview:true});await select(page);
  110 |     const preview=page.locator('.content-user-preview');await expect(preview).toBeVisible();
  111 |     await expect(preview.getByRole('heading',{level:1})).toHaveText('Welcome to Tunner');
  112 |     await expect.poll(()=>preview.locator('img').evaluateAll(images=>images.every(image=>image instanceof HTMLImageElement&&image.complete&&image.naturalWidth>0))).toBe(true);
  113 |     expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  114 |     const containment=await preview.locator('.authentication-card').evaluate(card=>{
  115 |      const bounds=card.getBoundingClientRect();return [...card.querySelectorAll('input,button,a')].map(control=>{
  116 |       const box=control.getBoundingClientRect();return {tag:control.tagName,className:control.className,left:box.left,right:box.right,cardLeft:bounds.left,cardRight:bounds.right};
  117 |      }).filter(box=>box.left<box.cardLeft-1||box.right>box.cardRight+1);
  118 |     });
> 119 |     expect(containment).toEqual([]);
      |                         ^ Error: expect(received).toEqual(expected) // deep equality
  120 |     await expect(preview.locator('button:not(:disabled),input:not(:disabled),a[href]')).toHaveCount(0);
  121 |     const wording=page.getByLabel('Wording',{exact:true});await wording.fill('');
  122 |     await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');
  123 |     await expect(wording).toBeFocused();await expect(wording).toHaveAttribute('aria-invalid','true');
  124 |     await wording.fill('Welcome to Tunner');await wording.press('Tab');await expect(wording).not.toHaveAttribute('aria-invalid','true');
  125 |     expect(server.writes).toHaveLength(0);
  126 |     // Restore the exact saved candidate; correcting a local draft never authorizes publication.
  127 |     await select(page);await expect(page.locator('.content-user-preview')).toBeVisible();
  128 |     if(viewport.width===768)await page.screenshot({path:info.outputPath('tablet-workspace-768.png'),fullPage:true});
  129 |     expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  130 |     await review(page);const publish=page.getByRole('button',{name:'Publish',exact:true});await publish.focus();await page.keyboard.press('Enter');
  131 |     const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();
  132 |     await dialog.getByRole('button',{name:'Confirm',exact:true}).focus();await page.keyboard.press('Enter');
  133 |     const from=dialog.getByLabel('Effective from',{exact:true});await expect(from).toBeFocused();await expect(from).toHaveAttribute('aria-invalid','true');
  134 |     await from.fill('2099-01-01T00:00');await from.press('Tab');await dialog.getByLabel('Start boundary').selectOption('inclusive');await dialog.getByLabel('End condition').selectOption('none');
  135 |     await expect(dialog.locator('[aria-invalid="true"]')).toHaveCount(0);await expect(page.locator(':invalid')).toHaveCount(0);
  136 |     const bounds=await dialog.boundingBox();expect(bounds).not.toBeNull();
  137 |     expect(bounds!.x).toBeGreaterThanOrEqual(0);expect(bounds!.y).toBeGreaterThanOrEqual(0);
  138 |     expect(bounds!.x+bounds!.width).toBeLessThanOrEqual(viewport.width+1);expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(viewport.height+1);
  139 |     expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  140 |     if(viewport.width===768)await page.screenshot({path:info.outputPath('tablet-dialog-768.png'),fullPage:true});
  141 |     expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  142 |     await dialog.getByRole('button',{name:'Cancel',exact:true}).focus();await page.keyboard.press('Enter');
  143 |     await expect(dialog).not.toBeVisible();await expect(publish).toBeFocused();expect(server.writes).toHaveLength(0);
  144 |    }catch(error){
  145 |     if(viewport.width!==768)await page.screenshot({path:info.outputPath(`responsive-failure-${viewport.width}.png`),fullPage:true});
  146 |     throw error;
  147 |    }
  148 |   });
  149 |  }
  150 | });
  151 | 
```