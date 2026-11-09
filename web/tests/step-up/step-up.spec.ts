import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
const initialId='12121212-3434-4567-8989-121212121212';
async function setup(page:Page){
 const content=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'synthetic',versionRef:'synthetic',publicationRef:'SYNTHETIC-TEST-ONLY'}]));
 const state={completed:false,allowed:true,id:initialId,posts:0};
 await page.route('**/authentication/step-up/options',route=>route.fulfill({json:state.completed?{status:'COMPLETED'}:{status:'PENDING',requestId:state.id,csrfToken:'fixture-authenticated-csrf',passwordMaximumLength:128,passwordAvailable:state.allowed,locale:'en',content}}));
 await page.route('**/authentication/session',route=>route.fulfill({json:{authenticated:true}}));
 return state;
}
async function open(page:Page){await page.goto('/browser-testing/step-up-assets/');await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();}
async function password(page:Page){await page.getByRole('button',{name:'Other method',exact:true}).click();await expect(page.getByLabel('Password',{exact:true})).toBeFocused();}
async function evidence(page:Page,name:string){
 await page.evaluate(()=>document.fonts.ready);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.screenshot({path:'test-results/step-up/'+name+'.png',fullPage:true});
}
for(const width of [1440,1024,768,390,320]){
 test('Approved chooser and password layout accessibility at '+width,async({page})=>{
  await page.setViewportSize({width,height:1100});await setup(page);await open(page);
  await expect(page.getByRole('button',{name:/^Passkey/})).toBeDisabled();await expect(page.getByRole('button',{name:/^Authenticator/})).toBeDisabled();
  if(width===1440){const box=await page.getByRole('dialog').boundingBox();expect(box?.x).toBeCloseTo(470,0);expect(box?.y).toBeCloseTo(300,0);expect(box?.width).toBeCloseTo(500,0);expect(box?.height).toBeCloseTo(410,0);}
  await evidence(page,'chooser-'+width);await password(page);await evidence(page,'password-'+width);
 });
 test('Empty blur correction server rejection and field focus at '+width,async({page})=>{
  await page.setViewportSize({width,height:1024});const state=await setup(page);
  await page.route('**/authentication/step-up/password',route=>{state.posts++;return route.fulfill({status:401,json:{error_code:'AUTHENTICATION_FAILED'}});});
  await open(page);await password(page);await page.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(page.getByLabel('Password',{exact:true})).toBeFocused();expect(state.posts).toBe(0);await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.highlighted']);
  await page.getByLabel('Password',{exact:true}).fill('old');await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByRole('button',{name:'Confirm',exact:true}).click();expect(state.posts).toBe(1);
  const field=page.getByLabel('Password',{exact:true});await expect(field).toHaveValue('');await expect(field).toHaveAttribute('aria-invalid','true');await expect(field).toBeFocused();
  const description=await field.getAttribute('aria-describedby');expect(description).toBeTruthy();await expect(page.locator('[id="'+description+'"]')).toHaveText(proposal.content['stepUp.error.credentials']);
  await evidence(page,'server-error-'+width);
 });
}
test('Modal keyboard containment Escape and focus restoration do not submit an action',async({page})=>{
 const state=await setup(page);await open(page);await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Other method',exact:true})).toBeFocused();
 await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeFocused();
 await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Other method',exact:true})).toBeFocused();
 await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('button',{name:'Verify identity',exact:true})).toBeFocused();expect(state.posts).toBe(0);
});
test('Pending submission blocks duplicates Back Cancel and Escape; completion resumes once',async({page})=>{
 const state=await setup(page);let release:()=>void=()=>{};const wait=new Promise<void>(resolve=>{release=resolve;});
 await page.route('**/authentication/step-up/password',async route=>{state.posts++;expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-authenticated-csrf');
  expect(route.request().postDataJSON()).toEqual({requestId:initialId,password:'fixture current password'});await wait;state.completed=true;await route.fulfill({json:{status:'COMPLETED'}});});
 await open(page);await password(page);await page.getByLabel('Password',{exact:true}).fill('fixture current password');
 await page.locator('dialog form').evaluate(element=>{const form=element as HTMLFormElement;form.requestSubmit();form.requestSubmit();});
 await expect(page.getByRole('button',{name:'Confirm',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Back',exact:true})).toBeDisabled();
 await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toBeVisible();expect(state.posts).toBe(1);release();
 await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Verification completed');expect(state.posts).toBe(1);
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash}))).toEqual({local:0,session:0,search:'',hash:''});
});
test('Lost success response reconciles authoritative owner completion instead of resubmission',async({page})=>{
 const state=await setup(page);await page.route('**/authentication/step-up/password',route=>{state.posts++;state.completed=true;return route.abort();});
 await open(page);await password(page);await page.getByLabel('Password',{exact:true}).fill('fixture existing password');await page.getByRole('button',{name:'Confirm',exact:true}).click();
 await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Verification completed');expect(state.posts).toBe(1);
});
for(const code of ['VERSION_CONFLICT','FORBIDDEN_SCOPE','AUTHENTICATION_REQUIRED']){
 test('Changed or expired authority never resumes the action: '+code,async({page})=>{
  const state=await setup(page);await page.route('**/authentication/step-up/password',route=>{state.posts++;return route.fulfill({status:code==='AUTHENTICATION_REQUIRED'?401:code==='VERSION_CONFLICT'?409:403,json:{error_code:code}});});
  await open(page);await password(page);await page.getByLabel('Password',{exact:true}).fill('fixture password');await page.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.changed']);await expect(page.getByLabel('Password',{exact:true})).toHaveCount(0);expect(state.posts).toBe(1);await evidence(page,'changed-'+code);
 });
}
test('Insufficient policy disables Other method without offering weaker verification',async({page})=>{
 const state=await setup(page);state.allowed=false;await open(page);await expect(page.getByRole('button',{name:'Other method',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Cancel',exact:true}).click();expect(state.posts).toBe(0);
});
test('Long password is independently rejected and Back clears credentials without changing URL',async({page})=>{
 const state=await setup(page);await open(page);const original=page.url();await password(page);await page.getByLabel('Password',{exact:true}).fill('x'.repeat(129));
 await page.getByRole('button',{name:'Confirm',exact:true}).click();await expect(page.getByLabel('Password',{exact:true})).toBeFocused();expect(state.posts).toBe(0);
 await page.getByRole('button',{name:'Back',exact:true}).click();await password(page);await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');expect(page.url()).toBe(original);
});
test('Short viewport remains scrollable and keyboard-reachable',async({page})=>{
 await page.setViewportSize({width:320,height:360});await setup(page);await open(page);await password(page);
 await page.getByRole('button',{name:'Confirm',exact:true}).focus();await expect(page.getByRole('button',{name:'Confirm',exact:true})).toBeInViewport();await evidence(page,'short-viewport');
});

test('Keyboard blur still validates before a button is activated',async({page})=>{
 await setup(page);await open(page);await password(page);await page.keyboard.press('Tab');await expect(page.getByLabel('Password',{exact:true})).toHaveAttribute('aria-invalid','true');await expect(page.getByRole('button',{name:'Back',exact:true})).toBeFocused();
});
