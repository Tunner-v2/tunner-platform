# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: navigation.spec.ts >> Approved Ready and SignedIn navigation retains canonical guarded chain, keyboard and geometry at 320
- Location: tests\passkeys\navigation.spec.ts:14:35

# Error details

```
Error: expect(received).not.toBe(expected) // Object.is equality

Expected: not "none"
```

# Page snapshot

```yaml
- generic [ref=f1e4]:
  - banner [ref=f1e5]:
    - generic [ref=f1e6]: Tunner
  - main [ref=f1e7]:
    - generic [ref=f1e8]:
      - generic [ref=f1e10]:
        - heading "You’re signed in" [level=1] [ref=f1e11]
        - paragraph [ref=f1e12]: You are signed in to Tunner.
      - complementary [ref=f1e13]:
        - strong [ref=f1e14]: Your Tunner account
        - paragraph [ref=f1e15]: For your security, sign out when you finish on a shared device.
      - button "Sign out" [ref=f1e16] [cursor=pointer]
      - link "Account security" [active] [ref=f1e18] [cursor=pointer]:
        - /url: /account/security
      - paragraph [ref=f1e20]: Signing out ends your Tunner session. Connected products manage their own sessions.
  - contentinfo [ref=f1e21]:
    - link "Privacy Policy" [ref=f1e22] [cursor=pointer]:
      - /url: https://fixture.example.test/privacy
    - generic [ref=f1e23]: •
    - link "Terms of Service" [ref=f1e24] [cursor=pointer]:
      - /url: https://fixture.example.test/terms
```

# Test source

```ts
  1  | import {expect,test} from '@playwright/test';
  2  | import AxeBuilder from '@axe-core/playwright';
  3  | import {setup} from './fixtures';
  4  | import {complete,content} from '../registration/support';
  5  |
  6  | async function readyFixture(page:Parameters<typeof setup>[0],missingPublication=false){
  7  |   const state=await setup(page,{authenticated:false});
  8  |   const readyContent={...content};if(missingPublication)delete readyContent['registration.ready.signIn'];
  9  |   await page.route('**/registration/options',route=>route.fulfill({json:{csrfToken:'synthetic-registration-csrf',serverTime:new Date().toISOString(),policyVersion:'synthetic',termsVersion:'synthetic',privacyVersion:'synthetic',passwordMinimumLength:15,passwordMaximumLength:128,proofDigits:6,locale:'en',content:readyContent,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:'https://fixture.example.test/support'}}));
  10 |   await page.route('**/registration/progress',route=>route.fulfill({json:complete()}));
  11 |   return state;
  12 | }
  13 |
  14 | for(const width of [1440,390,320])test('Approved Ready and SignedIn navigation retains canonical guarded chain, keyboard and geometry at '+width,async({page},testInfo)=>{
  15 |   await page.setViewportSize({width,height:1024});const state=await readyFixture(page);await page.goto('/registration/complete');await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();expect(state.authenticated).toBe(false);expect(state.assertions).toBe(0);const signIn=page.getByRole('link',{name:'Sign in',exact:true});await expect(signIn).toHaveAttribute('href','/login');expect(await signIn.evaluate(element=>element.getBoundingClientRect().height)).toBe(48);if(width===1440){expect(await signIn.evaluate(element=>element.getBoundingClientRect().width)).toBe(420);expect(await page.locator('.registration-card.ready').evaluate(element=>element.getBoundingClientRect().height)).toBe(612);}await signIn.focus();await expect(signIn).toBeFocused();expect(await signIn.evaluate(element=>getComputedStyle(element).outlineStyle)).not.toBe('none');await page.screenshot({path:testInfo.outputPath('ready-navigation-'+width+'.png'),fullPage:true});expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);await page.keyboard.press('Enter');await expect(page).toHaveURL(/\/login$/);await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();
> 16 |   await page.getByRole('button',{name:'Sign in with passkey',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);await expect(page.getByRole('heading',{name:'You’re signed in'})).toBeVisible();expect(state.assertions).toBe(1);const security=page.getByRole('link',{name:'Account security',exact:true});await expect(security).toHaveAttribute('href','/account/security');await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();await expect(page.getByText('Signing out ends your Tunner session. Connected products manage their own sessions.')).toBeVisible();expect(await security.evaluate(element=>element.getBoundingClientRect().height)).toBe(44);if(width===1440){expect(await security.evaluate(element=>element.getBoundingClientRect().width)).toBe(418);expect(await page.locator('.authentication-card.signed-in').evaluate(element=>element.getBoundingClientRect().height)).toBe(588);}await security.focus();await expect(security).toBeFocused();expect(await security.evaluate(element=>getComputedStyle(element).outlineStyle)).not.toBe('none');await page.screenshot({path:testInfo.outputPath('signed-in-navigation-'+width+'.png'),fullPage:true});expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.keyboard.press('Enter');await expect(page).toHaveURL(/\/account\/security$/);await expect(page.getByRole('heading',{name:'Security',exact:true})).toBeVisible();await page.getByRole('link',{name:'Review passkeys',exact:true}).click();await expect(page).toHaveURL(/\/account\/security\/sign-in-methods$/);await page.goBack();await expect(page).toHaveURL(/\/account\/security$/);await page.reload();await expect(page.getByRole('heading',{name:'Security',exact:true})).toBeVisible();expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
     |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           ^ Error: expect(received).not.toBe(expected) // Object.is equality
  17 | });
  18 |
  19 | test('The Account security continuation remains protected after the authoritative session ends',async({page})=>{
  20 |   const state=await setup(page);await page.goto('/signed-in');await expect(page.getByRole('link',{name:'Account security',exact:true})).toBeVisible();state.authenticated=false;await page.getByRole('link',{name:'Account security',exact:true}).click();await expect(page).toHaveURL(/\/login$/);await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await expect(page.getByRole('heading',{name:'Security',exact:true})).toHaveCount(0);
  21 | });
  22 |
  23 | test('Missing navigation publication fails safely without invented link text',async({page})=>{
  24 |   await readyFixture(page,true);await page.goto('/registration/complete');await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.getByRole('link',{name:'Sign in',exact:true})).toHaveCount(0);
  25 | });
  26 |
```
