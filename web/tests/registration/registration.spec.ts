import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/content.proposal.json' with { type: 'json' };

// Explicit synthetic publication/policy for browser tests only. The shipped source remains DRAFT.
const content=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-only-publication',locale:'en',state:'PUBLISHED'}]));
const operationId='70fe1ab6-6d1f-4edc-a0ee-bc46f817d28a';
function pending(version=1){return {operationId,correlationId:'fixture-correlation',status:'REQUIRES_ACTION',version,expiresAt:new Date(Date.now()+300000).toISOString(),resendAfter:new Date(Date.now()-1000).toISOString()};}
async function boot(page:Page){await page.route('**/registration/options',route=>route.fulfill({json:{csrfToken:'fixture-token',serverTime:new Date().toISOString(),policyVersion:'fixture-policy',termsVersion:'fixture-terms',privacyVersion:'fixture-privacy',passwordMinimumLength:15,passwordMaximumLength:128,proofDigits:6,locale:'en',content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:'https://fixture.example.test/support'}}));await page.goto('/registration/');await expect(page.getByRole('heading',{name:proposal.content['registration.heading']})).toBeVisible();await page.evaluate(()=>document.fonts.ready);}
async function fill(page:Page){await page.getByLabel('Email address').fill('fixture@example.test');await page.getByLabel('Password',{exact:true}).fill('a unique fixture passphrase');await page.getByRole('checkbox').check();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your eligibility'})).toBeVisible();await page.getByLabel('Date of birth').fill('1990-01-01');}
async function beginVerification(page:Page){await page.route('**/registration/register',route=>route.fulfill({status:202,json:pending()}));await boot(page);await fill(page);await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();}
async function axe(page:Page){const report=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(report.violations).toEqual([]);}

test('source geometry, local fonts, accessible registration and no stored sensitive state',async({page})=>{
 const external:string[]=[];page.on('request',r=>{if(!r.url().startsWith('http://127.0.0.1:5189')&&!r.url().startsWith('data:'))external.push(r.url());});await boot(page);
 expect(await page.locator('.registration-card').evaluate(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height}))).toEqual({width:580,height:574});
 expect(await page.getByLabel('Email address').evaluate(e=>e.getBoundingClientRect().height)).toBe(52);
 expect(await page.getByRole('button',{name:'Continue',exact:true}).evaluate(e=>e.getBoundingClientRect().height)).toBe(48);
 expect(await page.locator('.tn-brand').textContent()).toBe('Tunner');await expect(page.locator('.tn-brand img,.tn-brand svg')).toHaveCount(0);
 expect(await page.evaluate(()=>document.fonts.check('600 14px "Geist Variable"'))).toBe(true);
 expect(await page.locator('img').evaluateAll(images=>images.every(element=>{const e=element as HTMLImageElement;return e.complete&&e.naturalWidth>0&&e.getBoundingClientRect().width===e.naturalWidth;}))).toBe(true);
 expect(external).toEqual([]);await axe(page);await page.screenshot({path:'test-results/registration/register-desktop.png',fullPage:true});
 await expect(page.getByLabel('Date of birth')).toHaveCount(0);await fill(page);await axe(page);await page.screenshot({path:'test-results/registration/eligibility-desktop.png',fullPage:true});expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('underage error is server-owned and clears raw password and DOB',async({page})=>{
 await page.route('**/registration/register',route=>route.fulfill({status:400,json:{error_code:'AGE_INELIGIBLE',correlation_id:'fixture-correlation'}}));await boot(page);await fill(page);await page.getByLabel('Date of birth').fill('2015-01-01');await page.getByRole('button',{name:'Create account',exact:true}).click();
 await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.age']);await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');await expect(page.getByLabel('Date of birth')).toHaveCount(0);await axe(page);await page.screenshot({path:'test-results/registration/underage-error.png',fullPage:true});
});
test('proof errors use returned version, resend refreshes proof and successful activation grants no session or Product action',async({page})=>{
 await beginVerification(page);await axe(page);await page.screenshot({path:'test-results/registration/verify-desktop.png',fullPage:true});
 let receivedVersion=0;await page.route('**/registration/verify',async route=>{receivedVersion=(route.request().postDataJSON() as {expectedVersion:number}).expectedVersion;await route.fulfill({status:400,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE',current_version:2}});});
 await page.getByLabel('Verification code').fill('000000');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('alert')).toBeVisible();expect(receivedVersion).toBe(1);
 await page.route('**/registration/resend',async route=>{expect((route.request().postDataJSON() as {expectedVersion:number}).expectedVersion).toBe(2);await route.fulfill({status:202,json:pending(3)});});await page.getByRole('button',{name:'Resend code'}).click();await expect(page.getByLabel('Verification code')).toHaveValue('');
 await page.unroute('**/registration/verify');await page.route('**/registration/verify',async route=>{expect((route.request().postDataJSON() as {expectedVersion:number}).expectedVersion).toBe(3);await route.fulfill({json:{...pending(4),status:'SUCCEEDED',expiresAt:null,resendAfter:null,accountId:'ACC-fixture'}});});
 await page.getByLabel('Verification code').fill('123456');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();await expect(page.getByRole('link',{name:'Sign in',exact:true})).toHaveCount(0);await axe(page);await page.screenshot({path:'test-results/registration/ready-desktop.png',fullPage:true});
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('uncertain submit recovers the same idempotency identity without retaining credentials or automatically resubmitting',async({page})=>{
 let posts=0;let key='';await page.route('**/registration/register',async route=>{posts++;key=(route.request().postDataJSON() as {idempotencyKey:string}).idempotencyKey;await route.abort('failed');});
 await page.route('**/registration/status/*',async route=>{expect(route.request().url().endsWith(key)).toBe(true);await route.fulfill({status:202,json:pending()});});
 await boot(page);await fill(page);await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.getByRole('button',{name:'Check progress'})).toBeVisible();await expect(page.getByLabel('Password',{exact:true})).toHaveCount(0);await expect(page.getByLabel('Date of birth')).toHaveValue('');
 await page.getByRole('button',{name:'Check progress'}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();expect(posts).toBe(1);
});
test('320px layout remains accessible in all four states',async({page})=>{
 await page.setViewportSize({width:320,height:1000});await beginVerification(page);
 for(const stage of ['verify','ready']){if(stage==='ready'){await page.route('**/registration/verify',route=>route.fulfill({json:{...pending(2),status:'SUCCEEDED'}}));await page.getByLabel('Verification code').fill('123456');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();}
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await axe(page);await page.screenshot({path:'test-results/registration/'+stage+'-narrow.png',fullPage:true});}
 await boot(page);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await axe(page);await page.screenshot({path:'test-results/registration/register-narrow.png',fullPage:true});await fill(page);await axe(page);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'test-results/registration/eligibility-narrow.png',fullPage:true});
});
test('expired proof disables verification and unavailable policy does not render a registration form',async({page})=>{
 await page.route('**/registration/register',route=>route.fulfill({status:202,json:{...pending(),expiresAt:new Date(Date.now()-1000).toISOString()}}));await boot(page);await fill(page);await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.getByRole('button',{name:'Verify email',exact:true})).toBeDisabled();
 await page.unroute('**/registration/options');await page.route('**/registration/options',route=>route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}}));await page.reload();await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('form')).toHaveCount(0);
});

test('first screen has no DOB and requires notices before the separate eligibility step',async({page})=>{
 let posts=0;await page.route('**/registration/register',route=>{posts++;return route.fulfill({status:202,json:pending()});});await boot(page);
 await expect(page.getByLabel('Date of birth')).toHaveCount(0);await page.getByLabel('Email address').fill('fixture@example.test');await page.getByLabel('Password',{exact:true}).fill('a unique fixture passphrase');
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:proposal.content['registration.heading']})).toBeVisible();expect(posts).toBe(0);
 await page.getByRole('checkbox').check();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByLabel('Date of birth')).toBeVisible();expect(posts).toBe(0);
 await page.getByRole('button',{name:'Back to registration'}).click();await expect(page.getByLabel('Date of birth')).toHaveCount(0);await expect(page.getByRole('checkbox')).toBeChecked();
 await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByLabel('Date of birth').fill('1990-01-01');await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();expect(posts).toBe(1);
});
