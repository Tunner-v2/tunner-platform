import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/authentication.content.proposal.json' with {type:'json'};
const root='http://127.0.0.1:5189';
async function setup(page:Page,authenticated=false){
 const content=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-only-publication'}]));
 const boot={csrfToken:'fixture-anonymous-csrf',idempotencyKey:'12121212-3434-4567-8989-121212121212',passwordMaximumLength:128,locale:'en',content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:'https://fixture.example.test/support'};
 const state={authenticated};
 await page.route('**/authentication/options',route=>route.fulfill({json:boot}));
 await page.route('**/authentication/session',route=>route.fulfill(state.authenticated?{json:{authenticated:true,csrfToken:'fixture-authenticated-csrf'}}:{status:401,json:{error_code:'AUTHENTICATION_REQUIRED'}}));
 return state;
}
async function fill(page:Page,password='fixture existing passphrase'){await page.getByLabel('Email address',{exact:true}).fill('person@example.test');await page.getByLabel('Password',{exact:true}).fill(password);}
async function accessibility(page:Page,name:string){
 await page.evaluate(()=>document.fonts.ready);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.screenshot({path:'test-results/authentication/'+name+'.png',fullPage:true});
}
for(const width of [1440,1024,768,390,320]){
 test('Login fidelity accessibility and usable fields at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});await setup(page);await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Google',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Microsoft',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Forgot password?',exact:true})).toBeDisabled();
  await expect(page.getByText('System Status:')).toHaveCount(0);await expect(page.getByText('GitHub',{exact:true})).toHaveCount(0);await expect(page.locator('input[type=date]')).toHaveCount(0);
  expect(await page.locator('.authentication-glow').evaluateAll(nodes=>nodes.every(node=>{const image=node as HTMLImageElement;return image.complete&&image.naturalWidth>0;}))).toBe(true);
  await accessibility(page,'login-'+width);
 });
 test('Signed-in confirmation fidelity and accessibility at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});await setup(page,true);await page.goto('/signed-in');await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeEnabled();
  await expect(page.getByText('Signing out ends your Tunner session. Connected products manage their own sessions.')).toBeVisible();await accessibility(page,'signed-in-'+width);
 });
}
for(const width of [1440,768,320]){test('Login empty blur correction first invalid focus and server error preserve only safe values at '+width,async({page})=>{
 await page.setViewportSize({width,height:1024});
 await setup(page);let posts=0;await page.route('**/authentication/sign-in',route=>{posts++;return route.fulfill({status:401,json:{error_code:'AUTHENTICATION_FAILED'}});});await page.goto('/login');
 await page.getByLabel('Email address').fill('bad');await page.getByLabel('Password',{exact:true}).focus();await expect(page.getByLabel('Email address')).toHaveAttribute('aria-invalid','true');
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page.getByLabel('Email address')).toBeFocused();expect(posts).toBe(0);await expect(page.getByRole('alert')).toHaveText(proposal.content['authentication.error.highlighted']);
 await fill(page);await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['authentication.error.credentials']);
 expect(posts).toBe(1);await expect(page.getByLabel('Email address')).toHaveValue('person@example.test');await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');
 const field=page.getByLabel('Password',{exact:true});await field.focus();await page.getByLabel('Email address').focus();const description=await field.getAttribute('aria-describedby');expect(description).toBeTruthy();await expect(page.locator('[id="'+description+'"]')).toHaveText(proposal.content['authentication.error.password']);
 await accessibility(page,'login-server-error-'+width);
});}
test('Canonical URL direct entry refresh browser history and sign-out guard',async({page})=>{
 const state=await setup(page);let posts=0;await page.route('**/authentication/sign-in',async route=>{posts++;expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-anonymous-csrf');state.authenticated=true;await route.fulfill({json:{authenticated:true}});});
 await page.route('**/authentication/sign-out',async route=>{expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-authenticated-csrf');state.authenticated=false;await route.fulfill({json:{authenticated:false}});});
 await page.goto('/signed-in');await expect(page).toHaveURL(root+'/login');await fill(page,'old-pass');await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(root+'/signed-in');await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();expect(posts).toBe(1);
 await page.reload();await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();await page.goBack();await expect(page).toHaveURL(root+'/login');await page.goForward();await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();
 await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(root+'/login');await page.goBack();await expect(page).toHaveURL(root+'/login');
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,query:location.search,fragment:location.hash}))).toEqual({local:0,session:0,query:'',fragment:''});
});
test('Pending guard blocks accidental duplicates and password visibility is keyboard accessible',async({page})=>{
 const state=await setup(page);let posts=0;await page.route('**/authentication/sign-in',async route=>{posts++;await new Promise(resolve=>setTimeout(resolve,300));state.authenticated=true;await route.fulfill({json:{authenticated:true}});});await page.goto('/login');await fill(page);
 await page.getByRole('button',{name:'Show password'}).focus();await page.keyboard.press('Enter');await expect(page.getByLabel('Password',{exact:true})).toHaveAttribute('type','text');await expect(page.getByRole('button',{name:'Hide password'})).toHaveAttribute('aria-pressed','true');await page.keyboard.press('Enter');await expect(page.getByLabel('Password',{exact:true})).toHaveAttribute('type','password');
 await page.locator('form').evaluate(element=>{const form=element as HTMLFormElement;form.requestSubmit();form.requestSubmit();});await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();await expect(page).toHaveURL(root+'/signed-in');expect(posts).toBe(1);
});
test('Server unavailable logout retains truthful current state and retry; no secrets enter URL',async({page})=>{
 const state=await setup(page,true);let fail=true;await page.route('**/authentication/sign-out',route=>{if(fail)return route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}});state.authenticated=false;return route.fulfill({json:{authenticated:false}});});await page.goto('/signed-in');await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['authentication.error.unavailable']);await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeEnabled();fail=false;await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(root+'/login');
});
