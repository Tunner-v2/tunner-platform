import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import type {RecoveryViewModel} from '../../apps/user/recovery';
import type {RecoveryFixtureSnapshot} from '../../fixtures/recovery-case/main';

const goal={reference:'offered-A',labelKey:'fixture.goal.a'},other={reference:'offered-B',labelKey:'fixture.goal.b'};
const entry:RecoveryViewModel={kind:'entry',goals:[goal,other],canStart:true,canBackToSignIn:true};
const code:RecoveryViewModel={kind:'recovery-code',goal,canVerify:true,canCheck:true,canCancel:true};
async function setup(page:Page,model:RecoveryViewModel,options:{holdCode?:boolean;missingKey?:string}={}){
 await page.goto('/registration/');await page.waitForFunction(()=>!!window.recoveryPresentationFixture);await page.evaluate(({model,options})=>window.recoveryPresentationFixture.configure(model,options),{model,options});await expect(page.locator('.recovery-card')).toBeVisible();
}
async function snapshot(page:Page):Promise<RecoveryFixtureSnapshot>{return page.evaluate(()=>window.recoveryPresentationFixture.snapshot());}
async function noRestorationClaim(page:Page){await expect(page.getByRole('heading',{name:'Account recovered',exact:true})).toHaveCount(0);await expect(page.getByText(/^(?:Your account (has been|is) recovered|You are signed in|(?:Sign-in )?Access has been restored)[.!]?$/i)).toHaveCount(0);}

test('Empty entry focuses real email input, shares field/summary errors and clears them on correction',async({page})=>{
 await setup(page,entry);await expect(page.getByRole('radio',{checked:true})).toHaveCount(0);await expect(page.getByText('Fixture unoffered goal',{exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Continue',exact:true}).click();const email=page.getByLabel('Email address',{exact:true});await expect(email).toBeFocused();await expect(email).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText('Check the highlighted details');expect((await snapshot(page)).calls.start).toBe(0);
 await email.fill('person@example.test');await page.getByRole('radio',{name:/Fixture offered goal B/}).click();await expect(email).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect.poll(async()=>(await snapshot(page)).calls.start).toBe(1);expect((await snapshot(page)).chosenGoal).toBe('offered-B');
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
