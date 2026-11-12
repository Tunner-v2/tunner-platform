import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/content.proposal.json' with {type:'json'};
import authenticationCopy from '../../apps/user/authentication.content.proposal.json' with {type:'json'};
import {personal,content} from './support';

const requestId='12121212-3434-4567-8989-121212121212',attemptId='23232323-4545-4678-8989-232323232323',registrationKey='34343434-5656-4789-8989-343434343434';
const original={mode:'FEDERATED',provider:'Google',verifiedEmail:'person@example.test',federationRequestId:requestId,attemptId,registrationKey,expectedVersion:1,canContinue:true};
const operation=(status:string)=>({requestId,attemptId,status,errorCode:null,resolution:'REGISTRATION_INTAKE'});
async function setup(page:Page){
 const state:{intake:Record<string,unknown>|undefined;current:ReturnType<typeof personal>|undefined;content:typeof content;complete:number;result:number;restart:number}={intake:{...original},current:undefined,content:{...content},complete:0,result:0,restart:0};
 await page.route('**/registration/options',route=>route.fulfill({json:{csrfToken:'fixture-registration-csrf',registrationKey,serverTime:new Date().toISOString(),policyVersion:'fixture-policy',termsVersion:'fixture-terms',privacyVersion:'fixture-privacy',passwordMinimumLength:15,passwordMaximumLength:128,proofDigits:6,locale:'en',content:state.content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:'https://fixture.example.test/support',...(!state.current&&state.intake?{federationIntake:state.intake}:{})}}));
 await page.route('**/registration/progress',route=>route.fulfill(state.current?{json:state.current}:{status:400,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}}));
 await page.route('**/registration/restart',route=>{state.restart++;return route.fulfill({status:500});});
 await page.route('**/authentication/session',route=>route.fulfill({json:{authenticated:true,csrfToken:'fixture-current-csrf'}}));
 await page.route('**/authentication/federation/complete',route=>{state.complete++;state.current=personal();return route.fulfill({json:{authenticated:false,operation:operation('COMPLETED'),registration:state.current}});});
 await page.route('**/authentication/federation/result',route=>{state.result++;return route.fulfill({json:{authenticated:false,operation:operation(state.current?'COMPLETED':'UNKNOWN'),...(state.current?{registration:state.current}:{})}});});
 return state;
}
async function open(page:Page){await page.goto('/registration');await expect(page.getByRole('heading',{name:proposal.content['registration.heading']})).toBeVisible();}

test('Qualified original intake is read-only, passwordless and requires explicit shared notice validation',async({page})=>{
 const state=await setup(page);await open(page);
 await expect(page.getByLabel('Email address')).toHaveValue(original.verifiedEmail);await expect(page.getByLabel('Email address')).toHaveAttribute('readonly','');
 await expect(page.locator('input[type=password],input[type=date]')).toHaveCount(0);await expect(page.getByText('This email was verified by Google.',{exact:true})).toBeVisible();
 await expect(page.getByRole('checkbox')).not.toBeChecked();await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(page.getByRole('checkbox')).toBeFocused();await expect(page.getByRole('checkbox')).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.highlighted']);expect(state.complete).toBe(0);expect(state.restart).toBe(0);
 await page.getByRole('checkbox').check();await expect(page.getByRole('alert')).toHaveCount(0);
});

test('Explicit intake completion posts only original identifiers/version and acknowledged current notices with real-principal CSRF',async({page})=>{
 const state=await setup(page);let body:Record<string,unknown>|undefined;
 await page.route('**/authentication/federation/complete',route=>{state.complete++;body=route.request().postDataJSON();expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-csrf');state.current=personal();return route.fulfill({json:{authenticated:false,operation:operation('COMPLETED'),registration:state.current}});});
 await open(page);await page.getByRole('checkbox').check();await page.locator('form').evaluate((form:HTMLFormElement)=>{form.requestSubmit();form.requestSubmit();});
 await expect(page).toHaveURL(/\/registration\/personal-information$/);await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();
 expect(body).toEqual({attemptId,federationRequestId:requestId,registrationKey,expectedVersion:1,termsVersion:'fixture-terms',privacyVersion:'fixture-privacy',acknowledgedNotices:true});expect(state.complete).toBe(1);expect(state.restart).toBe(0);
 await expect(page.getByLabel('Date of birth (required)')).toBeVisible();await expect(page.getByLabel('Primary email')).toHaveAttribute('readonly','');
});

test('Lost completion checks only the exact original result and never resubmits or stores registration identity',async({page})=>{
 const state=await setup(page);await page.route('**/authentication/federation/complete',async route=>{state.complete++;state.current=personal();await route.abort('failed');});
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});return route.fulfill({json:{authenticated:false,operation:operation('COMPLETED'),registration:state.current}});});
 await open(page);await page.getByRole('checkbox').check();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('button',{name:'Check progress'})).toBeVisible();
 expect(state.complete).toBe(1);expect(state.result).toBe(0);await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Check progress'}).click();await expect(page).toHaveURL(/\/registration\/personal-information$/);expect(state.complete).toBe(1);expect(state.result).toBe(1);
 expect(new URL(page.url()).search).toBe('');expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});

for(const changed of ['version','notices','withdrawn'] as const)test('Intake preflight '+changed+' change cannot submit the acknowledged old source',async({page})=>{
 const state=await setup(page);await open(page);
 if(changed==='version')state.intake={...original,expectedVersion:2};
 else if(changed==='withdrawn')state.intake={...original,verifiedEmail:null,canContinue:false};
 else await page.route('**/registration/options',route=>route.fulfill({json:{registrationKey,termsVersion:'changed-terms',privacyVersion:'fixture-privacy',federationIntake:original}}));
 await page.getByRole('checkbox').check();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.changed']);await expect(page.getByRole('checkbox')).not.toBeChecked();expect(state.complete).toBe(0);
});

for(const invalid of ['unqualified','blocked','wrong-key','missing-publication'] as const)test('Intake '+invalid+' stays unavailable without ordinary restart or credential fallback',async({page})=>{
 const state=await setup(page);
 if(invalid==='unqualified')state.intake={...original,provider:'Microsoft'};
 if(invalid==='blocked')state.intake={...original,verifiedEmail:null,canContinue:false};
 if(invalid==='wrong-key')state.intake={...original,registrationKey:requestId};
 if(invalid==='missing-publication')delete state.content['registration.federation.emailSource.Google'];
 await page.goto('/registration');await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('form')).toHaveCount(0);expect(state.complete).toBe(0);expect(state.restart).toBe(0);
});

test('Reload and Back/Forward retain the same intake and later Personal Information uses native progress',async({page})=>{
 const state=await setup(page);await open(page);await page.reload();await expect(page.getByLabel('Email address')).toHaveValue(original.verifiedEmail);
 await page.evaluate(()=>{history.pushState(null,'','/registration/personal-information');dispatchEvent(new PopStateEvent('popstate'));});await expect(page).toHaveURL(/\/registration$/);await expect(page.getByLabel('Email address')).toHaveValue(original.verifiedEmail);
 await page.goBack();await expect(page.getByRole('heading',{name:proposal.content['registration.heading']})).toBeVisible();await page.goForward();await expect(page.getByLabel('Email address')).toHaveValue(original.verifiedEmail);expect(state.restart).toBe(0);expect(state.complete).toBe(0);
 state.current=personal();await page.reload();await expect(page).toHaveURL(/\/registration\/personal-information$/);await expect(page.getByLabel('Primary email')).toHaveAttribute('readonly','');expect(state.restart).toBe(0);
});

for(const restored of ['VERIFIED','UNKNOWN'])test('Login '+restored+' intake hands off to registration without linked-sign-in completion',async({page})=>{
 const state=await setup(page);const published=Object.fromEntries(Object.entries(authenticationCopy.content).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-publication'}]));
 await page.route('**/authentication/options',route=>route.fulfill({json:{csrfToken:'fixture-auth-csrf',idempotencyKey:requestId,passwordMaximumLength:128,locale:'en',content:published,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:null,federation:{googleAvailable:true,microsoftAvailable:true,pending:operation(restored)}}}));
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});return route.fulfill({json:{authenticated:false,operation:operation('VERIFIED'),registrationRequired:true}});});
 await page.goto('/login');if(restored==='UNKNOWN'){await page.getByRole('button',{name:'Check status',exact:true}).click();}
 await expect(page).toHaveURL(/\/registration$/);await expect(page.getByLabel('Email address')).toHaveValue(original.verifiedEmail);expect(state.complete).toBe(0);expect(state.restart).toBe(0);expect(state.result).toBe(restored==='UNKNOWN'?1:0);
});

for(const width of [1440,390,320])test('Approved passwordless intake normal/error geometry, keyboard and accessibility at '+width,async({page},testInfo)=>{
 await page.setViewportSize({width,height:1024});await setup(page);await open(page);await page.evaluate(()=>document.fonts.ready);
 await expect(page.getByLabel('Email address')).toHaveAttribute('readonly','');expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.screenshot({path:testInfo.outputPath('federation-intake-normal-'+width+'.png'),fullPage:true});await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('checkbox')).toBeFocused();
 await page.screenshot({path:testInfo.outputPath('federation-intake-error-'+width+'.png'),fullPage:true});expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 await page.keyboard.press('Space');await expect(page.getByRole('checkbox')).toBeChecked();await expect(page.getByRole('alert')).toHaveCount(0);await page.keyboard.press('Tab');await expect(page.getByRole('link',{name:'Terms',exact:true})).toBeFocused();
});
