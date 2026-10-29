import { test, expect } from '@playwright/test';
import type { TestOperation } from './support';
import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/content.proposal.json' with { type:'json' };
import { registrationRoutes } from '../../apps/user/routes';
import { pending, personal, complete, boot, fill, beginVerification, openPersonal, fillPersonal } from './support';
const headings={register:'Create your Tunner account',verify:'Check your email',personal:'Personal Information',consent:'Additional consent required',ready:'Your email is verified'};
const sleep=(ms:number)=>new Promise(resolve=>setTimeout(resolve,ms));
async function accessible(page:Page,name:string){
 await page.evaluate(()=>document.fonts.ready);const report=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(report.violations).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 for(const input of await page.locator('input').all()){const box=await input.boundingBox();if(box)expect(box.width).toBeGreaterThan(0);}
 await page.screenshot({path:'test-results/registration/'+name+'.png',fullPage:true});
}
async function fieldError(page:Page,name:string){const field=page.getByLabel(name,{exact:true});await expect(field).toHaveAttribute('aria-invalid','true');const id=await field.getAttribute('aria-describedby');expect(id).toBeTruthy();const error=page.locator('[id="'+id?.split(' ').at(-1)+'"]');await expect(error).toBeVisible();expect(await error.evaluate(e=>({size:getComputedStyle(e).fontSize,color:getComputedStyle(e).color}))).toEqual({size:'12px',color:'rgb(185, 28, 28)'});return field;}
for(const width of [1440,768,320]){
 test('all five canonical screens: direct entry refresh guard and responsive reference at '+width,async({page})=>{
  test.setTimeout(60000);await page.setViewportSize({width,height:1024});let current:TestOperation|undefined;let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
  await boot(page,'/registration/',()=>current);
  for(const stage of ['register','verify','personal','consent','ready'] as const){
   await test.step(registrationRoutes[stage].id,async()=>{
    current=stage==='register'?undefined:stage==='verify'?pending():stage==='personal'?personal():stage==='consent'?pending(3,'CONSENT_REQUIRED'):complete();
    await page.goto(registrationRoutes[stage].pathname);await expect(page.getByRole('heading',{name:headings[stage],exact:true})).toBeVisible();await expect(page).toHaveURL(new RegExp(registrationRoutes[stage].pathname+'$'));
    await page.reload();await expect(page.getByRole('heading',{name:headings[stage],exact:true})).toBeVisible();await expect(page).toHaveURL(new RegExp(registrationRoutes[stage].pathname+'$'));
    expect(await page.evaluate(()=>history.state)).toBeNull();expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
    await accessible(page,'global-'+stage+'-'+width);
   });
  }expect(posts).toBe(0);
 });
 test('FORM-REGISTRATION empty invalid corrected and server-rejected states at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});let posts=0;let reject=true;await page.route('**/registration/register',async route=>{posts++;await sleep(150);await route.fulfill(reject?{status:400,json:{error_code:'VALIDATION_ERROR',detail:'private-server-detail'}}:{status:202,json:pending()});});await boot(page);
  await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByLabel('Email address',{exact:true})).toBeFocused();
  await fieldError(page,'Email address');await fieldError(page,'Password');await fieldError(page,'Confirm password');await expect(page.getByRole('checkbox')).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.highlighted']);expect(posts).toBe(0);
  await accessible(page,'global-registration-errors-'+width);
  await page.getByLabel('Email address').fill('bad');await page.getByLabel('Email address').blur();await fieldError(page,'Email address');
  await fill(page);await expect(page.getByRole('checkbox')).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);
  await page.locator('form').evaluate((f:HTMLFormElement)=>{f.requestSubmit();f.requestSubmit();});await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.invalid']);expect(posts).toBe(1);await expect(page.getByLabel('Email address')).toHaveValue('fixture@example.test');await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');await expect(page.getByText('private-server-detail')).toHaveCount(0);
  reject=false;await fill(page);await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page).toHaveURL(new RegExp(registrationRoutes.verify.pathname+'$'));expect(posts).toBe(2);
 });
 test('FORM-VERIFY-EMAIL empty format correction server rejection and guarded transition at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});await beginVerification(page);let posts=0;let reject=true;
  await page.route('**/registration/verify',async route=>{posts++;await sleep(150);await route.fulfill(reject?{status:400,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}}:{status:202,json:personal()});});
  await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByLabel('Verification code')).toBeFocused();await fieldError(page,'Verification code');expect(posts).toBe(0);
  await accessible(page,'global-verification-errors-'+width);
  await page.getByLabel('Verification code').fill('１２３４５６');await fieldError(page,'Verification code');await page.getByLabel('Verification code').fill('123456');await expect(page.getByRole('alert')).toHaveCount(0);
  await page.locator('form').evaluate((f:HTMLFormElement)=>{f.requestSubmit();f.requestSubmit();});await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.proof']);expect(posts).toBe(1);await expect(page.getByLabel('Verification code')).toHaveValue('');
  reject=false;await page.getByLabel('Verification code').fill('123456');await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page).toHaveURL(new RegExp(registrationRoutes.personal.pathname+'$'));expect(posts).toBe(2);expect(new URL(page.url()).search).toBe('');
 });
 test('FORM-PERSONAL-INFORMATION required calendar safe correction preserved names and server errors at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});await beginVerification(page);await openPersonal(page);let posts=0;let reject=true;
  await page.route('**/registration/personal-information',async route=>{posts++;await sleep(150);await route.fulfill(reject?{status:400,json:{error_code:'VALIDATION_ERROR'}}:{status:202,json:pending(3,'CONSENT_REQUIRED')});});
  await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByLabel('Date of birth (required)')).toBeFocused();await fieldError(page,'Date of birth (required)');expect(posts).toBe(0);
  await accessible(page,'global-personal-errors-'+width);
  await page.getByLabel('Date of birth (required)').fill('2099-01-01');await fieldError(page,'Date of birth (required)');await fillPersonal(page);await expect(page.getByRole('alert')).toHaveCount(0);
  await page.locator('form').evaluate((f:HTMLFormElement)=>{f.requestSubmit();f.requestSubmit();});await expect(page.getByRole('alert')).toHaveText(proposal.content['registration.error.invalid']);expect(posts).toBe(1);await expect(page.getByLabel('Full name',{exact:true})).toHaveValue('Fixture Person');await expect(page.getByLabel('Display name',{exact:true})).toHaveValue('Fixture');await expect(page.getByLabel('Primary email')).toHaveValue('fixture@example.test');await expect(page.getByLabel('Primary email')).toHaveAttribute('readonly','');await expect(page.getByLabel('Date of birth (required)')).toHaveValue('');
  reject=false;await page.getByLabel('Date of birth (required)').fill('2015-01-01');await expect(page.getByRole('alert')).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page).toHaveURL(new RegExp(registrationRoutes.consent.pathname+'$'));expect(posts).toBe(2);
 });
}
test('browser history reconstructs server stages including duplicate path entries and restart',async({page})=>{
 let current:TestOperation|undefined;let reads=0;await page.route('**/registration/register',route=>{current=pending();return route.fulfill({status:202,json:current});});await page.route('**/registration/verify',route=>{current=personal();return route.fulfill({status:202,json:current});});await page.route('**/registration/restart',route=>{current=undefined;return route.fulfill({json:{registrationKey:crypto.randomUUID()}});});
 await boot(page,'/registration/',()=>{reads++;return current;});await fill(page);await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page).toHaveURL(/verify-email$/);
 const before=reads;await page.goBack();await expect(page).toHaveURL(/verify-email$/);await expect.poll(()=>reads).toBeGreaterThan(before);const again=reads;await page.goForward();await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();await expect.poll(()=>reads).toBeGreaterThan(again);
 await page.getByLabel('Verification code').fill('123456');await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();await expect(page).toHaveURL(/personal-information$/);await page.goBack();await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();await expect(page).toHaveURL(/personal-information$/);
 current=pending();await page.goto('/registration/verify-email');await expect(page.getByRole('button',{name:'Go back',exact:true})).toBeVisible();await page.getByRole('button',{name:'Go back',exact:true}).click();await expect(page).toHaveURL(new RegExp(registrationRoutes.register.pathname+'$'));await expect(page.getByRole('heading',{name:headings.register})).toBeVisible();
});
test('unauthorized purged and forged history stages redirect safely; query/hash never grant a screen or redirect',async({page})=>{
 await boot(page);for(const stage of ['verify','personal','consent','ready'] as const){await page.goto(registrationRoutes[stage].pathname);await expect(page).toHaveURL(new RegExp(registrationRoutes.register.pathname+'$'));await expect(page.getByRole('heading',{name:headings.register})).toBeVisible();await expect(page.locator('input[type=date]')).toHaveCount(0);}
 await page.evaluate(()=>{history.pushState({stage:'ready'},'', '/registration/complete');dispatchEvent(new PopStateEvent('popstate'));});await expect(page).toHaveURL(new RegExp(registrationRoutes.register.pathname+'$'));expect(await page.evaluate(()=>history.state)).toBeNull();
 const external:string[]=[];page.on('request',r=>{if(r.url().startsWith('https://outside.example.test'))external.push(r.url());});await page.goto('/registration?redirect=https%3A%2F%2Foutside.example.test%2F#ready');await expect(page).toHaveURL(new RegExp(registrationRoutes.register.pathname+'$'));await expect(page.getByRole('heading',{name:headings.register})).toBeVisible();expect(external).toEqual([]);
});
