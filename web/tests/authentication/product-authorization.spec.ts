import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/authentication.content.proposal.json' with {type:'json'};

async function setup(page:Page,pending:boolean){
  const content=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-only-publication'}]));
  const state={authenticated:false,resumes:0,nativeEntries:0};
  await page.route('**/authentication/options',route=>route.fulfill({json:{csrfToken:'fixture-anonymous-csrf',idempotencyKey:'12121212-3434-4567-8989-121212121212',passwordMaximumLength:128,locale:'en',content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:null}}));
  await page.route('**/authentication/sign-in',route=>{state.authenticated=true;return route.fulfill({json:{authenticated:true}});});
  await page.route('**/authentication/session',route=>route.fulfill(state.authenticated?{json:{authenticated:true,csrfToken:'fixture-current-session-csrf',...(pending?{productAuthorizationPending:true}:{})}}:{status:401,json:{error_code:'AUTHENTICATION_REQUIRED'}}));
  await page.route('**/product-authorization/continuation/resume',route=>{
    state.resumes++;
    expect(state.authenticated).toBe(true);
    expect(route.request().method()).toBe('POST');
    expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-session-csrf');
    expect(route.request().postDataJSON()).toEqual({});
    return route.fulfill({json:{resumePath:'/connect/authorize'}});
  });
  await page.route('**/connect/authorize',route=>{state.nativeEntries++;return route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="en"><title>Protocol fixture</title><body><h1>Native authorization fixture</h1></body></html>'});});
  return state;
}
async function signIn(page:Page){
  await page.goto('/login');
  await page.getByLabel('Email address',{exact:true}).fill('person@example.test');
  await page.getByLabel('Password',{exact:true}).fill('fixture existing passphrase');
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
}

test('Product continuation uses current-session CSRF and re-enters the native document endpoint without URL secrets',async({page})=>{
  const state=await setup(page,true);await signIn(page);
  await expect(page).toHaveURL('http://127.0.0.1:5189/connect/authorize');
  await expect(page.getByRole('heading',{name:'Native authorization fixture'})).toBeVisible();
  expect(state.resumes).toBe(1);expect(state.nativeEntries).toBe(1);
  expect(await page.evaluate(()=>({query:location.search,fragment:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',fragment:'',local:0,session:0});
});

for(const path of ['https://untrusted.example.test/authorize','//untrusted.example.test/authorize','/connect/authorize?code=fixture-secret','/connect/authorize#fixture-secret','/connect/%2e%2e/sign-out']){
  test('Product continuation rejects an unsafe resume pathname '+path,async({page})=>{
    const state=await setup(page,true);
    await page.route('**/product-authorization/continuation/resume',route=>{state.resumes++;return route.fulfill({json:{resumePath:path}});});
    await signIn(page);
    await expect(page.getByRole('alert')).toHaveText(proposal.content['authentication.error.unavailable']);
    await expect(page).toHaveURL('http://127.0.0.1:5189/signed-in');
    await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();
    expect(state.resumes).toBe(1);expect(state.nativeEntries).toBe(0);
  });
}

test('Unavailable Product continuation preserves truthful Tunner sign-in and does not replay credentials',async({page})=>{
  const state=await setup(page,true);let credentialPosts=0;
  await page.route('**/authentication/sign-in',route=>{credentialPosts++;state.authenticated=true;return route.fulfill({json:{authenticated:true}});});
  await page.route('**/product-authorization/continuation/resume',route=>{state.resumes++;return route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}});});
  await signIn(page);
  await expect(page.getByRole('alert')).toHaveText(proposal.content['authentication.error.unavailable']);
  await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeEnabled();
  expect(credentialPosts).toBe(1);expect(state.resumes).toBe(1);expect(state.nativeEntries).toBe(0);
  expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
});

test('Ordinary sign-in without a pending capability does not call Product continuation',async({page})=>{
  const state=await setup(page,false);await signIn(page);
  await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();
  expect(state.resumes).toBe(0);expect(state.nativeEntries).toBe(0);
});
