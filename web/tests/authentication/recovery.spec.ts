import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import type {Page} from '@playwright/test';
import authentication from '../../apps/user/authentication.content.proposal.json' with {type:'json'};
import recovery from '../../apps/user/recovery.content.proposal.json' with {type:'json'};

const requestId='12121212-3434-4567-8989-121212121212',otherId='23232323-4545-4678-8989-232323232323';
const goal='fixture-goal';
function original(id=requestId,status='AWAITING_PROOF'){
 return {requestId:id,status,version:status==='CANCELLED'||status==='PASSWORD_REPAIRED_NO_SESSION'?2:1,goalReference:goal,canCancel:status==='AWAITING_PROOF',canSubmitRepair:status==='AWAITING_PROOF',codeSubmission:'ATOMIC_REPAIR_ONLY',authenticated:false,...(status==='PASSWORD_REPAIRED_NO_SESSION'?{result:{outcome:'PASSWORD_REPAIRED_NO_SESSION',effects:[{kind:'TUNNER_SESSION_INVALIDATION',labelKey:'fixture.effect.label',valueKey:'fixture.effect.value'}],followUps:[]}}:{})};
}
async function fixture(page:Page,initial:ReturnType<typeof original>|undefined=undefined){
 const copy={...authentication.content,...recovery.content,'fixture.goal.label':'Fixture offered recovery goal','fixture.goal.description':'Fixture admitted request','fixture.effect.label':'Fixture effect','fixture.effect.value':'Fixture confirmed effect'};
 const content:Record<string,unknown>=Object.fromEntries(Object.entries(copy).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-publication'}]));
 const state={operation:initial,available:true,missingPublication:false,showPointer:true,starts:0,results:0,cancels:0,repairs:0,options:0,csrf:0,resultFailure:false,resultRejected:false,resultMismatch:false,startLost:false,holdCancel:undefined as Promise<void>|undefined,holdStart:undefined as Promise<void>|undefined};
 await page.route('**/authentication/options',route=>{state.csrf++;return route.fulfill({json:{csrfToken:'fixture-csrf-'+state.csrf,idempotencyKey:requestId,passwordMaximumLength:128,locale:'en',content,termsUri:'#fixture-terms',privacyUri:'#fixture-privacy',supportUri:null,...(state.available?{recoveryAvailable:true}:{})}})});
 await page.route('**/recovery/*',async route=>{
  if(route.request().isNavigationRequest())return route.continue();
  const path=new URL(route.request().url()).pathname;
  if(path==='/recovery/options'){
   state.options++;const publications={...content};if(state.missingPublication)delete publications['fixture.goal.label'];
   return route.fulfill({json:{available:true,profileVersion:'fixture-profile-v1',publicationVersion:'fixture-publication-v1',locale:'en',content:publications,goals:[{reference:goal,labelKey:'fixture.goal.label',descriptionKey:'fixture.goal.description'}],repair:{available:true,passwordMinimumLength:12,passwordMaximumLength:128,sessionEffect:'InvalidateExistingTunnerSessions',effectLabelKey:'fixture.effect.label',effectValueKey:'fixture.effect.value'},...(state.showPointer&&state.operation?{operation:state.operation}:{})}});
  }
  expect(route.request().method()).toBe('POST');expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-csrf-'+state.csrf);
  const body=route.request().postDataJSON() as Record<string,unknown>;
  if(path==='/recovery/start'){
   state.starts++;expect(Object.keys(body).sort()).toEqual(['email','goalReference','requestId']);expect(body.goalReference).toBe(goal);expect(body.email).toBe('person@example.test');expect(typeof body.requestId).toBe('string');
   state.operation=original(body.requestId as string);if(state.holdStart)await state.holdStart;
   if(state.startLost)return route.abort('connectionreset');
   return route.fulfill({json:{operation:state.operation}}).catch(()=>{});
  }
  if(path==='/recovery/result'){
   state.results++;expect(body).toEqual({requestId:state.operation?.requestId});
   if(state.resultFailure)return route.abort('connectionreset');
   if(state.resultRejected)return route.fulfill({status:400,json:{error_code:'RECOVERY_PROOF_REJECTED',detail:'synthetic-private-rejection-detail',account_exists:true}});
   return route.fulfill({json:{operation:state.resultMismatch?{...state.operation,requestId:otherId}:state.operation}});
  }
  if(path==='/recovery/cancel'){
   state.cancels++;expect(body).toEqual({requestId:state.operation?.requestId});if(state.holdCancel)await state.holdCancel;
   state.operation=original(state.operation?.requestId,'CANCELLED');return route.fulfill({json:{operation:state.operation}});
  }
  if(path==='/recovery/repair')state.repairs++;
  return route.fulfill({status:404,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}});
 });
 return state;
}
async function start(page:Page){await page.goto('/recovery');await page.getByLabel('Email address',{exact:true}).fill('person@example.test');await page.getByRole('radio',{name:/Fixture offered recovery goal/}).click();await page.getByRole('button',{name:'Continue',exact:true}).click();}
async function assertUnavailableProof(page:Page){await expect(page).toHaveURL(/\/recovery\/verify$/);await expect(page.getByText(recovery.content['recovery.entry.optionsUnavailable'],{exact:true})).toBeVisible();await expect(page.getByLabel('Recovery code',{exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Verify recovery code',exact:true})).toHaveCount(0);}

test('Default Login retains disabled recovery and does not fetch an unadvertised workflow',async({page})=>{
 const state=await fixture(page);state.available=false;await page.goto('/login');await expect(page.getByRole('button',{name:'Forgot password?',exact:true})).toBeDisabled();expect(state.options).toBe(0);expect(state.starts).toBe(0);
});
test('Advertised Login opens clean entry; shared validation precedes exact neutral Start without a proof claim',async({page})=>{
 const state=await fixture(page);await page.goto('/login');await page.getByRole('button',{name:'Forgot password?',exact:true}).click();await expect(page).toHaveURL(/\/recovery$/);
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByLabel('Email address',{exact:true})).toBeFocused();expect(state.starts).toBe(0);
 await page.getByLabel('Email address',{exact:true}).fill('person@example.test');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('radio')).toBeFocused();await expect(page.getByRole('radio')).toHaveAttribute('aria-checked','false');
 await page.getByRole('radio').click();await page.getByRole('button',{name:'Continue',exact:true}).click();await assertUnavailableProof(page);expect(state.starts).toBe(1);expect(state.repairs).toBe(0);await expect(page.getByRole('button',{name:'Cancel request',exact:true})).toBeEnabled();expect(new URL(page.url()).search).toBe('');
});
test('Entry format errors appear on blur, focus the invalid email on submit and clear on correction',async({page})=>{
 const state=await fixture(page);await page.goto('/recovery');const email=page.getByLabel('Email address',{exact:true});
 await expect(page.locator('form')).toHaveAttribute('novalidate','');await email.fill('person');await email.press('Tab');
 await expect(email).toHaveAttribute('aria-invalid','true');await expect(page.getByText(recovery.content['recovery.error.email'],{exact:true})).toBeVisible();
 const describedBy=await email.getAttribute('aria-describedby');expect(describedBy).toBeTruthy();expect(await email.evaluate(input=>(input.getAttribute('aria-describedby')??'').split(/\s+/).some(id=>document.getElementById(id)?.textContent==='Enter a valid email address.'))).toBe(true);
 await page.getByRole('radio').click();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(email).toBeFocused();await expect(page.getByRole('alert')).toHaveText(recovery.content['recovery.error.highlighted']);expect(state.starts).toBe(0);
 await email.fill('person@example.test');await expect(email).not.toHaveAttribute('aria-invalid','true');await expect(page.getByText(recovery.content['recovery.error.email'],{exact:true})).toHaveCount(0);await expect(page.getByRole('alert')).toHaveCount(0);await expect(page.getByRole('radio')).toHaveAttribute('aria-checked','true');
 await page.getByRole('button',{name:'Continue',exact:true}).click();await assertUnavailableProof(page);expect(state.starts).toBe(1);expect(state.repairs).toBe(0);
});
test('Synthetic HTTP400 maps to the governed generic error without exposing problem details or replaying a mutation',async({page})=>{
 const state=await fixture(page,original());await page.goto('/recovery/verify');await assertUnavailableProof(page);state.showPointer=false;state.resultRejected=true;
 await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await expect(page).toHaveURL(/\/recovery\/result$/);await expect(page.getByRole('heading',{name:recovery.content['recovery.unknown.heading'],exact:true})).toBeVisible();await expect(page.getByRole('alert')).toHaveText(recovery.content['recovery.error.unavailable']);
 await expect(page.locator('body')).not.toContainText('synthetic-private-rejection-detail');await expect(page.locator('body')).not.toContainText('RECOVERY_PROOF_REJECTED');await expect(page.locator('body')).not.toContainText('account_exists');await expect(page.getByText('Fixture confirmed effect',{exact:true})).toHaveCount(0);expect(state.starts+state.cancels+state.repairs).toBe(0);
 state.resultRejected=false;await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await assertUnavailableProof(page);await expect(page.getByRole('alert')).toHaveCount(0);expect(state.operation!.requestId).toBe(requestId);expect(state.starts+state.cancels+state.repairs).toBe(0);
});
test('Guarded direct proof/result entry without an exact original returns neutral entry without a mutation',async({page})=>{
 const state=await fixture(page);for(const pathname of ['/recovery/verify','/recovery/result']){await page.goto(pathname);await expect(page).toHaveURL(/\/recovery$/);await expect(page.getByRole('heading',{name:'Recover your account',exact:true})).toBeVisible();}expect(state.starts+state.results+state.cancels+state.repairs).toBe(0);
});
test('Protected original survives reload and Back/Forward with server stage guards and no mutation replay',async({page})=>{
 const state=await fixture(page);await start(page);await assertUnavailableProof(page);const retained=state.operation!.requestId;await page.reload();await assertUnavailableProof(page);expect(state.operation!.requestId).toBe(retained);
 state.operation=original(retained,'CANCELLED');await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await expect(page).toHaveURL(/\/recovery\/result$/);await expect(page.getByRole('heading',{name:'Recovery request cancelled',exact:true})).toBeVisible();
 await page.goBack();await expect(page).toHaveURL(/\/recovery\/result$/);await page.goForward();await expect(page.getByRole('heading',{name:'Recovery request cancelled',exact:true})).toBeVisible();expect(state.starts).toBe(1);expect(state.cancels+state.repairs).toBe(0);
});
test('Lost Start keeps the same original UNKNOWN; only explicit read-only status recovery follows',async({page})=>{
 const state=await fixture(page);state.startLost=true;state.showPointer=false;state.resultFailure=true;await start(page);await expect(page.getByRole('heading',{name:'Check your recovery result',exact:true})).toBeVisible();expect(state.starts).toBe(1);const retained=state.operation!.requestId;
 state.resultFailure=false;await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await assertUnavailableProof(page);expect(state.operation!.requestId).toBe(retained);expect(state.starts).toBe(1);expect(state.repairs).toBe(0);
});
test('Cancellation uses only the exact request and synchronous duplicate/status actions remain held',async({page})=>{
 const state=await fixture(page,original());let release!:()=>void;state.holdCancel=new Promise<void>(resolve=>{release=resolve;});await page.goto('/recovery/verify');await assertUnavailableProof(page);
 await page.getByRole('button',{name:'Cancel request',exact:true}).evaluate((button:HTMLButtonElement)=>{button.click();button.click();});await expect.poll(()=>state.cancels).toBe(1);await expect(page.getByRole('button',{name:'Check recovery status',exact:true})).toBeDisabled();release();await expect(page.getByRole('heading',{name:'Recovery request cancelled',exact:true})).toBeVisible();expect(state.cancels).toBe(1);expect(state.repairs).toBe(0);
});
test('A different original in result cannot replace the retained request or manufacture completion',async({page})=>{
 const state=await fixture(page,original());await page.goto('/recovery/verify');await assertUnavailableProof(page);state.resultMismatch=true;state.showPointer=false;await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your recovery result',exact:true})).toBeVisible();await expect(page.getByText('Fixture confirmed effect',{exact:true})).toHaveCount(0);
 state.resultMismatch=false;await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await assertUnavailableProof(page);expect(state.operation!.requestId).toBe(requestId);expect(state.starts+state.cancels+state.repairs).toBe(0);
});
test('Missing actual goal publication or absent global availability keeps direct entry unavailable',async({page})=>{
 const state=await fixture(page);state.missingPublication=true;await page.goto('/recovery');await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('form')).toHaveCount(0);await expect(page.getByRole('button',{name:'Continue',exact:true})).toHaveCount(0);state.missingPublication=false;state.available=false;await page.reload();await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('form')).toHaveCount(0);expect(state.starts).toBe(0);
});
test('Actual password result exposes only supplied effects without automatic sign-in or enabled Finish',async({page})=>{
 const state=await fixture(page,original(requestId,'PASSWORD_REPAIRED_NO_SESSION'));await page.goto('/recovery/result');await expect(page.getByRole('heading',{name:'Recovery result',exact:true})).toBeVisible();await expect(page.getByText('Fixture confirmed effect',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Finish',exact:true})).toBeDisabled();await expect(page.getByText(recovery.content['recovery.result.boundary'],{exact:true})).toBeVisible();await expect(page).toHaveURL(/\/recovery\/result$/);expect(state.starts+state.cancels+state.repairs).toBe(0);expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('Leaving during Start fences late response; returning reacquires only the server original',async({page})=>{
 const state=await fixture(page);let release!:()=>void;state.holdStart=new Promise<void>(resolve=>{release=resolve;});await start(page);await expect.poll(()=>state.starts).toBe(1);await page.goto('/login');release();await expect(page.getByRole('heading',{name:authentication.content['login.heading'],exact:true})).toBeVisible();await expect(page).toHaveURL(/\/login$/);await page.goto('/recovery');await assertUnavailableProof(page);expect(state.starts).toBe(1);expect(state.repairs).toBe(0);
});

test('Recovery approved routes support keyboard activation and accessible tablet narrow reflow',async({page})=>{
 test.setTimeout(60000);
 for(const width of [768,320]){
  await page.setViewportSize({width,height:1024});const state=await fixture(page);
  const accessible=async()=>{
   await page.evaluate(()=>document.fonts.ready);
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
   const card=await page.locator('.recovery-card').boundingBox();expect(card).not.toBeNull();expect(card!.x).toBeGreaterThanOrEqual(0);expect(card!.x+card!.width).toBeLessThanOrEqual(width);
   expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  };
  await page.goto('/recovery');await expect(page.getByRole('heading',{name:'Recover your account',exact:true})).toBeFocused();
  await page.keyboard.press('Tab');const email=page.getByLabel('Email address',{exact:true});await expect(email).toBeFocused();
  await page.keyboard.press('Enter');await expect(email).toBeFocused();await expect(email).toHaveAttribute('aria-invalid','true');expect(state.starts).toBe(0);
  await expect(page.getByRole('alert')).toHaveText(recovery.content['recovery.error.highlighted']);await accessible();
  await email.fill('person@example.test');await expect(email).not.toHaveAttribute('aria-invalid','true');
  await page.keyboard.press('Tab');const radio=page.getByRole('radio');await expect(radio).toBeFocused();await expect(radio).toHaveAttribute('aria-checked','false');
  await page.keyboard.press('Space');await expect(radio).toHaveAttribute('aria-checked','true');
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeFocused();await page.keyboard.press('Enter');
  await assertUnavailableProof(page);await expect(page.getByRole('heading',{name:'Verify this recovery request',exact:true})).toBeFocused();
  expect(state.starts).toBe(1);const retained=state.operation!.requestId;await accessible();
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Check recovery status',exact:true})).toBeFocused();
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Cancel request',exact:true})).toBeFocused();await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/recovery\/result$/);const resultHeading=page.getByRole('heading',{name:'Recovery request cancelled',exact:true});await expect(resultHeading).toBeFocused();
  expect(state.cancels).toBe(1);expect(state.operation!.requestId).toBe(retained);await accessible();
  await page.keyboard.press('Tab');const check=page.getByRole('button',{name:'Check recovery status',exact:true});await expect(check).toBeFocused();await page.keyboard.press('Enter');
  await expect.poll(()=>state.results).toBe(1);await expect(check).toBeEnabled();
  await resultHeading.focus();await page.keyboard.press('Tab');await expect(check).toBeFocused();await page.keyboard.press('Tab');
  await expect(page.getByRole('button',{name:'Back to sign in',exact:true})).toBeFocused();await page.keyboard.press('Enter');await expect(page).toHaveURL(/\/login$/);
  expect({starts:state.starts,cancels:state.cancels,results:state.results,repairs:state.repairs}).toEqual({starts:1,cancels:1,results:1,repairs:0});
  expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash}))).toEqual({local:0,session:0,search:'',hash:''});
 }
});
