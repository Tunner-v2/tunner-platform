import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/sessions.content.proposal.json' with {type:'json'};
import inventory from '../../../docs/contracts/ui-inventory.json' with {type:'json'};
import {sessionRoutes} from '../../apps/user/sessions.routes';
import type {SessionItem} from '../../apps/user/sessions.api';
import stepUpProposal from '../../apps/user/step-up.content.proposal.json' with {type:'json'};

const actor='11111111-1111-4111-8111-111111111111',target='22222222-2222-4222-8222-222222222222',expired='33333333-3333-4333-8333-333333333333';
const list='/account/sessions',details='/account/sessions/details';
function item(reference:string,isCurrent=false):SessionItem{return {reference,version:'9007199254740993',isCurrent,authenticatedAt:'2026-11-14T02:00:00Z',lastSeenAt:'2026-11-14T04:00:00Z',expiresAt:'2026-11-15T02:00:00Z',recordedState:'ACTIVE',effectiveState:'ACTIVE',evaluatedAt:'2026-11-14T04:01:00Z'};}
const publication={locale:'en',content:Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'synthetic-controlled-bff-variant',versionRef:'synthetic-controlled-bff-version',publicationRef:'synthetic-local-test-not-production-publication'}]))};
interface Fixture{reads:(string|null)[];bodies:unknown[];sessionReads:number;signOuts:number;authenticated:boolean;status:number;lostSignOut:boolean;first:SessionItem[];next:SessionItem[];release:(()=>void)|undefined;hold:boolean}
async function setup(page:Page,change:Partial<Fixture>={}){
 const state:Fixture={reads:[],bodies:[],sessionReads:0,signOuts:0,authenticated:true,status:200,lostSignOut:false,first:[item(actor,true),item(target)],next:[{...item(expired),effectiveState:'EXPIRED'}],release:undefined,hold:false,...change};
 await page.route('**/authentication/session',async route=>{state.sessionReads++;return route.fulfill({status:state.authenticated?200:401,json:state.authenticated?{authenticated:true,csrfToken:'fixture-current-user-csrf'}:{error_code:'AUTHENTICATION_FAILED'}});});
 await page.route('**/account/sessions/options',route=>route.fulfill({json:publication}));
 await page.route('**/account/sessions/revocation/options',route=>route.fulfill({status:404,json:{error_code:'NOT_FOUND'}}));
 await page.route('**/account/sessions/list',async route=>{
  const body=route.request().postDataJSON() as {continuation:string|null};state.bodies.push(body);state.reads.push(body.continuation);
  expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-user-csrf');
  if(state.hold){await new Promise<void>(resolve=>{state.release=resolve;});}
  if(state.status!==200)return route.fulfill({status:state.status,json:{error_code:state.status===409?'VERSION_CONFLICT':'DEPENDENCY_UNAVAILABLE',privateDetail:'never-render-this'}});
  const more=body.continuation===null&&state.first.length>0;
  return route.fulfill({json:{sessions:body.continuation===null?state.first:state.next,hasMore:more,continuation:more?'opaque-body-only-cursor':null}});
 });
 await page.route('**/authentication/sign-out',async route=>{state.signOuts++;expect(route.request().postDataJSON()).toEqual({});if(state.lostSignOut)return route.abort('connectionreset');state.authenticated=false;return route.fulfill({json:{authenticated:false}});});
 // Only the landing document is synthetic; the real source entry and its assets come from the existing dev server.
 await page.route('**/login',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>Fixture sign in</title><main>Fixture sign in</main>'}));
 return state;
}
async function noStorageOrSelectors(page:Page){expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length,search:location.search,hash:location.hash}))).toEqual({local:0,session:0,search:'',hash:''});expect(page.url()).not.toContain(target);}
for(const width of [1440,1366,1024,768,390,320]){
 test('Approved listing/detail pattern, truthful state and responsive layout at '+width,async({page})=>{
  await page.setViewportSize({width,height:1100});const state=await setup(page);await page.goto(list);await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeFocused();
  await expect(page.getByRole('heading',{name:'Session …11111111'})).toBeVisible();await expect(page.getByText('Device names, location and device trust are unavailable here.',{exact:false})).toBeVisible();
  await expect(page.locator('.sessions-current').getByText('YOU ARE USING THIS SESSION',{exact:true})).toBeVisible();await expect(page.locator('.sessions-current').getByText(/^Checked /)).toHaveCount(0);
  if(width<=700)await page.getByRole('button',{name:'Account menu',exact:true}).click();
  const support=page.getByRole('button',{name:'Contact support',exact:true});await expect(support).toBeVisible();await expect(support).toBeDisabled();await expect(support).toHaveAttribute('data-appearance','outline');
  if(width<=700)await page.getByRole('button',{name:'Account menu',exact:true}).click();
  const openSecurity=page.getByRole('button',{name:'Open security',exact:true});await expect(openSecurity).toBeVisible();await expect(openSecurity).toHaveAttribute('data-appearance','outline');await expect(openSecurity).toHaveCSS('border-top-width','1px');
  const listScope=page.locator('.sessions-product-note');await expect(listScope).toHaveText(proposal.content['account.sessions.productBoundary']);await expect(listScope).toHaveCSS('border-top-style','none');
  await expect(page.getByText(/Chrome|Windows|Karachi|unfamiliar|12 SESSIONS|8 DEVICES/)).toHaveCount(0);await expect(page.getByRole('button',{name:'Sign out this session'})).toHaveCount(0);
  await page.evaluate(()=>document.fonts.ready);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  await page.screenshot({path:'test-results/sessions/list-'+width+'.png',fullPage:true});
  await page.locator('[data-session-reference="'+target+'"]').getByRole('button',{name:'Session information'}).click();await expect(page).toHaveURL(new RegExp(details+'$'));await expect(page.getByRole('heading',{name:'Session details'})).toBeFocused();await expect(page.getByText(target,{exact:true})).toBeVisible();await expect(page.getByText('9007199254740993')).toHaveCount(0);await expect(page.getByRole('button',{name:/Sign out this session/})).toHaveCount(0);
  const deviceFacts=page.locator('.sessions-facts > div').filter({has:page.getByText('Device, location & trust',{exact:true})});await expect(deviceFacts.locator('dd')).toHaveText('Unavailable');
  const reviewSecurity=page.getByRole('button',{name:'Review security',exact:true});await expect(reviewSecurity).toBeVisible();await expect(reviewSecurity).toHaveAttribute('data-appearance','outline');await expect(reviewSecurity).toHaveCSS('border-top-width','1px');
  await expect(page.locator('.sessions-back-arrow')).toHaveText('←');await expect(page.locator('.sessions-back-arrow')).toHaveAttribute('aria-hidden','true');await expect(page.getByRole('button',{name:'Back to sessions',exact:true})).toBeVisible();
  await expect(page.locator('.sessions-facts').getByText(/^Checked /)).toBeVisible();await expect(page.locator('.passkeys-helpbox').filter({hasText:proposal.content['account.sessions.productBoundary']})).toHaveCSS('border-top-width','1px');
  expect(state.reads).toEqual([null,null]);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'test-results/sessions/details-'+width+'.png',fullPage:true});await noStorageOrSelectors(page);
 });
}
test('Keyboard navigation reaches both guarded screens on desktop and narrow mobile',async({page})=>{
 expect(inventory.items.find(item=>item.id===sessionRoutes.list.id)?.pathname).toBe(list);expect(inventory.items.find(item=>item.id===sessionRoutes.details.id)?.pathname).toBe(details);
 await setup(page);await page.goto(list);await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeFocused();
 await expect(page.locator('main')).toHaveAttribute('data-tunner-screen',sessionRoutes.list.id);
 await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'View current session'})).toBeFocused();await page.keyboard.press('Enter');await expect(page).toHaveURL(new RegExp(details+'$'));await expect(page.getByRole('heading',{name:'Session details'})).toBeFocused();
 await expect(page.locator('main')).toHaveAttribute('data-tunner-screen',sessionRoutes.details.id);
 await page.setViewportSize({width:320,height:1100});await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Back to sessions',exact:true})).toBeFocused();await page.keyboard.press('Enter');await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeFocused();await noStorageOrSelectors(page);
});
test('Load more uses one retained body cursor; double clicks do not issue duplicate requests',async({page})=>{
 const state=await setup(page);await page.goto(list);await expect(page.getByRole('button',{name:'Load more'})).toBeVisible();state.hold=true;
 await page.getByRole('button',{name:'Load more'}).dblclick({delay:5});await expect.poll(()=>state.reads.length).toBe(2);expect(state.bodies).toEqual([{continuation:null},{continuation:'opaque-body-only-cursor'}]);state.hold=false;state.release?.();
 await expect(page.getByText('Expired',{exact:true})).toBeVisible();await expect(page.getByText('No more sessions in this response.')).toBeVisible();await expect(page.getByRole('button',{name:'Load more'})).toHaveCount(0);await noStorageOrSelectors(page);
});
test('Direct detail entry/reload safely reselects through list; history preserves only current server-owned rows',async({page})=>{
 const state=await setup(page);await page.goto(details);await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeVisible();
 await page.locator('[data-session-reference="'+target+'"]').getByRole('button',{name:'Session information'}).click();await expect(page.getByText(target,{exact:true})).toBeVisible();await page.goBack();await expect(page).toHaveURL(new RegExp(list+'$'));await page.goForward();await expect(page).toHaveURL(new RegExp(details+'$'));await expect(page.getByText(target,{exact:true})).toBeVisible();
 state.first=[item(actor,true)];await page.reload();await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByText(target,{exact:true})).toHaveCount(0);await noStorageOrSelectors(page);
});
test('An actor/context change cannot display a retained detail row absent from the freshly authorized page',async({page})=>{
 const state=await setup(page);await page.goto(list);await expect(page.locator('[data-session-reference="'+target+'"]')).toBeVisible();state.first=[item(actor,true)];
 await page.locator('[data-session-reference="'+target+'"]').getByRole('button',{name:'Session information'}).click();await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByText(target,{exact:true})).toHaveCount(0);
});
for(const status of [404,409,503]){
 test('Pagination '+status+' clears stale rows and requires explicit refresh, without automatic retry',async({page})=>{
  const state=await setup(page);await page.goto(list);await expect(page.getByRole('button',{name:'Load more'})).toBeVisible();state.status=status;await page.getByRole('button',{name:'Load more'}).click();
  await expect(page.getByRole('alert')).toContainText(status===503?'We could not refresh':'This session list has changed');await expect(page.locator('[data-session-reference]')).toHaveCount(0);expect(state.reads).toHaveLength(2);await expect(page.getByText('never-render-this')).toHaveCount(0);
  state.status=200;await page.getByRole('button',{name:'Refresh sessions'}).click();await expect(page.locator('[data-session-reference="'+target+'"]')).toBeVisible();expect(state.reads).toEqual([null,'opaque-body-only-cursor',null]);
 });
}
test('Current session detail retains ordinary Account-menu sign-out; lost response offers a read, never a fresh retry',async({page})=>{
 const state=await setup(page,{lostSignOut:true});await page.goto(list);await page.getByRole('button',{name:'View current session'}).click();await expect(page.getByRole('heading',{name:'Session details'})).toBeVisible();await expect(page.getByText('CURRENT SESSION',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Account owner'}).click();await page.locator('#sessions-account-actions').getByRole('button',{name:'Sign out',exact:true}).click();await expect(page.getByRole('alert')).toContainText('We could not confirm whether you signed out');expect(state.signOuts).toBe(1);
 await page.getByRole('button',{name:'Check sign-in status'}).click();await expect(page.getByRole('heading',{name:'Session details'})).toBeVisible();expect(state.signOuts).toBe(1);await noStorageOrSelectors(page);
});
test('Anonymous actor is redirected before data is requested',async({page})=>{
 const state=await setup(page,{authenticated:false});await page.goto(list);await expect(page).toHaveURL(/\/login$/);expect(state.reads).toEqual([]);
});
test('Ordinary current-session sign-out uses only the existing operation and returns to sign in',async({page})=>{
 const state=await setup(page);await page.goto(list);await expect(page.getByRole('button',{name:'View current session'})).toBeVisible();await page.getByRole('button',{name:'Account owner'}).click();await page.locator('#sessions-account-actions').getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);expect(state.signOuts).toBe(1);expect(state.authenticated).toBe(false);
});
test('Published loading and empty response do not invent a current row, total or target action',async({page})=>{
 const state=await setup(page,{hold:true,first:[]});await page.goto(list);await expect(page.getByRole('heading',{name:'Loading sessions…'})).toBeVisible();await expect.poll(()=>state.reads.length).toBe(1);state.hold=false;state.release?.();await expect(page.getByRole('heading',{name:'No other sessions loaded'})).toBeVisible();await expect(page.getByRole('button',{name:'View current session'})).toHaveCount(0);await expect(page.getByRole('button',{name:'Load more'})).toHaveCount(0);await expect(page.getByRole('button',{name:'Sign out this session'})).toHaveCount(0);
});
test('Withdrawn publication fails closed and renders no session facts',async({page})=>{
 const state=await setup(page);await page.route('**/account/sessions/options',route=>route.fulfill({json:{...publication,content:{}}}));await page.goto(list);await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');expect(state.reads).toEqual([]);
});
test('A stale in-flight list response cannot restore data after navigation leaves its revision',async({page})=>{
 const state=await setup(page);let release:(()=>void)|undefined,posts=0;
 await page.route('**/account/sessions/list',async route=>{posts++;if(posts===1){await new Promise<void>(resolve=>{release=resolve;});return route.fulfill({json:{sessions:[item(target)],hasMore:false,continuation:null}}).catch(()=>{});}return route.fulfill({json:{sessions:[item(actor,true)],hasMore:false,continuation:null}});});
 await page.goto(list);await expect.poll(()=>posts).toBe(1);await page.evaluate(()=>{history.pushState(null,'','/account/sessions/details');dispatchEvent(new PopStateEvent('popstate'));});
 await expect(page).toHaveURL(new RegExp(list+'$'));await expect.poll(()=>posts).toBe(2);release?.();await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeVisible();await expect(page.locator('[data-session-reference="'+target+'"]')).toHaveCount(0);expect(state.signOuts).toBe(0);await noStorageOrSelectors(page);
});
test('An initial list failure uses the current published retry state; withdrawn refreshed publication exposes no stale facts',async({page})=>{
 const state=await setup(page,{status:503});await page.goto(list);await expect(page.getByRole('alert')).toHaveText(proposal.content['account.sessions.error']);await expect(page.getByRole('button',{name:'Refresh sessions'})).toBeVisible();
 state.status=200;await page.getByRole('button',{name:'Refresh sessions'}).click();await expect(page.locator('[data-session-reference="'+target+'"]')).toBeVisible();
 await page.route('**/account/sessions/options',route=>route.fulfill({json:{...publication,content:{}}}));await page.getByRole('button',{name:'Refresh sessions'}).click();await expect(page.getByRole('alert')).toHaveText('The request could not be completed.');await expect(page.locator('[data-session-reference]')).toHaveCount(0);
});
test('Navigation while ordinary sign-out is in flight becomes check-only UNKNOWN and never replays the POST',async({page})=>{
 const state=await setup(page);let release:(()=>void)|undefined;
 await page.route('**/authentication/sign-out',async route=>{state.signOuts++;await new Promise<void>(resolve=>{release=resolve;});return route.fulfill({json:{authenticated:false}}).catch(()=>{});});
 await page.goto(list);await page.getByRole('button',{name:'View current session'}).click();await expect(page.getByRole('heading',{name:'Session details'})).toBeVisible();await page.getByRole('button',{name:'Account owner'}).click();await page.locator('#sessions-account-actions').getByRole('button',{name:'Sign out',exact:true}).click();await expect.poll(()=>state.signOuts).toBe(1);
 await page.getByRole('link',{name:'Sessions & devices',exact:true}).click();await expect(page.getByRole('alert')).toContainText('We could not confirm whether you signed out');release?.();await expect(page.getByRole('button',{name:'Check sign-in status'})).toBeEnabled();await page.getByRole('button',{name:'Check sign-in status'}).click();await expect(page.getByRole('heading',{name:'Sessions & devices',exact:true})).toBeVisible();expect(state.signOuts).toBe(1);
});
test('Expired detail continuation discards selection and refresh returns to a fresh first page',async({page})=>{
 const state=await setup(page);await page.goto(list);await page.getByRole('button',{name:'Load more'}).click();await expect(page.locator('[data-session-reference="'+expired+'"]')).toBeVisible();state.status=409;
 await page.locator('[data-session-reference="'+expired+'"]').getByRole('button',{name:'Session information'}).click();await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('alert')).toContainText('This session list has changed');expect(state.reads.slice(-2)).toEqual(['opaque-body-only-cursor',null]);
 state.status=200;await page.getByRole('button',{name:'Refresh sessions'}).click();await expect(page.locator('[data-session-reference="'+target+'"]')).toBeVisible();expect(state.reads.at(-1)).toBeNull();
});

/** Controlled BFF responses exercise real Sessions/StepUp components, not live proof/provider qualification. */
async function revocationSetup(page:Page){
 const base=await setup(page),proofId='55555555-5555-4555-8555-555555555555';
 const state={intent:undefined as {requestId:string;operationReference:string;status:'PENDING'|'COMPLETED'|'CHANGED';target:{reference:string;version:string;lastSeenAt:string};expiresAt:string}|undefined,prepares:[] as {requestId:string;targetReference:string;expectedVersion:string}[],proofPosts:0,proofReads:0,resultReads:0,resultAvailable:true,proofCompleted:false,proofMismatch:false,lostProof:false};
 const content=Object.fromEntries(Object.entries(stepUpProposal.content).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'synthetic-sessions-proof',versionRef:'synthetic-version',publicationRef:'SYNTHETIC-CONTROLLED-BFF-ONLY'}]));
 await page.route('**/account/sessions/revocation/options',route=>route.fulfill({json:publication}));
 await page.route('**/account/sessions/revocation/context',route=>route.fulfill(state.intent?{json:{...state.intent,status:state.proofCompleted&&state.resultAvailable?'COMPLETED':state.intent.status}}:{status:404,json:{error_code:'NOT_FOUND'}}));
 await page.route('**/account/sessions/revocation/prepare',route=>{
  const request=route.request().postDataJSON() as {requestId:string;targetReference:string;expectedVersion:string};state.prepares.push(request);expect(Object.keys(request).sort()).toEqual(['expectedVersion','requestId','targetReference']);expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-user-csrf');
  state.intent={requestId:request.requestId,operationReference:'fixture-session-owner-'+request.requestId,status:'PENDING',target:{reference:request.targetReference,version:request.expectedVersion,lastSeenAt:item(target).lastSeenAt},expiresAt:'2026-11-15T04:05:00Z'};return route.fulfill({json:state.intent});
 });
 await page.route('**/authentication/step-up/options',route=>{
  state.proofReads++;expect(state.intent).toBeDefined();const operationReference=state.proofMismatch?'unrelated-owner':state.intent!.operationReference;
  return route.fulfill({json:state.proofCompleted?{status:'COMPLETED',requestId:proofId,operationReference}:{status:'PENDING',requestId:proofId,operationReference,csrfToken:'fixture-shared-proof-csrf',passwordMaximumLength:128,passwordAvailable:true,locale:'en',content}});
 });
 await page.route('**/authentication/step-up/password',route=>{
  state.proofPosts++;expect(route.request().postDataJSON()).toEqual({requestId:proofId,password:'synthetic current password'});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-shared-proof-csrf');state.proofCompleted=true;
  if(state.resultAvailable)base.first=base.first.map(value=>value.reference===target?{...value,version:'9007199254740994',recordedState:'REVOKED',effectiveState:'REVOKED'}:value);
  return state.lostProof?route.abort('connectionreset'):route.fulfill({json:{status:'COMPLETED',signInRequired:false,operationReference:state.intent!.operationReference}});
 });
 await page.route('**/account/sessions/revocation/result',route=>{
  state.resultReads++;expect(route.request().postDataJSON()).toEqual({requestId:state.intent!.requestId});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-user-csrf');
  if(!state.proofCompleted||!state.resultAvailable)return route.fulfill({status:409,json:{error_code:'NOT_APPLIED_OR_NOT_DISCLOSABLE',privateDetail:'never-render-causal-internals'}});
  return route.fulfill({json:{requestId:state.intent!.requestId,operationReference:state.intent!.operationReference,status:'REVOKED',target:{...state.intent!.target,version:(BigInt(state.intent!.target.version)+1n).toString()},revokedAt:'2026-11-14T04:02:00Z'}});
 });
 return {base,state};
}
async function reviewOther(page:Page){await page.goto(list);await page.locator('[data-session-reference="'+target+'"]').getByRole('button',{name:'Session information'}).click();await page.getByRole('button',{name:'Sign out this session',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign out this session?',exact:true})).toBeFocused();}
async function finishPassword(page:Page){await page.getByRole('button',{name:'Other method',exact:true}).click();await page.getByLabel('Password',{exact:true}).fill('synthetic current password');await page.getByRole('button',{name:'Confirm',exact:true}).click();}
for(const width of [1440,1366,1024,768,390,320]){
 test('Selected-other-session confirmation and exact result reuse approved modal at '+width,async({page})=>{
  await page.setViewportSize({width,height:1100});const {state}=await revocationSetup(page);await reviewOther(page);
  const dialog=page.getByRole('dialog');await expect(dialog).toContainText(proposal.content['account.sessions.productBoundary']);await expect(dialog).toContainText('Session …22222222');await expect(page.getByText('9007199254740993')).toHaveCount(0);
  await page.keyboard.press('Tab');await expect(dialog.getByRole('button',{name:'Keep signed in'})).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(dialog.getByRole('button',{name:'Sign out',exact:true})).toBeFocused();await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);expect(state.prepares).toHaveLength(0);await expect(page.getByRole('button',{name:'Sign out this session',exact:true})).toBeFocused();
  await page.getByRole('button',{name:'Sign out this session',exact:true}).click();await expect(dialog).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);const box=await dialog.boundingBox();expect(box!.width).toBeLessThanOrEqual(width-32);expect(box!.x).toBeGreaterThanOrEqual(16);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);await page.screenshot({path:'test-results/sessions/revocation-confirm-'+width+'.png',fullPage:true});
  await dialog.getByRole('button',{name:'Sign out',exact:true}).evaluate(element=>{(element as HTMLButtonElement).click();(element as HTMLButtonElement).click();});await expect(page.getByRole('heading',{name:stepUpProposal.content['stepUp.heading'],exact:true})).toBeVisible();expect(state.prepares).toHaveLength(1);expect(state.prepares[0]!.expectedVersion).toBe('9007199254740993');await finishPassword(page);
  await expect(page.getByRole('heading',{name:'Session signed out',exact:true})).toBeFocused();expect(state.proofPosts).toBe(1);expect(state.resultReads).toBe(1);await expect(page.getByRole('dialog')).toContainText('Access inside connected Products is managed separately.');expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);await page.screenshot({path:'test-results/sessions/revocation-result-'+width+'.png',fullPage:true});
  await page.getByRole('dialog').getByRole('button',{name:'Back to sessions',exact:true}).click();await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('dialog')).toHaveCount(0);expect(state.prepares).toHaveLength(1);expect(state.proofPosts).toBe(1);await noStorageOrSelectors(page);
 });
}
test('Result409 remains UNKNOWN and checks the same original without preparing or proving again',async({page})=>{
 const {state}=await revocationSetup(page);state.resultAvailable=false;await reviewOther(page);await page.getByRole('dialog').getByRole('button',{name:'Sign out',exact:true}).click();await finishPassword(page);
 await expect(page.getByRole('heading',{name:'Sign-out is not confirmed',exact:true})).toBeVisible();await expect(page.getByText('never-render-causal-internals')).toHaveCount(0);const original=state.intent;
 await page.getByRole('button',{name:'Check sign-out status',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign-out is not confirmed',exact:true})).toBeVisible();expect(state.resultReads).toBe(2);expect(state.prepares).toHaveLength(1);expect(state.proofPosts).toBe(1);expect(state.intent).toEqual(original);
 await page.screenshot({path:'test-results/sessions/revocation-unknown.png',fullPage:true});state.resultAvailable=true;await page.getByRole('button',{name:'Check sign-out status',exact:true}).click();await expect(page.getByRole('heading',{name:'Session signed out',exact:true})).toBeVisible();expect(state.resultReads).toBe(3);expect(state.prepares).toHaveLength(1);expect(state.proofPosts).toBe(1);
});
test('Lost shared proof response reconciles exact owner result once without a second mutation',async({page})=>{
 const {state}=await revocationSetup(page);state.lostProof=true;await reviewOther(page);await page.getByRole('dialog').getByRole('button',{name:'Sign out',exact:true}).click();await finishPassword(page);await expect(page.getByRole('heading',{name:'Session signed out',exact:true})).toBeVisible();expect(state.prepares).toHaveLength(1);expect(state.proofPosts).toBe(1);expect(state.resultReads).toBe(1);
});
test('Unrelated shared proof context cannot authorize the selected target or claim success',async({page})=>{
 const {state}=await revocationSetup(page);state.proofMismatch=true;await reviewOther(page);await page.getByRole('dialog').getByRole('button',{name:'Sign out',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign-out is not confirmed',exact:true})).toBeVisible();expect(state.proofPosts).toBe(0);expect(state.prepares).toHaveLength(1);await expect(page.getByLabel('Password',{exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Check sign-out status',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign-out is not confirmed',exact:true})).toBeVisible();expect(state.proofPosts).toBe(0);expect(state.prepares).toHaveLength(1);
});
test('Direct detail reload recovers protected completed intent before the selection guard and reads its result',async({page})=>{
 const {state,base}=await revocationSetup(page);state.intent={requestId:'66666666-6666-4666-8666-666666666666',operationReference:'fixture-retained-original',status:'COMPLETED',target:{reference:target,version:item(target).version,lastSeenAt:item(target).lastSeenAt},expiresAt:'2026-11-15T04:05:00Z'};state.proofCompleted=true;base.first=[item(actor,true)];await page.goto(details);await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('heading',{name:'Session signed out',exact:true})).toBeVisible();expect(state.resultReads).toBe(1);expect(state.prepares).toHaveLength(0);expect(state.proofPosts).toBe(0);expect(state.proofReads).toBe(0);await expect(page.getByText(target,{exact:true})).toHaveCount(0);await noStorageOrSelectors(page);
});
test('Authoritative CHANGED original requires explicit refresh and never replaces the expected version',async({page})=>{
 const {state,base}=await revocationSetup(page);state.intent={requestId:'77777777-7777-4777-8777-777777777777',operationReference:'fixture-changed-original',status:'CHANGED',target:{reference:target,version:item(target).version,lastSeenAt:item(target).lastSeenAt},expiresAt:'2026-11-15T04:05:00Z'};await page.goto(list);await expect(page.getByRole('heading',{name:'This session has changed',exact:true})).toBeVisible();expect(state.prepares).toHaveLength(0);expect(state.proofReads).toBe(0);await page.screenshot({path:'test-results/sessions/revocation-changed.png',fullPage:true});base.first=base.first.map(value=>value.reference===target?{...value,version:'9007199254740994'}:value);
 await page.getByRole('button',{name:'Refresh details',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.locator('[data-session-reference="'+target+'"]').getByRole('button',{name:'Session information'})).toBeVisible();expect(state.prepares).toHaveLength(0);expect(state.proofPosts).toBe(0);
});
test('Rejected stale preparation allows explicit refresh without claiming a submitted revocation',async({page})=>{
 const {state}=await revocationSetup(page);let rejected=0;await page.route('**/account/sessions/revocation/prepare',route=>{rejected++;return route.fulfill({status:409,json:{error_code:'VERSION_CONFLICT'}});});await reviewOther(page);await page.getByRole('dialog').getByRole('button',{name:'Sign out',exact:true}).click();await expect(page.getByRole('heading',{name:'This session has changed',exact:true})).toBeVisible();expect(rejected).toBe(1);expect(state.proofReads).toBe(0);expect(state.resultReads).toBe(0);await page.getByRole('button',{name:'Refresh details',exact:true}).click();await expect(page).toHaveURL(new RegExp(list+'$'));await expect(page.getByRole('dialog')).toHaveCount(0);expect(rejected).toBe(1);
});
