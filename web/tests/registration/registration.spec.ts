import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/content.proposal.json' with { type: 'json' };

// Explicit synthetic publication/policy for browser tests only. Shipped copy remains DRAFT.
const content=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-only-publication',locale:'en',state:'PUBLISHED'}]));
const operationId='70fe1ab6-6d1f-4edc-a0ee-bc46f817d28a';
function pending(version=1,nextAction='VERIFY_CONTACT'){return {operationId,correlationId:'fixture-correlation',status:'REQUIRES_ACTION',version,nextAction,expiresAt:new Date(Date.now()+300000).toISOString(),resendAfter:new Date(Date.now()-1000).toISOString()};}
function personal(){return {...pending(2,'PERSONAL_INFORMATION'),personalInformation:{fullName:null,displayName:null,verifiedEmail:'fixture@example.test',fullNameMaximumLength:100,displayNameMaximumLength:80}};}
function complete(){return {...pending(4,'COMPLETE'),status:'SUCCEEDED',expiresAt:null,resendAfter:null,accountId:'ACC-fixture'};}
async function boot(page:Page){await page.route('**/registration/options',route=>route.fulfill({json:{csrfToken:'fixture-token',serverTime:new Date().toISOString(),policyVersion:'fixture-policy',termsVersion:'fixture-terms',privacyVersion:'fixture-privacy',passwordMinimumLength:15,passwordMaximumLength:128,proofDigits:6,locale:'en',content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:'https://fixture.example.test/support'}}));await page.goto('/registration/');await expect(page.getByRole('heading',{name:proposal.content['registration.heading']})).toBeVisible();await page.evaluate(()=>document.fonts.ready);}
async function fill(page:Page){await page.getByLabel('Email address').fill('fixture@example.test');await page.getByLabel('Password',{exact:true}).fill('a unique fixture passphrase');await page.getByLabel('Confirm password',{exact:true}).fill('a unique fixture passphrase');await page.getByRole('checkbox').check();}
async function beginVerification(page:Page){await page.route('**/registration/register',async route=>{const body=route.request().postDataJSON() as Record<string,unknown>;expect(Object.keys(body).sort()).toEqual(['acknowledgedNotices','email','idempotencyKey','password','passwordConfirmation','privacyVersion','termsVersion'].sort());await route.fulfill({status:202,json:pending()});});await boot(page);await fill(page);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();await expect(page.getByLabel('Date of birth (required)')).toHaveCount(0);}
async function openPersonal(page:Page){await page.route('**/registration/verify',route=>route.fulfill({status:202,json:personal()}));await page.getByLabel('Verification code').fill('123456');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();}
async function fillPersonal(page:Page){await page.getByLabel('Full name',{exact:true}).fill('Fixture Person');await page.getByLabel('Display name',{exact:true}).fill('Fixture');await page.getByLabel('Date of birth (required)').fill('2015-01-01');}
async function axe(page:Page){const report=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(report.violations).toEqual([]);}
async function snapshot(page:Page,name:string){await axe(page);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'test-results/registration/'+name+'.png',fullPage:true});}

test('registration reference geometry, local assets/fonts and no DOB or stored sensitive state',async({page})=>{
 const external:string[]=[];page.on('request',r=>{if(!r.url().startsWith('http://127.0.0.1:5189')&&!r.url().startsWith('data:'))external.push(r.url());});await boot(page);
 expect(await page.locator('.registration-card').evaluate(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height}))).toEqual({width:580,height:662});
 expect(await page.getByLabel('Email address').evaluate(e=>e.getBoundingClientRect().height)).toBe(52);
 expect(await page.getByRole('button',{name:'Continue',exact:true}).evaluate(e=>e.getBoundingClientRect().height)).toBe(48);
 expect(await page.locator('.tn-brand').textContent()).toBe('Tunner');await expect(page.locator('.tn-brand img,.tn-brand svg')).toHaveCount(0);
 expect(await page.evaluate(()=>document.fonts.check('600 14px "Geist Variable"'))).toBe(true);
 expect(await page.locator('img').evaluateAll(images=>images.every(element=>{const e=element as HTMLImageElement;return e.complete&&e.naturalWidth>0&&e.getBoundingClientRect().width===e.naturalWidth;}))).toBe(true);
 expect(external).toEqual([]);await expect(page.locator('input[type=date]')).toHaveCount(0);await expect(page.getByText(/18 or older|eligibility/i)).toHaveCount(0);
 await expect(page.getByLabel('Email address')).toHaveAttribute('placeholder','you@example.com');
 await expect(page.getByLabel('Password',{exact:true})).toHaveAttribute('placeholder','Create a strong password');
 await expect(page.getByLabel('Confirm password')).toHaveAttribute('placeholder','Enter your password again');
 expect(await page.getByRole('checkbox').evaluate(e=>getComputedStyle(e).borderRadius)).toBe('5px');
 expect(await page.getByRole('heading').evaluate(e=>getComputedStyle(e).letterSpacing)).toBe('normal');
 await expect(page.getByRole('link',{name:'Terms of Service',exact:true})).toBeVisible();
 await expect(page.getByRole('link',{name:'Contact Support',exact:true})).toBeVisible();
 await snapshot(page,'register-desktop');expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('confirmation and notice acceptance precede direct registration without age fields',async({page})=>{
 let posts=0;await page.route('**/registration/register',route=>{posts++;return route.fulfill({status:202,json:pending()});});await boot(page);
 await page.getByLabel('Email address').fill('fixture@example.test');await page.getByLabel('Password',{exact:true}).fill('a unique fixture passphrase');await page.getByLabel('Confirm password').fill('different');
 await page.getByRole('button',{name:'Continue',exact:true}).click();expect(posts).toBe(0);
 await page.getByRole('checkbox').check();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.confirmation']);expect(posts).toBe(0);
 await page.getByLabel('Confirm password').fill('a unique fixture passphrase');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();expect(posts).toBe(1);await expect(page.locator('input[type=date]')).toHaveCount(0);
});
test('proof errors/version/resend lead to normal Personal Information and under18 completion without Product/session action',async({page})=>{
 await beginVerification(page);
 await expect(page.getByLabel('Verification code')).toHaveAttribute('placeholder','Verification code');
 await expect(page.getByText('STEP 2',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Go back',exact:true})).toBeVisible();
 await snapshot(page,'verify-desktop');
 let receivedVersion=0;await page.route('**/registration/verify',async route=>{receivedVersion=(route.request().postDataJSON() as {expectedVersion:number}).expectedVersion;await route.fulfill({status:400,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE',current_version:2}});});
 await page.getByLabel('Verification code').fill('000000');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('alert')).toBeVisible();expect(receivedVersion).toBe(1);
 await page.route('**/registration/resend',async route=>{expect((route.request().postDataJSON() as {expectedVersion:number}).expectedVersion).toBe(2);await route.fulfill({status:202,json:pending(3)});});await page.getByRole('button',{name:'Resend code'}).click();await expect(page.getByLabel('Verification code')).toHaveValue('');
 await page.unroute('**/registration/verify');await page.route('**/registration/verify',async route=>{expect((route.request().postDataJSON() as {expectedVersion:number}).expectedVersion).toBe(3);await route.fulfill({status:202,json:{...personal(),version:4}});});
 await page.getByLabel('Verification code').fill('123456');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();
 await expect(page.getByLabel('Primary email')).toHaveValue('fixture@example.test');await expect(page.getByLabel('Primary email')).toHaveAttribute('readonly','');
 await snapshot(page,'personal-desktop');await fillPersonal(page);
 await page.route('**/registration/personal-information',async route=>{const body=route.request().postDataJSON() as Record<string,unknown>;expect(body).toMatchObject({expectedVersion:4,dateOfBirth:'2015-01-01',fullName:'Fixture Person',displayName:'Fixture'});expect(body).not.toHaveProperty('independentCommercialEligible');await route.fulfill({json:complete()});});
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();await expect(page.getByRole('link',{name:'Sign in',exact:true})).toHaveCount(0);await expect(page.locator('input[type=date]')).toHaveCount(0);await snapshot(page,'ready-desktop');
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('policy-required consent uses trusted status and cannot be asserted by a browser checkbox',async({page})=>{
 await beginVerification(page);await openPersonal(page);await fillPersonal(page);
 await page.route('**/registration/personal-information',route=>route.fulfill({status:202,json:pending(3,'CONSENT_REQUIRED')}));await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Additional consent required'})).toBeVisible();await expect(page.getByRole('checkbox')).toHaveCount(0);await snapshot(page,'consent-desktop');
 await page.route('**/registration/continue',async route=>{expect(Object.keys(route.request().postDataJSON()).sort()).toEqual(['expectedVersion','idempotencyKey','operationId']);await route.fulfill({json:complete()});});await page.getByRole('button',{name:'Check consent status'}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();
});
test('uncertain registration recovers same identity and clears credentials without automatically resubmitting',async({page})=>{
 let posts=0;let key='';await page.route('**/registration/register',async route=>{posts++;key=(route.request().postDataJSON() as {idempotencyKey:string}).idempotencyKey;await route.abort('failed');});
 await page.route('**/registration/status/*',async route=>{expect(route.request().url().endsWith(key)).toBe(true);await route.fulfill({status:202,json:pending()});});
 await boot(page);await fill(page);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('button',{name:'Check progress'})).toBeVisible();await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');await expect(page.getByLabel('Confirm password')).toHaveValue('');await expect(page.locator('input[type=date]')).toHaveCount(0);
 await page.getByRole('button',{name:'Check progress'}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();expect(posts).toBe(1);
});
test('320px layout remains accessible across registration, verification, normal profile, consent and completion',async({page})=>{
 await page.setViewportSize({width:320,height:1000});await boot(page);await snapshot(page,'register-narrow');
 await beginVerification(page);await snapshot(page,'verify-narrow');await openPersonal(page);await snapshot(page,'personal-narrow');await fillPersonal(page);
 await page.route('**/registration/personal-information',route=>route.fulfill({status:202,json:pending(3,'CONSENT_REQUIRED')}));await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Additional consent required'})).toBeVisible();await snapshot(page,'consent-narrow');
 await page.route('**/registration/continue',route=>route.fulfill({json:complete()}));await page.getByRole('button',{name:'Check consent status'}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();await snapshot(page,'ready-narrow');
});
test('expired proof disables verification and unavailable policy does not render a registration form',async({page})=>{
 await page.route('**/registration/register',route=>route.fulfill({status:202,json:{...pending(),expiresAt:new Date(Date.now()-1000).toISOString()}}));await boot(page);await fill(page);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('button',{name:'Verify email',exact:true})).toBeDisabled();
 await page.unroute('**/registration/options');await page.route('**/registration/options',route=>route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}}));await page.reload();await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('form')).toHaveCount(0);
});
test('uncertain Personal Information submission clears DOB and recovers without another profile POST',async({page})=>{
 await beginVerification(page);await openPersonal(page);await fillPersonal(page);let posts=0;
 await page.route('**/registration/personal-information',async route=>{posts++;await route.abort('failed');});await page.route('**/registration/status/*',route=>route.fulfill({json:complete()}));
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('button',{name:'Check progress'})).toBeVisible();await expect(page.getByLabel('Date of birth (required)')).toHaveValue('');
 await page.getByRole('button',{name:'Check progress'}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();expect(posts).toBe(1);
});
