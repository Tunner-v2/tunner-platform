import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/authentication.content.proposal.json' with {type:'json'};

const requestId='12121212-3434-4567-8989-121212121212',attemptId='23232323-4545-4678-8989-232323232323';
const operation=(status:string,extra:Record<string,unknown>={})=>({attemptId,requestId,status,errorCode:null,...extra});
async function setup(page:Page,pending:unknown=null,configuration:Record<string,unknown>|null={googleAvailable:true,microsoftAvailable:true},omitNewContent=false){
 const content=Object.fromEntries(Object.entries(proposal.content).filter(([key])=>!omitNewContent||!key.startsWith('federation.')).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'fixture-variant',versionRef:'fixture-version',publicationRef:'fixture-only-publication'}]));
 const state={authenticated:false,pending,options:0,sessionStatus:401,start:0,complete:0,result:0,cancel:0,challenges:0,idempotencyKey:requestId};
 await page.route('**/authentication/options',route=>{state.options++;return route.fulfill({json:{csrfToken:'fixture-anonymous-csrf',idempotencyKey:state.idempotencyKey,passwordMaximumLength:128,locale:'en',content,termsUri:'https://fixture.example.test/terms',privacyUri:'https://fixture.example.test/privacy',supportUri:null,...(configuration?{federation:{...configuration,pending:state.pending}}:{})}})});
 await page.route('**/authentication/session',route=>route.fulfill(state.authenticated?{json:{authenticated:true,csrfToken:'fixture-current-principal-csrf'}}:{status:state.sessionStatus,json:{error_code:'AUTHENTICATION_REQUIRED'}}));
 return state;
}
async function noCredentialPosts(page:Page){let count=0;await page.route('**/authentication/sign-in',route=>{count++;return route.fulfill({status:500})});return ()=>count;}

for(const configuration of [null,{googleAvailable:false,microsoftAvailable:false}])test((configuration?'Disabled':'Unset')+' federation does not require new publications or activate old provider slots',async({page})=>{
 const state=await setup(page,null,configuration,true);await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();
 await expect(page.getByRole('button',{name:'Google',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Microsoft',exact:true})).toBeDisabled();
 expect(state.start).toBe(0);await expect(page.locator('[data-tunner-component="COMP-FEDERATION-SIGN-IN"]')).toHaveCount(0);
});
test('An enabled capability requires its published feedback and fails closed when absent',async({page})=>{
 await setup(page,null,{googleAvailable:true,microsoftAvailable:false},true);await page.goto('/login');await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.getByRole('button',{name:'Google',exact:true})).toHaveCount(0);
});
for(const provider of ['Google','Microsoft'] as const)test(provider+' starts without credential validation and navigates only its exact fixed clean challenge',async({page})=>{
 const state=await setup(page,null,{googleAvailable:provider==='Google',microsoftAvailable:provider==='Microsoft'});const passwordPosts=await noCredentialPosts(page);
 let body:Record<string,unknown>|undefined;
 await page.route('**/authentication/federation/start',async route=>{state.start++;body=route.request().postDataJSON();expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-anonymous-csrf');await route.fulfill({json:{authenticated:false,operation:operation('PREPARED',{requestId:body!.requestId}),challengePath:'/authentication/federation/'+provider.toLowerCase()+'/challenge'}})});
 await page.route('**/authentication/federation/'+provider.toLowerCase()+'/challenge',route=>{state.challenges++;return route.fulfill({contentType:'text/html',body:'<!doctype html><title>Public challenge boundary</title><p>Fixed local challenge</p>'})});
 await page.goto('/login');await page.getByRole('checkbox',{name:'Remember me'}).check();await page.getByRole('button',{name:provider,exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5189/authentication/federation/'+provider.toLowerCase()+'/challenge');
 expect(body).toEqual({requestId:expect.any(String),provider,rememberMe:true});expect(Object.keys(body!).sort()).toEqual(['provider','rememberMe','requestId']);expect(state.start).toBe(1);expect(state.challenges).toBe(1);expect(passwordPosts()).toBe(0);
 expect(await page.evaluate(()=>({search:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({search:'',hash:'',local:0,session:0});
});
test('A current principal supplies Start CSRF only and does not imply sign-in success',async({page})=>{
 const state=await setup(page);state.authenticated=true;
 await page.route('**/authentication/federation/start',route=>{state.start++;expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-principal-csrf');return route.abort('connectionreset')});
 await page.goto('/login');const started=page.waitForRequest(request=>request.method()==='POST'&&request.url().endsWith('/authentication/federation/start'));
 await page.getByRole('button',{name:'Google',exact:true}).click();await started;await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeEnabled();await expect(page).toHaveURL(/\/login$/);expect(state.start).toBe(1);
});
test('Non401 session failure does not rotate anonymous context or prepare a new operation',async({page})=>{
 const state=await setup(page);state.sessionStatus=503;
 await page.route('**/authentication/federation/start',route=>{state.start++;return route.fulfill({status:500})});
 await page.goto('/login');await expect(page.getByRole('button',{name:'Google',exact:true})).toBeEnabled();const before=state.options;
 await page.getByRole('button',{name:'Google',exact:true}).click();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();expect(state.options).toBe(before);expect(state.start).toBe(0);
});
for(const badPath of ['/authentication/federation/microsoft/challenge','/authentication/federation/google/challenge?attempt=not-allowed','https://fixture.example.test/challenge'])test('Start rejects unbound challenge path '+badPath,async({page})=>{
 const state=await setup(page);await page.route('**/authentication/federation/start',route=>{state.start++;const body=route.request().postDataJSON();return route.fulfill({json:{authenticated:false,operation:operation('PREPARED',{requestId:body.requestId}),challengePath:badPath}})});
 await page.goto('/login');const started=page.waitForResponse(response=>response.request().method()==='POST'&&response.url().endsWith('/authentication/federation/start'));
 await page.getByRole('button',{name:'Google',exact:true}).click();await started;await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeEnabled();await expect(page).toHaveURL(/\/login$/);expect(state.start).toBe(1);
});
test('Restored VERIFIED requires explicit completion with exact original result and current CSRF',async({page})=>{
 const state=await setup(page,operation('VERIFIED'));state.authenticated=true;
 await page.route('**/authentication/federation/complete',route=>{state.complete++;expect(route.request().postDataJSON()).toEqual({});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-principal-csrf');return route.fulfill({json:{authenticated:true,operation:operation('COMPLETED')}})});
 await page.goto('/login');await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toBeVisible();expect(state.complete).toBe(0);await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Complete sign-in',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(state.complete).toBe(1);
});
for(const status of ['UNKNOWN','PREPARED','CHALLENGE_ADMITTED','COMPLETED'])test('Restored '+status+' never reissues a challenge or infers a surviving session outcome',async({page})=>{
 const state=await setup(page,operation(status));state.authenticated=true;
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});return route.fulfill({json:{authenticated:false,operation:operation(status)}})});
 await page.route('**/authentication/federation/start',route=>{state.start++;return route.fulfill({status:500})});
 await page.route('**/authentication/federation/complete',route=>{state.complete++;return route.fulfill({status:500})});
 await page.goto('/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();expect(state.result).toBe(0);await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Check status',exact:true}).click();await expect.poll(()=>state.result).toBe(1);await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeEnabled();await expect(page).toHaveURL(/\/login$/);expect(state.start).toBe(0);expect(state.complete).toBe(0);
});
test('VERIFIED with an error classification remains unresolved instead of enabling completion',async({page})=>{
 await setup(page,operation('VERIFIED',{errorCode:'DEPENDENCY_UNAVAILABLE'}));await page.goto('/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toHaveCount(0);
});
for(const mismatch of ['requestId','attemptId'])test('A different '+mismatch+' cannot complete the retained federation operation',async({page})=>{
 const state=await setup(page,operation('VERIFIED'));state.authenticated=true;
 await page.route('**/authentication/federation/complete',route=>{state.complete++;return route.fulfill({json:{authenticated:true,operation:operation('COMPLETED',{[mismatch]:'34343434-5656-4789-8989-343434343434'})}})});
 await page.goto('/login');await page.getByRole('button',{name:'Complete sign-in',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['federation.signIn.changed']);await expect(page).toHaveURL(/\/login$/);await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();expect(state.complete).toBe(1);
});
test('Lost completion is not retried; explicit exact-result check recovers with fresh principal CSRF',async({page})=>{
 const state=await setup(page,operation('VERIFIED'));
 await page.route('**/authentication/federation/complete',route=>{state.complete++;state.authenticated=true;return route.abort('connectionreset')});
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-principal-csrf');return route.fulfill({json:{authenticated:true,operation:operation('COMPLETED')}})});
 await page.goto('/login');await page.getByRole('button',{name:'Complete sign-in',exact:true}).click();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();expect(state.complete).toBe(1);expect(state.result).toBe(0);
 await page.getByRole('button',{name:'Check status',exact:true}).click();await expect(page).toHaveURL(/\/signed-in$/);expect(state.result).toBe(1);expect(state.complete).toBe(1);
});
test('MFA_REQUIRED preserves the existing additional-verification handoff instead of ordinary signed-in success',async({page})=>{
 const state=await setup(page,operation('VERIFIED'));
 await page.route('**/authentication/federation/complete',route=>{state.complete++;return route.fulfill({json:{status:'MFA_REQUIRED',authenticated:false,requestId,expiresAt:'2099-01-01T00:00:00Z'}})});
 await page.route('**/authentication/totp/options',route=>route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}}));
 await page.goto('/login');await page.getByRole('button',{name:'Complete sign-in',exact:true}).click();await expect(page).toHaveURL(/\/login\/verify-authenticator$/);expect(state.complete).toBe(1);expect(state.authenticated).toBe(false);
});
test('Terminal acknowledgement clears only the original pointer before returning to ordinary Login',async({page})=>{
 const state=await setup(page,operation('EXPIRED'));
 await page.route('**/authentication/federation/cancel',route=>{state.cancel++;expect(route.request().postDataJSON()).toEqual({});state.pending=null;return route.fulfill({json:{authenticated:false,operation:operation('EXPIRED')}})});
 await page.goto('/login');await expect(page.getByText(proposal.content['federation.signIn.expiredDescription'])).toBeVisible();await page.getByRole('button',{name:'Back to sign in',exact:true}).click();await expect(page.getByRole('button',{name:'Google',exact:true})).toBeEnabled();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeEnabled();expect(state.cancel).toBe(1);
});
test('In-flight provider preparation prevents credential and duplicate-provider submissions',async({page})=>{
 const state=await setup(page),passwordPosts=await noCredentialPosts(page);let release:()=>void=()=>{};const gate=new Promise<void>(resolve=>{release=resolve});
 await page.route('**/authentication/federation/start',async route=>{state.start++;await gate;await route.abort('connectionreset')});
 await page.goto('/login');await page.getByRole('button',{name:'Google',exact:true}).evaluate(button=>{(button as HTMLButtonElement).click();(button as HTMLButtonElement).click()});
 await expect.poll(()=>state.start).toBe(1);await expect(page.getByRole('button',{name:'Microsoft',exact:true})).toBeDisabled();await expect(page.getByLabel('Password',{exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();
 release();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeEnabled();expect(state.start).toBe(1);expect(passwordPosts()).toBe(0);
});
test('Route revision aborts/fences an old completion response before a new Login entry',async({page})=>{
 const state=await setup(page,operation('VERIFIED'));let release:()=>void=()=>{};const gate=new Promise<void>(resolve=>{release=resolve});
 await page.route('**/authentication/federation/complete',async route=>{state.complete++;await gate;try{await route.fulfill({json:{authenticated:true,operation:operation('COMPLETED')}})}catch{/* The old request was aborted by navigation. */}});
 await page.goto('/login');await page.getByRole('button',{name:'Complete sign-in',exact:true}).click();await expect.poll(()=>state.complete).toBe(1);state.pending=null;
 await page.evaluate(()=>{history.pushState(null,'','/login');dispatchEvent(new PopStateEvent('popstate'))});await expect(page.getByRole('button',{name:'Google',exact:true})).toBeEnabled();release();await expect(page.getByRole('button',{name:'Google',exact:true})).toBeEnabled();await expect(page).toHaveURL(/\/login$/);
});
for(const width of [1440,768,390,320])test('Published federation recovery preserves Login assets accessible controls and layout at '+width,async({page})=>{
 await page.setViewportSize({width,height:1024});await setup(page,operation('VERIFIED'));await page.goto('/login');await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toBeVisible();await page.evaluate(()=>document.fonts.ready);
 expect(await page.locator('.authentication-glow,.authentication-providers img').evaluateAll(nodes=>nodes.every(node=>{const image=node as HTMLImageElement;return image.complete&&image.naturalWidth>0}))).toBe(true);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 await page.getByRole('button',{name:'Complete sign-in',exact:true}).focus();await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toBeFocused();await page.screenshot({path:'test-results/authentication/federation-recovery-'+width+'.png',fullPage:true});
});

test('V01 focused canonical reload and Back Forward restore only the original pending federation context',async({page})=>{
 const state=await setup(page,operation('UNKNOWN'));state.authenticated=true;
 const credentialPosts=await noCredentialPosts(page);
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-principal-csrf');return route.fulfill({json:{authenticated:false,operation:operation('UNKNOWN')}})});
 await page.route('**/authentication/federation/start',route=>{state.start++;return route.fulfill({status:500})});
 await page.route('**/authentication/federation/complete',route=>{state.complete++;return route.fulfill({status:500})});
 await page.route(/\/authentication\/federation\/(google|microsoft)\/challenge$/,route=>{state.challenges++;return route.fulfill({status:500})});
 await page.goto('/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();
 const beforeReload=state.options;state.idempotencyKey='45454545-6767-4890-8989-454545454545';await page.reload();
 await expect(page).toHaveURL('http://127.0.0.1:5189/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect.poll(()=>state.options).toBeGreaterThan(beforeReload);
 // This explicit destination is the unrelated current session, never the federation attempt's success.
 await page.goto('/signed-in');await expect(page.getByRole('heading',{name:'You’re signed in',exact:true})).toBeVisible();
 const beforeBack=state.options;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5189/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect.poll(()=>state.options).toBeGreaterThan(beforeBack);
 await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5189/signed-in');await expect(page.getByRole('heading',{name:'You’re signed in',exact:true})).toBeVisible();
 const beforeSecondBack=state.options;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5189/login');await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeVisible();await expect.poll(()=>state.options).toBeGreaterThan(beforeSecondBack);
 expect(state.result).toBe(0);await expect(page.getByRole('button',{name:'Complete sign-in',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Check status',exact:true}).click();await expect.poll(()=>state.result).toBe(1);await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeEnabled();await expect(page).toHaveURL('http://127.0.0.1:5189/login');
 expect(state.pending).toEqual(operation('UNKNOWN'));expect(state.start).toBe(0);expect(state.complete).toBe(0);expect(state.challenges).toBe(0);expect(credentialPosts()).toBe(0);
 expect(await page.evaluate(()=>({search:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({search:'',hash:'',local:0,session:0});
});

for(const status of ['UNKNOWN','REJECTED','EXPIRED'] as const)test('V01 focused '+status+' feedback and keyboard controls at desktop and narrow mobile',async({page},testInfo)=>{
 const state=await setup(page,operation(status));const credentialPosts=await noCredentialPosts(page);
 await page.route('**/authentication/federation/result',route=>{state.result++;expect(route.request().postDataJSON()).toEqual({});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-anonymous-csrf');return route.fulfill({json:{authenticated:false,operation:operation(status)}})});
 await page.route('**/authentication/federation/cancel',route=>{state.cancel++;expect(route.request().postDataJSON()).toEqual({});state.pending=null;return route.fulfill({json:{authenticated:false,operation:operation(status)}})});
 for(const width of [1440,320]){
  state.pending=operation(status);await page.setViewportSize({width,height:1024});await page.goto('/login');
  const first=page.getByRole('button',{name:status==='UNKNOWN'?'Check status':'Back to sign in',exact:true});await expect(first).toBeEnabled();
  const description=status==='UNKNOWN'?'federation.signIn.unknownDescription':status==='EXPIRED'?'federation.signIn.expiredDescription':'federation.signIn.notCompletedDescription';
  await expect(page.getByText(proposal.content[description],{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeDisabled();
  await page.evaluate(()=>document.fonts.ready);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  await first.focus();await expect(first).toBeFocused();const picture=testInfo.outputPath('federation-'+status.toLowerCase()+'-'+width+'.png');await page.screenshot({path:picture,fullPage:true});await testInfo.attach('federation-'+status.toLowerCase()+'-'+width,{path:picture,contentType:'image/png'});
  if(status==='UNKNOWN'){
   await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(first).toBeFocused();
   const before=state.result;await page.keyboard.press('Enter');await expect.poll(()=>state.result).toBe(before+1);await expect(first).toBeEnabled();await expect(page).toHaveURL('http://127.0.0.1:5189/login');expect(state.cancel).toBe(0);
  }else{
   const before=state.cancel;await page.keyboard.press('Enter');await expect.poll(()=>state.cancel).toBe(before+1);await expect(page.getByRole('button',{name:'Google',exact:true})).toBeEnabled();await expect(page.getByLabel('Password',{exact:true})).toBeEnabled();expect(state.pending).toBeNull();
  }
 }
 expect(credentialPosts()).toBe(0);expect(state.start).toBe(0);expect(state.complete).toBe(0);
});
