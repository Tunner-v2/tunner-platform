import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import type {Page} from '@playwright/test';
import type {RecoveryViewModel} from '../../apps/user/recovery';
import type {RecoveryFixtureSnapshot} from '../../fixtures/recovery-case/main';

const goal={reference:'offered-A',labelKey:'fixture.goal.a'},other={reference:'offered-B',labelKey:'fixture.goal.b'};
const entry:RecoveryViewModel={kind:'entry',goals:[goal,other],canStart:true,canBackToSignIn:true};
const code:RecoveryViewModel={kind:'recovery-code',goal,canVerify:true,canCheck:true,canCancel:true};
const repair:RecoveryViewModel={kind:'password-repair',goal,canRepair:true,passwordMinimumLength:3,passwordMaximumLength:6,canCheck:true,canCancel:true,canBackToSignIn:true};
async function setup(page:Page,model:RecoveryViewModel,options:{holdCode?:boolean;holdRepair?:boolean;missingKey?:string;missingRepairCallback?:boolean}={}){
 await page.goto('/registration/');await page.waitForFunction(()=>!!window.recoveryPresentationFixture);await page.evaluate(({model,options})=>window.recoveryPresentationFixture.configure(model,options),{model,options});await expect(page.locator('.recovery-card')).toBeVisible();
}
async function snapshot(page:Page):Promise<RecoveryFixtureSnapshot>{return page.evaluate(()=>window.recoveryPresentationFixture.snapshot());}
async function noRestorationClaim(page:Page){await expect(page.getByRole('heading',{name:'Account recovered',exact:true})).toHaveCount(0);await expect(page.getByText(/^(?:Your account (has been|is) recovered|You are signed in|(?:Sign-in )?Access has been restored)[.!]?$/i)).toHaveCount(0);}

test('Empty entry focuses real email input, shares field/summary errors and clears them on correction',async({page})=>{
 await setup(page,entry);await expect(page.getByRole('radio',{checked:true})).toHaveCount(0);await expect(page.getByText('Fixture unoffered goal',{exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Continue',exact:true}).click();const email=page.getByLabel('Email address',{exact:true});await expect(email).toBeFocused();await expect(email).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText('Check the highlighted details');expect((await snapshot(page)).calls.start).toBe(0);
 await email.fill('person@example.test');await page.getByRole('radio',{name:/Fixture offered goal B/}).click();await expect(email).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.start).toBe(1);expect((await snapshot(page)).chosenGoal).toBe('offered-B');
});

test('Password repair empty submit, blur, mismatch and supplied bounds share correction and first-invalid focus',async({page})=>{
 await setup(page,repair);const password=page.getByLabel('New password',{exact:true}),confirmation=page.getByLabel('Confirm password',{exact:true}),save=page.getByRole('button',{name:'Save new password',exact:true});
 await expect(page.getByText('Use 3 to 6 characters for this request.',{exact:true})).toBeVisible();await expect(page.locator('form')).toHaveAttribute('novalidate','');
 await save.click();await expect(password).toBeFocused();await expect(password).toHaveAttribute('aria-invalid','true');await expect(confirmation).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText('Check the highlighted details');expect((await snapshot(page)).calls.repair).toBe(0);
 expect(await password.evaluate(field=>(field.getAttribute('aria-describedby')??'').split(/\s+/).some(id=>document.getElementById(id)?.textContent==='Enter a new password.'))).toBe(true);
 expect(await password.evaluate(field=>(field.getAttribute('aria-describedby')??'').split(/\s+/).some(id=>document.getElementById(id)?.textContent==='Use 3 to 6 characters for this request.'))).toBe(true);
 await password.fill('abc');await confirmation.fill('abd');await save.click();await expect(confirmation).toBeFocused();await expect(page.getByText('The passwords do not match.',{exact:true})).toBeVisible();
 await confirmation.fill('abc');await expect(page.getByRole('alert')).toHaveCount(0);await expect(confirmation).not.toHaveAttribute('aria-invalid','true');
 for(const value of ['ab','abcdefg']){await password.fill(value);await confirmation.fill(value);await save.click();await expect(password).toBeFocused();await expect(page.getByText('Use a password within the length requirements shown for this request.',{exact:true})).toBeVisible();expect((await snapshot(page)).calls.repair).toBe(0);}
 await password.fill('😀😀😀');await confirmation.fill('😀😀😀');await expect(page.getByRole('alert')).toHaveCount(0);await save.click();await expect.poll(async()=>(await snapshot(page)).calls.repair).toBe(1);await expect(page.locator('input')).toHaveCount(0);await expect(page.getByRole('heading',{name:'Password updated',exact:true})).toBeVisible();expect((await snapshot(page)).passwordArgumentsMatch).toBe(true);expect((await snapshot(page)).passwordDomEmptyAtCallback).toBe(true);
 await setup(page,repair);await page.getByLabel('New password',{exact:true}).focus();await page.keyboard.press('Tab');await expect(page.getByLabel('New password',{exact:true})).toHaveAttribute('aria-invalid','true');await expect(page.getByText('Enter a new password.',{exact:true})).toBeVisible();await noRestorationClaim(page);
});

test('Password repair clears both DOM secrets before callback and prevents pending duplicate actions',async({page})=>{
 await setup(page,repair,{holdRepair:true});await page.getByLabel('New password',{exact:true}).fill('abc');await page.getByLabel('Confirm password',{exact:true}).fill('abc');
 await page.locator('form').evaluate((form:HTMLFormElement)=>{form.requestSubmit();form.requestSubmit();});await expect.poll(async()=>(await snapshot(page)).calls.repair).toBe(1);const observed=await snapshot(page);expect(observed.passwordArgumentsMatch).toBe(true);expect(observed.passwordDomEmptyAtCallback).toBe(true);
 await expect(page.locator('input')).toHaveCount(0);await expect(page.getByRole('heading',{name:'Updating your password',exact:true})).toBeVisible();
 for(const name of ['Save new password','Check recovery status'])await expect(page.getByRole('button',{name,exact:true})).toBeDisabled();for(const name of ['Cancel request','Back to sign in'])await expect(page.getByRole('button',{name,exact:true})).toHaveCount(0);
 await expect(page.getByRole('status')).toContainText('No completed password update is confirmed yet.');await expect(page.getByRole('alert')).toHaveCount(0);await page.evaluate(()=>window.recoveryPresentationFixture.releaseRepair());await expect(page.getByRole('heading',{name:'Password updated',exact:true})).toBeFocused();const viewResult=page.getByRole('button',{name:'View recovery result',exact:true});await expect(viewResult).toBeEnabled();await expect(page.getByRole('button')).toHaveCount(1);await viewResult.click();expect((await snapshot(page)).calls.viewResult).toBe(1);expect((await snapshot(page)).calls.repair).toBe(1);await noRestorationClaim(page);
});

test('Password repair clears on context replacement, pagehide and unknown-state unmount without persistence',async({page})=>{
 await setup(page,repair);const password=page.getByLabel('New password',{exact:true}),confirmation=page.getByLabel('Confirm password',{exact:true});
 await password.fill('abc');await confirmation.fill('abc');const previousPassword=await password.elementHandle(),previousConfirmation=await confirmation.elementHandle();await page.evaluate(()=>window.recoveryPresentationFixture.replaceContext());await expect(password).toHaveValue('');await expect(confirmation).toHaveValue('');
 for(const field of [previousPassword,previousConfirmation])expect(await field!.evaluate(element=>(element as HTMLInputElement).value)).toBe('');
 await password.fill('abc');await confirmation.fill('abc');expect(await page.evaluate(()=>{const fields=[...document.querySelectorAll<HTMLInputElement>('input[type=password]')];dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true}));return fields.every(field=>field.value==='');})).toBe(true);await expect(password).toHaveValue('');await expect(confirmation).toHaveValue('');await page.evaluate(()=>dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true})));
 await password.fill('abc');await confirmation.fill('abc');const detached=await password.elementHandle();await page.evaluate(model=>window.recoveryPresentationFixture.configure(model),{kind:'password-repair-status',state:'unknown',goal,canCheck:true,canCancel:false,canBackToSignIn:true,canViewResult:false} as RecoveryViewModel);await expect(page.getByRole('heading',{name:'Check your password update',exact:true})).toBeVisible();await expect(page.locator('input')).toHaveCount(0);expect(await detached!.evaluate(element=>(element as HTMLInputElement).value)).toBe('');
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash}))).toEqual({local:0,session:0,search:'',hash:''});expect((await snapshot(page)).calls.repair).toBe(0);await noRestorationClaim(page);
});

test('Password repair missing callback, bounds or publication fail closed with no secret fields or actions',async({page})=>{
 await setup(page,repair);
 for(const options of [{missingRepairCallback:true},{missingKey:'recovery.repair.requirements'},{missingKey:'recovery.error.passwordConfirmation'}]){await page.evaluate(({model,options})=>window.recoveryPresentationFixture.configure(model,options),{model:repair,options});await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('input,button')).toHaveCount(0);}
 await page.evaluate(model=>window.recoveryPresentationFixture.configure(model),{...repair,passwordMinimumLength:7,passwordMaximumLength:6});await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('input,button')).toHaveCount(0);
});

test('Password repair contact proof applies server digit format with shared errors and leading zeros',async({page},testInfo)=>{
 await page.setViewportSize({width:320,height:900});
 await setup(page,{...code,purpose:'contact',proofDigits:6,submitLabelKey:'recovery.action.continue'});const input=page.getByLabel('Verification code',{exact:true});await expect(page.getByRole('heading',{name:'Enter your verification code',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Verify recovery code',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(input).toBeFocused();await expect(page.getByText('Enter a verification code.',{exact:true})).toBeVisible();
 await input.evaluate(field=>field.removeAttribute('required'));for(const value of ['12a456','01234','0123456','１２３４５６',' 012345','012345 ']){await input.fill(value);await page.locator('form').evaluate((form:HTMLFormElement)=>form.requestSubmit());await expect(input).toBeFocused();await expect(page.getByText('Enter the verification code exactly as provided.',{exact:true})).toBeVisible();expect((await snapshot(page)).calls.code).toBe(0);}
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);await page.screenshot({path:testInfo.outputPath('contact-format-error-320.png'),fullPage:true});
 await input.fill('012345');await expect(input).not.toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.code).toBe(1);await expect(input).toHaveValue('');expect((await snapshot(page)).codeDomEmptyAtCallback).toBe(true);await noRestorationClaim(page);
});

test('Password repair presentation reflows accessibly and captures normal/error/pending/unknown/success at four widths',async({page},testInfo)=>{
 test.setTimeout(120000);
 for(const width of [1440,768,390,320]){
  await page.setViewportSize({width,height:1024});await setup(page,repair,{holdRepair:true});await page.evaluate(()=>document.fonts.ready);
  const capture=async(state:string)=>{const height=width===390&&(state==='normal'||state==='pending')?900:width===390&&state==='error'?1050:width===320&&state==='unknown'?900:1024;await page.setViewportSize({width,height});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);const card=await page.locator('.recovery-card').boundingBox();expect(card).not.toBeNull();expect(card!.x).toBeGreaterThanOrEqual(0);expect(card!.x+card!.width).toBeLessThanOrEqual(width);await page.screenshot({path:testInfo.outputPath('password-repair-'+state+'-'+width+'.png'),fullPage:true});};
  await capture('normal');await page.getByRole('button',{name:'Save new password',exact:true}).click();await expect(page.getByLabel('New password',{exact:true})).toBeFocused();await capture('error');expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  await page.getByLabel('New password',{exact:true}).fill('abc');await page.getByLabel('Confirm password',{exact:true}).fill('abc');await page.getByRole('button',{name:'Save new password',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.repair).toBe(1);await expect(page.locator('input')).toHaveCount(0);await expect(page.getByRole('heading',{name:'Updating your password',exact:true})).toBeVisible();await capture('pending');await page.evaluate(()=>window.recoveryPresentationFixture.releaseRepair());await expect(page.getByRole('heading',{name:'Password updated',exact:true})).toBeVisible();await capture('success');
  await page.evaluate(model=>window.recoveryPresentationFixture.configure(model),{kind:'password-repair-status',state:'unknown',goal,canCheck:true,canCancel:false,canBackToSignIn:true,canViewResult:false} as RecoveryViewModel);await expect(page.locator('input')).toHaveCount(0);await expect(page.getByRole('heading',{name:'Check your password update',exact:true})).toBeVisible();await capture('unknown');await noRestorationClaim(page);
 }
});
test('Valid email without a goal focuses the actual first offered radio and never invents selection',async({page})=>{
 await setup(page,entry);await page.getByLabel('Email address',{exact:true}).fill('person@example.test');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('radio',{name:/Fixture offered goal A/})).toBeFocused();await expect(page.getByRole('radio',{checked:true})).toHaveCount(0);await expect(page.getByText('Select the help you need.',{exact:true})).toBeVisible();expect((await snapshot(page)).calls.start).toBe(0);
});
test('Presented code form focuses its empty field and clears required errors on correction without claiming public proof',async({page})=>{
 await setup(page,code);const input=page.getByLabel('Recovery code',{exact:true});await expect(page.locator('form')).toHaveAttribute('novalidate','');
 await page.getByRole('button',{name:'Verify recovery code',exact:true}).click();await expect(input).toBeFocused();await expect(input).toHaveAttribute('aria-invalid','true');await expect(page.getByText('Enter a recovery code.',{exact:true})).toBeVisible();await expect(page.getByRole('alert')).toHaveText('Check the highlighted details');expect((await snapshot(page)).calls.code).toBe(0);
 expect(await input.evaluate(field=>(field.getAttribute('aria-describedby')??'').split(/\s+/).some(id=>document.getElementById(id)?.textContent==='Enter a recovery code.'))).toBe(true);
 await input.fill('public-required-field-value');await expect(input).not.toHaveAttribute('aria-invalid','true');await expect(page.getByText('Enter a recovery code.',{exact:true})).toHaveCount(0);await expect(page.getByRole('alert')).toHaveCount(0);
 await page.getByRole('button',{name:'Verify recovery code',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.code).toBe(1);await expect(input).toHaveValue('');expect((await snapshot(page)).codeDomEmptyAtCallback).toBe(true);await noRestorationClaim(page);
});
test('Code DOM is empty inside async callback and duplicate submits/status/cancel stay held',async({page})=>{
 await setup(page,code,{holdCode:true});await page.getByLabel('Recovery code',{exact:true}).fill('public-nonfunctional-value');
 await page.locator('form').evaluate((form:HTMLFormElement)=>{form.requestSubmit();form.requestSubmit();});await expect.poll(async()=>(await snapshot(page)).calls.code).toBe(1);
 const observed=await snapshot(page);expect(observed.codeArgumentPresent).toBe(true);expect(observed.codeDomEmptyAtCallback).toBe(true);await expect(page.getByLabel('Recovery code',{exact:true})).toHaveValue('');await expect(page.getByRole('button',{name:'Check recovery status',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Cancel request',exact:true})).toBeDisabled();expect(observed.calls.check).toBe(0);expect(observed.calls.cancel).toBe(0);
 await page.evaluate(()=>window.recoveryPresentationFixture.releaseCode());await expect(page.getByRole('button',{name:'Verify recovery code',exact:true})).toBeEnabled();expect((await snapshot(page)).calls.code).toBe(1);await noRestorationClaim(page);
});
test('Context leave and pagehide clear the owned secret synchronously without submitting',async({page})=>{
 await setup(page,code);const input=page.getByLabel('Recovery code',{exact:true});await input.fill('public-context-value');const previous=await input.elementHandle();expect(previous).not.toBeNull();await page.evaluate(()=>window.recoveryPresentationFixture.replaceContext());await expect(input).toHaveValue('');expect(await previous!.evaluate(element=>(element as HTMLInputElement).value)).toBe('');
 await input.fill('public-pagehide-value');const hidden=await page.evaluate(()=>{const field=document.querySelector<HTMLInputElement>('input[name=recoveryCode]')!;dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true}));return {emptyImmediately:field.value==='',local:localStorage.length,session:sessionStorage.length};});expect(hidden).toEqual({emptyImmediately:true,local:0,session:0});await expect(input).toHaveValue('');expect((await snapshot(page)).calls.code).toBe(0);await page.evaluate(()=>dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true})));expect(new URL(page.url()).search).toBe('');
});
for(const kind of ['pending-review','unknown'] as const)test(kind+' permits only admitted original status/navigation and never restores access',async({page})=>{
 await setup(page,{kind,goal,canCheck:true,canCancel:false,canBackToSignIn:true});await expect(page.getByRole('button')).toHaveCount(2);await expect(page.getByRole('button',{name:'Finish',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Cancel request',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Check recovery status',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.check).toBe(1);const observed=await snapshot(page);expect(observed.calls.start+observed.calls.choose+observed.calls.code+observed.calls.finish+observed.calls.cancel).toBe(0);await noRestorationClaim(page);
});
test('Partial result details block Finish; complete supplied details still require an explicit action',async({page})=>{
 const model:Extract<RecoveryViewModel,{kind:'result'}>={kind:'result',goal,canCheck:true,canCancel:false,canBackToSignIn:true,detailsAvailable:true,effects:null,followUps:[],canFinish:true};await setup(page,model);await expect(page.getByRole('button',{name:'Finish',exact:true})).toBeDisabled();expect((await snapshot(page)).calls.finish).toBe(0);await noRestorationClaim(page);
 await page.evaluate(model=>window.recoveryPresentationFixture.configure(model),{...model,effects:[{reference:'effect-A',labelKey:'fixture.effect.label',valueKey:'fixture.effect.value'}]});await expect(page.getByText('Fixture authoritative value',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Finish',exact:true})).toBeEnabled();expect((await snapshot(page)).calls.finish).toBe(0);await page.getByRole('button',{name:'Finish',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.finish).toBe(1);await noRestorationClaim(page);
});

test('Recovery conditional presentation supports keyboard activation and accessible tablet narrow reflow',async({page})=>{
 test.setTimeout(60000);
 for(const width of [768,320]){
  await page.setViewportSize({width,height:1024});
  const accessible=async()=>{
   await page.evaluate(()=>document.fonts.ready);
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
   const card=await page.locator('.recovery-card').boundingBox();expect(card).not.toBeNull();expect(card!.x).toBeGreaterThanOrEqual(0);expect(card!.x+card!.width).toBeLessThanOrEqual(width);
   expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  };
  await setup(page,entry);await expect(page.locator('h1')).toBeFocused();await page.keyboard.press('Tab');
  await expect(page.getByLabel('Email address',{exact:true})).toBeFocused();await page.keyboard.type('person@example.test');await page.keyboard.press('Tab');
  await expect(page.getByRole('radio',{name:/Fixture offered goal A/})).toBeFocused();await page.keyboard.press('ArrowDown');
  const selected=page.getByRole('radio',{name:/Fixture offered goal B/});await expect(selected).toBeFocused();await expect(selected).toHaveAttribute('aria-checked','true');
  await page.keyboard.press('Space');await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeFocused();await page.keyboard.press('Enter');
  await expect.poll(async()=>(await snapshot(page)).calls.start).toBe(1);expect((await snapshot(page)).chosenGoal).toBe('offered-B');await accessible();
  await setup(page,code);await expect(page.locator('h1')).toBeFocused();await page.keyboard.press('Tab');const input=page.getByLabel('Recovery code',{exact:true});await expect(input).toBeFocused();
  await page.keyboard.press('Enter');await expect(input).toBeFocused();await expect(input).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText('Check the highlighted details');
  expect(await input.evaluate(field=>(field.getAttribute('aria-describedby')??'').split(/\s+/).some(id=>document.getElementById(id)?.textContent==='Enter a recovery code.'))).toBe(true);await accessible();
  await page.keyboard.type('public-required-field-value');await expect(input).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Show recovery code',exact:true})).toBeFocused();await page.keyboard.press('Enter');await expect(input).toHaveAttribute('type','text');
  await page.keyboard.press('Space');await expect(input).toHaveAttribute('type','password');await page.keyboard.press('Tab');
  await expect(page.getByRole('button',{name:'Verify recovery code',exact:true})).toBeFocused();await page.keyboard.press('Enter');
  await expect.poll(async()=>(await snapshot(page)).calls.code).toBe(1);await expect(input).toHaveValue('');expect((await snapshot(page)).codeDomEmptyAtCallback).toBe(true);await noRestorationClaim(page);
  await setup(page,{kind:'proof-choice',goal,choices:[{reference:'offered-proof',labelKey:'fixture.proof',canSelect:true}],canCheck:true,canCancel:true});
  await page.keyboard.press('Tab');const choice=page.getByRole('button',{name:'Fixture admitted method',exact:true});await expect(choice).toBeFocused();await page.keyboard.press('Enter');
  await expect.poll(async()=>(await snapshot(page)).calls.choose).toBe(1);await expect(choice).toBeEnabled();await accessible();
  await setup(page,{kind:'pending-review',goal,canCheck:true,canCancel:false,canBackToSignIn:true});await page.keyboard.press('Tab');
  const check=page.getByRole('button',{name:'Check recovery status',exact:true});await expect(check).toBeFocused();await page.keyboard.press('Enter');await expect.poll(async()=>(await snapshot(page)).calls.check).toBe(1);await expect(check).toBeEnabled();await accessible();
  await setup(page,{kind:'result',goal,detailsAvailable:true,effects:[{reference:'effect-A',labelKey:'fixture.effect.label',valueKey:'fixture.effect.value'}],followUps:[],canFinish:true,canCheck:true,canCancel:false,canBackToSignIn:true});
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Check recovery status',exact:true})).toBeFocused();await page.keyboard.press('Tab');
  await expect(page.getByRole('button',{name:'Finish',exact:true})).toBeFocused();await page.keyboard.press('Enter');await expect.poll(async()=>(await snapshot(page)).calls.finish).toBe(1);await accessible();await noRestorationClaim(page);
  expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash}))).toEqual({local:0,session:0,search:'',hash:''});
 }
});
