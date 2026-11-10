# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: authentication-flow.spec.ts >> verified local account signs in and completes operation-bound verification through the real BFF
- Location: tests\browser-testing\authentication-flow.spec.ts:4:1

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: "Strict"
Received: "None"
```

# Page snapshot

```yaml
- main [ref=f7e4]:
  - heading "Identity verification" [level=1] [ref=f7e5]
  - paragraph [ref=f7e6]: Isolated local test. This verifies your signed-in identity and records a test result. It changes no account settings.
  - button "Verify identity" [ref=f7e7]
  - status [ref=f7e8]: Verification completed
  - link "Sign in" [ref=f7e9]:
    - /url: /login
```

# Test source

```ts
  1  | import { test, expect } from '@playwright/test';
  2  | import {registerVerifiedAccount} from './fixtures';
  3  |
  4  | test('verified local account signs in and completes operation-bound verification through the real BFF',async({page,request})=>{
  5  |  const {email,password}=await registerVerifiedAccount(page,request,'2015-01-01');
  6  |  await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill(password);const auth=page.waitForResponse(r=>r.url().endsWith('/authentication/sign-in')&&r.request().method()==='POST');await page.getByRole('button',{name:'Sign in',exact:true}).click();const signed=await auth;expect(signed.status()).toBe(200);
  7  |  await expect(page).toHaveURL(/\/signed-in$/);await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();await page.reload();await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();await page.screenshot({path:'../artifacts/browser-testing/signed-in-browser-test.png',fullPage:true});
  8  |  await page.goto('/browser-testing/step-up');await expect(page.getByRole('heading',{name:'Identity verification',exact:true})).toBeVisible();await page.reload();await page.goBack();await expect(page).toHaveURL(/\/signed-in$/);await page.goForward();await expect(page.getByRole('heading',{name:'Identity verification',exact:true})).toBeVisible();
  9  |  const options=page.waitForResponse(r=>r.url().endsWith('/authentication/step-up/options'));await page.getByRole('button',{name:'Verify identity',exact:true}).click();const operationReference=(await(await options).json()).operationReference;expect(typeof operationReference).toBe('string');await expect(page.getByRole('dialog')).toBeVisible();const actionUrl=page.url();await page.getByRole('button',{name:'Other method',exact:true}).click();
  10 |  await page.getByLabel('Password',{exact:true}).fill('wrong existing credential');await page.getByRole('button',{name:'Confirm',exact:true}).click();await expect(page.getByLabel('Password',{exact:true})).toHaveAttribute('aria-invalid','true');await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');
  11 |  let completedPosts=0;await page.route('**/authentication/step-up/password',async route=>{completedPosts++;const actual=await route.fetch();expect(actual.status()).toBe(200);expect(await actual.json()).toEqual({status:'COMPLETED',signInRequired:false,operationReference});await route.abort();});
  12 |  await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Confirm',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Verification completed');expect(completedPosts).toBe(1);expect(page.url()).toBe(actionUrl);
  13 |  await page.reload();await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('status')).toHaveText('Verification completed');await expect(page.getByRole('dialog')).toHaveCount(0);expect(completedPosts).toBe(1);
> 14 |  const stepCookie=(await page.context().cookies()).find(c=>c.name==='__Host-Tunner.StepUp');expect(stepCookie?.httpOnly).toBe(true);expect(stepCookie?.secure).toBe(true);expect(stepCookie?.sameSite).toBe('Strict');
     |                                                                                                                                                                                                        ^ Error: expect(received).toBe(expected) // Object.is equality
  15 |  expect(await page.evaluate(()=>({search:location.search,hash:location.hash,visibleProof:document.cookie.includes('__Host-Tunner.StepUp')}))).toEqual({search:'',hash:'',visibleProof:false});await page.screenshot({path:'../artifacts/browser-testing/step-up-real-completed.png',fullPage:true});
  16 |  await page.goto('/signed-in');
  17 |  const cookie=(await page.context().cookies()).find(c=>c.name==='__Host-Tunner.User.Session');expect(cookie?.httpOnly).toBe(true);expect(cookie?.secure).toBe(true);expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,hasBearer:document.cookie.includes('__Host-Tunner.User.Session')}))).toEqual({local:0,session:0,hasBearer:false});
  18 |  await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);await page.goto('/signed-in');await expect(page).toHaveURL(/\/login$/);await page.goto('/browser-testing/step-up');await expect(page).toHaveURL(/\/login$/);
  19 | });
  20 |
```
