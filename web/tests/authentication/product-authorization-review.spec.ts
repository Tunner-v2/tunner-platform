import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {join} from 'node:path';
import loginProposal from '../../apps/user/authentication.content.proposal.json' with {type:'json'};

// Synthetic published fixtures qualify only the actual browser UI. They are not production publication or native issuance evidence.
const origin='http://127.0.0.1:5189';
const evidenceDirectory=fileURLToPath(new URL('../../../artifacts/p2-014-authorization-20261115/ui-evidence/',import.meta.url));
const reviewCopy={
 'brand.tunner':'Tunner','authorization.heading':'Continue to {productName}',
 'authorization.description':'Review what you’ll share before you continue.',
 'authorization.product':'Product','authorization.account':'Signed in to Tunner',
 'authorization.permissions':'{productName} will receive','authorization.assurance':'Sign-in requirements',
 'authorization.callback':'Return to','authorization.allow':'Allow','authorization.deny':'Deny',
 'authorization.switchAccount':'Use another account','authorization.working':'Please wait…',
 'authorization.changed':'This request has changed or expired. Return to the product to start again.',
 'authorization.unavailable':'We couldn’t complete this request. Return to the product and try again.',
 'authorization.proofRequired':'Additional sign-in verification is needed before you can continue.',
 'authorization.loginRequired':'Sign in again before you continue.',
 'authorization.boundary':'Tunner handles your identity. Product roles, workspaces, and permissions remain inside {productName}.',
 'authorization.footer.privacy':'Privacy Policy','authorization.footer.terms':'Terms of Service','authorization.footer.support':'Contact Support',
 'authorization.loginDescription':'Sign in with your Tunner account to continue.',
 'authorization.loginPermissions':'{productName} is requesting','authorization.loginSummary':'You’ll review this request after signing in.'
};
function published(values:Record<string,string>){return Object.fromEntries(Object.entries(values).map(([key,text])=>[key,{key,text,state:'PUBLISHED' as const,locale:'en',variantRef:'review-fixture-variant',versionRef:'review-fixture-version',publicationRef:'synthetic-review-only-publication'}]));}
function options(overrides:Record<string,unknown>={}){return {reference:'synthetic-exact-review-reference',csrfToken:'synthetic-review-csrf',canAllow:true,interactionStatus:'Satisfied',locale:'en',content:published(reviewCopy),productName:'Product A',accountLabel:'Alex Example',registeredReturnOrigin:'https://product-a.example',requestedAssuranceLabel:'Your current sign-in meets this request’s requirements.',issuedClaimNames:['sub'],scopeDescriptions:published({openid:'Your Tunner identity'}),termsUri:'https://fixture.example/terms',privacyUri:'https://fixture.example/privacy',supportUri:'https://fixture.example/support',...overrides};}
async function setupReview(page:Page,value=options()){
 const state={reads:0,posts:[] as unknown[],nativeEntries:0,loginEntries:0};
 await page.route('**/product-authorization/options',route=>{state.reads++;expect(route.request().method()).toBe('GET');return route.fulfill({json:value});});
 await page.route('**/product-authorization/decision',route=>{expect(route.request().method()).toBe('POST');expect(route.request().headers()['x-tunner-csrf']).toBe(value.csrfToken);state.posts.push(route.request().postDataJSON());return route.fulfill({json:{resumePath:'/connect/authorize'}});});
 await page.route('**/connect/authorize',route=>{state.nativeEntries++;expect(route.request().isNavigationRequest()).toBe(true);expect(route.request().resourceType()).toBe('document');return route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="en"><title>Native document fixture</title><body><h1>Native authorization fixture</h1></body></html>'});});
 return state;
}
async function setupLogin(page:Page,productContext:boolean){
 const content=published(loginProposal.content);
 const productAuthorization={productName:'Product A',locale:'en',content:published(reviewCopy),scopeDescriptions:published({openid:'Your Tunner identity'})};
 await page.route('**/authentication/options',route=>route.fulfill({json:{csrfToken:'synthetic-login-csrf',idempotencyKey:'12121212-3434-4567-8989-121212121212',passwordMaximumLength:128,locale:'en',content,termsUri:'https://fixture.example/terms',privacyUri:'https://fixture.example/privacy',supportUri:'https://fixture.example/support',...(productContext?{productAuthorization}:{})}}));
 await page.route('**/authentication/session',route=>route.fulfill({status:401,json:{error_code:'AUTHENTICATION_REQUIRED'}}));
}
async function noBrowserLeak(page:Page){expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});}
async function settleFrames(page:Page){await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));}
async function capture(page:Page,name:string){
 await page.evaluate(()=>document.fonts.ready);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 const assets=await page.locator('img').evaluateAll(nodes=>nodes.map(node=>{const image=node as HTMLImageElement;return {source:image.getAttribute('src'),loaded:image.complete&&image.naturalWidth>0};}));
 expect(assets.every(asset=>asset.loaded)).toBe(true);
 expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 const card=page.locator('.product-authorization-card,.authentication-card');const footer=page.locator('.product-authorization-footer,.authentication-footer');
 const cardBox=await card.boundingBox(),footerBox=await footer.boundingBox();expect(cardBox).not.toBeNull();expect(footerBox).not.toBeNull();
 expect(cardBox!.y+cardBox!.height).toBeLessThanOrEqual(footerBox!.y);
 await mkdir(evidenceDirectory,{recursive:true});await page.screenshot({path:join(evidenceDirectory,name+'.png'),fullPage:true});
}

for(const decision of ['ALLOW','DENY','SWITCH_ACCOUNT'] as const){test('Product review '+decision+' submits its exact reference and follows a document pathname',async({page})=>{
 const state=await setupReview(page);
 if(decision==='SWITCH_ACCOUNT')await page.route('**/product-authorization/decision',route=>{expect(route.request().headers()['x-tunner-csrf']).toBe('synthetic-review-csrf');state.posts.push(route.request().postDataJSON());return route.fulfill({json:{resumePath:'/login'}});});
 if(decision==='SWITCH_ACCOUNT')await page.route('**/login',route=>{state.loginEntries++;expect(route.request().isNavigationRequest()).toBe(true);return route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="en"><title>Login document fixture</title><body><h1>Login document fixture</h1></body></html>'});});
 await page.goto('/product-authorization');await expect(page.getByRole('heading',{name:'Continue to Product A'})).toBeVisible();
 await page.getByRole('button',{name:decision==='ALLOW'?'Allow':decision==='DENY'?'Deny':'Use another account',exact:true}).click();
 await expect(page).toHaveURL(origin+(decision==='SWITCH_ACCOUNT'?'/login':'/connect/authorize'));
 await expect(page.getByRole('heading',{name:decision==='SWITCH_ACCOUNT'?'Login document fixture':'Native authorization fixture'})).toBeVisible();
 expect(state.posts).toEqual([{reference:'synthetic-exact-review-reference',decision}]);expect(state.nativeEntries).toBe(decision==='SWITCH_ACCOUNT'?0:1);expect(state.loginEntries).toBe(decision==='SWITCH_ACCOUNT'?1:0);await noBrowserLeak(page);
});}

for(const viewport of [{width:1440,height:1024},{width:390,height:844},{width:320,height:844}]){
 test('Product review actual production composition, keyboard and axe at '+viewport.width,async({page})=>{
  await page.setViewportSize(viewport);await setupReview(page);await page.goto('/product-authorization');
  await expect(page.getByRole('heading',{name:'Continue to Product A'})).toBeFocused();await expect(page.getByText('Alex Example',{exact:true})).toBeVisible();await expect(page.getByText('Your Tunner identity',{exact:true})).toBeVisible();
  await expect(page.getByText('https://product-a.example',{exact:true})).toBeVisible();await expect(page.getByText('Verified email',{exact:true})).toHaveCount(0);await expect(page.getByText('System Status:')).toHaveCount(0);
  await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Deny',exact:true})).toBeFocused();await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeFocused();await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Use another account',exact:true})).toBeFocused();
  await noBrowserLeak(page);await page.getByRole('heading',{name:'Continue to Product A'}).focus();await capture(page,'authorization-production-'+viewport.width);
 });
 test('Product-aware Login retains actual credentials and ordinary Login at '+viewport.width,async({page})=>{
  await page.setViewportSize(viewport);await setupLogin(page,true);let credentialPosts=0;
  await page.route('**/authentication/sign-in',route=>{credentialPosts++;expect(route.request().postDataJSON()).toEqual({idempotencyKey:'12121212-3434-4567-8989-121212121212',email:'person@example.test',rememberMe:false,password:'synthetic existing passphrase'});return route.fulfill({status:401,json:{error_code:'AUTHENTICATION_FAILED'}});});
  await page.goto('/login');await expect(page.getByRole('heading',{name:'Continue to Product A'})).toBeVisible();await expect(page.getByText('You’ll review this request after signing in.')).toBeVisible();await expect(page.getByLabel('Password',{exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'Google',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Microsoft',exact:true})).toBeVisible();await expect(page.getByText('GitHub',{exact:true})).toHaveCount(0);await expect(page.getByText('Verified email',{exact:true})).toHaveCount(0);await capture(page,'product-login-production-'+viewport.width);
  await page.getByLabel('Email address',{exact:true}).fill('person@example.test');await page.getByLabel('Password',{exact:true}).fill('synthetic existing passphrase');await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(loginProposal.content['authentication.error.credentials']);expect(credentialPosts).toBe(1);await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');
  await setupLogin(page,false);await page.reload();await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await expect(page.locator('.authentication-card')).not.toHaveClass(/product-origin/);await expect(page.locator('[data-tunner-component="U-AUTH-PRODUCT-ORIGIN"]')).toHaveCount(0);await noBrowserLeak(page);
 });
}

test('Product review holds loading state and blocks duplicate decision submissions',async({page})=>{
 const state=await setupReview(page);let releaseOptions!:()=>void;const optionsGate=new Promise<void>(resolve=>{releaseOptions=resolve;});
 let releaseDecision:(()=>void)|undefined;
 try{
 await page.route('**/product-authorization/options',async route=>{state.reads++;await optionsGate;await route.fulfill({json:options()});});
 await page.goto('/product-authorization');await expect(page.locator('main[aria-busy="true"]')).toBeAttached();await expect(page.getByRole('button',{name:'Allow',exact:true})).toHaveCount(0);expect(state.posts).toHaveLength(0);releaseOptions();await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();
 const decisionGate=new Promise<void>(resolve=>{releaseDecision=resolve;});
 await page.route('**/product-authorization/decision',async route=>{state.posts.push(route.request().postDataJSON());await decisionGate;await route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}});});
 await page.getByRole('button',{name:'Allow',exact:true}).evaluate(button=>{(button as HTMLButtonElement).click();(button as HTMLButtonElement).click();});
 await expect(page.getByRole('status')).toHaveText('Please wait…');await expect(page.getByRole('button',{name:'Deny',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Use another account',exact:true})).toBeDisabled();await expect.poll(()=>state.posts.length).toBe(1);releaseDecision!();await expect(page.getByRole('alert')).toHaveText(reviewCopy['authorization.unavailable']);expect(state.posts).toHaveLength(1);expect(state.nativeEntries).toBe(0);
 }finally{releaseOptions();releaseDecision?.();}
});

for(const status of ['RequiresFreshProof','RequiresSignIn','Denied'] as const){test('Product review '+status+' never enables Allow or invents a session upgrade',async({page})=>{
 const state=await setupReview(page,options({canAllow:false,interactionStatus:status,issuedClaimNames:[],requestedAssuranceLabel:null}));await page.goto('/product-authorization');
 await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Deny',exact:true})).toBeEnabled();await expect(page.getByRole('status')).toHaveText(reviewCopy[status==='RequiresFreshProof'?'authorization.proofRequired':status==='RequiresSignIn'?'authorization.loginRequired':'authorization.unavailable']);
 await expect(page.getByRole('dialog')).toHaveCount(0);await settleFrames(page);expect(state.posts).toHaveLength(0);expect(state.nativeEntries).toBe(0);await noBrowserLeak(page);
});}

for(const failure of ['changed','network','body','unsafePath'] as const){test('Product review '+failure+' decision response fails safely without retry or redirect',async({page})=>{
 const state=await setupReview(page);
 await page.route('**/product-authorization/decision',route=>{state.posts.push(route.request().postDataJSON());return failure==='changed'?route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}}):failure==='network'?route.abort('connectionreset'):failure==='body'?route.fulfill({status:200,contentType:'application/json',body:'{'}):route.fulfill({json:{resumePath:'/connect/authorize?code=synthetic-secret'}});});
 await page.goto('/product-authorization');await page.getByRole('button',{name:'Allow',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(reviewCopy[failure==='changed'?'authorization.changed':'authorization.unavailable']);
 await expect(page.locator('div[tabindex="-1"]').filter({has:page.getByRole('alert')})).toBeFocused();await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Deny',exact:true})).toBeDisabled();await expect(page).toHaveURL(origin+'/product-authorization');await settleFrames(page);expect(state.posts).toHaveLength(1);expect(state.nativeEntries).toBe(0);await noBrowserLeak(page);
});}

for(const failure of ['unavailable','unpublished','inconsistent'] as const){test('Product review '+failure+' options never expose actionable controls',async({page})=>{
 const value=options(failure==='inconsistent'?{canAllow:true,interactionStatus:'RequiresFreshProof'}:failure==='unpublished'?{content:published({'authorization.heading':'Continue to {productName}'})}:{});const state=await setupReview(page,value);
 if(failure==='unavailable')await page.route('**/product-authorization/options',route=>route.fulfill({status:503,json:{error_code:'DEPENDENCY_UNAVAILABLE'}}));
 await page.goto('/product-authorization');await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.getByRole('button',{name:'Allow',exact:true})).toHaveCount(0);expect(state.posts).toHaveLength(0);expect(state.nativeEntries).toBe(0);await noBrowserLeak(page);
});}

test('Product review refresh and browser Back/Forward reread current options without automatic decisions',async({page})=>{
 await setupLogin(page,false);const state=await setupReview(page);await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await page.goto('/product-authorization');await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();const initialReads=state.reads;
 await page.reload();await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();expect(state.reads).toBeGreaterThan(initialReads);const refreshedReads=state.reads;
 await page.goBack();await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await page.goForward();await expect(page.getByRole('button',{name:'Allow',exact:true})).toBeEnabled();expect(state.reads).toBeGreaterThan(refreshedReads);expect(state.posts).toHaveLength(0);await noBrowserLeak(page);
});

test('Product review drops a delayed decision response after browser history leaves the review',async({page})=>{
 await setupLogin(page,false);const state=await setupReview(page);let release!:()=>void;const gate=new Promise<void>(resolve=>{release=resolve;});let received!:()=>void;const requestReceived=new Promise<void>(resolve=>{received=resolve;});
 await page.route('**/product-authorization/decision',async route=>{state.posts.push(route.request().postDataJSON());received();await gate;try{await route.fulfill({json:{resumePath:'/connect/authorize'}});}catch{/* The old document request may already have been aborted. */}});
 await page.goto('/login');await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();await page.goto('/product-authorization');await page.getByRole('button',{name:'Allow',exact:true}).click();await requestReceived;await page.goBack();await expect(page.getByRole('heading',{name:'Welcome back'})).toBeVisible();release();await settleFrames(page);await expect(page).toHaveURL(origin+'/login');expect(state.posts).toHaveLength(1);expect(state.nativeEntries).toBe(0);await noBrowserLeak(page);
});

test('Product review direct entry removes safe display query and fragment before loading its canonical state',async({page})=>{
 const state=await setupReview(page);await page.goto('/product-authorization?display=review#details');await expect(page).toHaveURL(origin+'/product-authorization');await expect(page.getByRole('heading',{name:'Continue to Product A'})).toBeVisible();expect(state.posts).toHaveLength(0);await noBrowserLeak(page);
});
