import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import {randomUUID} from 'node:crypto';
import AxeBuilder from '@axe-core/playwright';
import {setup,passwordProof,paths} from './fixtures';
import copy from '../../apps/user/passkeys.content.proposal.json' with {type:'json'};
import stepCopy from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import type {FederationManagementOperation,FederationManagementSettings} from '../../apps/user/federation.management.api';

const first='12121212-3434-4567-8989-121212121212',second='23232323-4545-4678-8989-232323232323',third='34343434-5656-4789-8989-343434343434';
const published=(source:Record<string,string>)=>Object.fromEntries(Object.entries(source).map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'public-fixture-variant',versionRef:'public-fixture-version',publicationRef:'PUBLIC-SYNTHETIC-ONLY'}]));
function operation(kind:'LINK'|'UNLINK'='UNLINK',extra:Partial<FederationManagementOperation>={}):FederationManagementOperation{return {operationId:randomUUID(),requestId:randomUUID(),kind,provider:'Google',status:kind==='LINK'?'PENDING_PROVIDER':'PENDING_CONFIRMATION',version:1,targetLinkReference:kind==='LINK'?null:first,expectedLinkVersion:kind==='LINK'?0:2,expectedAccountVersion:7,resultLinkReference:null,resultLinkVersion:null,errorCode:null,providerVerified:kind==='LINK',canConfirm:true,canRequestProof:false,canCancel:true,sessionEffect:kind==='UNLINK'?'PreserveExistingSessions':null,impactKey:kind==='UNLINK'?'federation.unlink.impact.preserve':null,...extra};}
async function managed(page:Page,original:FederationManagementOperation|null=null){
 const legacy=await setup(page);const settings:FederationManagementSettings={accountVersion:7,googleAvailable:true,microsoftAvailable:true,canStartLink:true,links:[{linkReference:first,version:2,provider:'Google',state:'ACTIVE',linkedAt:'2026-11-12T12:00:00Z',canStartUnlink:true},{linkReference:second,version:4,provider:'Google',state:'ACTIVE',linkedAt:null,canStartUnlink:false},{linkReference:third,version:3,provider:'Microsoft',state:'UNLINKED',linkedAt:null,canStartUnlink:false}],operation:original};
 const state={legacy,settings,module:true as boolean|undefined,missingKeys:false,missingImpact:false,loseStart:false,wrongResult:false,wrongProof:false,gets:0,proofs:0,proofOptions:0,posts:[] as {path:string;body:Record<string,unknown>}[]};
 await page.route('**/account/passkeys/options',route=>{const content=published(copy.content);if(state.missingKeys)for(const k of Object.keys(content))if(k.startsWith('federation.'))delete content[k];if(state.missingImpact)delete content['federation.unlink.impact.preserve'];return route.fulfill({json:{csrfToken:'fixture-account-csrf',requestId:randomUUID(),maximumCredentialJsonBytes:131072,revocationSessionEffect:'PreserveExistingSessions',locale:'en',content,recovery:null,...(state.module===undefined?{}:{federationAvailable:state.module})}})});
 await page.route('**/account/security/federation',route=>{state.gets++;return route.fulfill({json:settings})});
 await page.route('**/account/security/federation/**',async route=>{
  const path=new URL(route.request().url()).pathname.replace('/account/security/federation/',''),body=route.request().postDataJSON() as Record<string,unknown>;state.posts.push({path,body});expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-authenticated-csrf');
  if(path==='link/start'||path==='unlink/start'){
   const kind=path==='link/start'?'LINK':'UNLINK';settings.operation=operation(kind,{requestId:String(body.requestId),provider:body.provider as 'Google'|'Microsoft',targetLinkReference:typeof body.targetLinkReference==='string'?body.targetLinkReference:null,expectedLinkVersion:Number(body.expectedLinkVersion),expectedAccountVersion:Number(body.expectedAccountVersion),providerVerified:false,canConfirm:kind==='UNLINK'});
   if(state.loseStart){state.loseStart=false;await route.abort('connectionreset');return;}
   await route.fulfill({json:{operation:settings.operation,...(kind==='LINK'?{challengePath:'/authentication/federation/'+String(body.provider).toLowerCase()+'/challenge'}:{})}});return;
  }
  const op=settings.operation;if(!op||body.requestId!==op.requestId||body.operationId!==undefined&&body.operationId!==op.operationId){await route.fulfill({status:404,json:{error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'}});return;}
  if(path==='confirm'){expect(body).toEqual({operationId:op.operationId,requestId:op.requestId,expectedVersion:op.version,confirmed:true});op.status='PENDING_PROOF';op.version++;op.canConfirm=false;op.canRequestProof=true;}
  if(path==='cancel'){op.status='REJECTED';op.version++;op.canConfirm=false;op.canRequestProof=false;op.canCancel=false;}
  await route.fulfill({json:{operation:state.wrongResult?{...op,operationId:randomUUID()}:op}});
 });
 const proofRequest=randomUUID();
 await page.route('**/authentication/step-up/options',route=>{state.proofOptions++;return route.fulfill({json:{status:'PENDING',requestId:proofRequest,operationReference:state.wrongProof?randomUUID().replaceAll('-',''):settings.operation?.operationId.replaceAll('-',''),csrfToken:'fixture-account-csrf',passwordMaximumLength:128,passwordAvailable:true,locale:'en',content:published(stepCopy.content)}})});
 await page.route('**/authentication/step-up/password',route=>{state.proofs++;expect(route.request().postDataJSON().requestId).toBe(proofRequest);const op=settings.operation!;op.status='COMPLETED';op.version++;op.canRequestProof=false;op.canCancel=false;const resultReference=op.targetLinkReference??randomUUID(),resultVersion=op.expectedLinkVersion+1;op.resultLinkReference=resultReference;op.resultLinkVersion=resultVersion;settings.accountVersion++;
  const link=settings.links.find(x=>x.linkReference===op.targetLinkReference);if(link){link.state=op.kind==='UNLINK'?'UNLINKED':'ACTIVE';link.version=resultVersion;}else settings.links.push({linkReference:resultReference,version:resultVersion,provider:op.provider,state:'ACTIVE',linkedAt:'2026-11-12T13:00:00Z',canStartUnlink:true});
  return route.fulfill({json:{status:'COMPLETED',signInRequired:false,operationReference:op.operationId.replaceAll('-','')}});
 });
 return state;
}
const moduleFor=(page:Page)=>page.locator('[data-tunner-component="COMP-FEDERATION-MANAGEMENT"]');

for(const available of [undefined,false])test('Management omitted/false capability '+String(available)+' keeps the default host unchanged without new publications or GET',async({page})=>{
 const state=await managed(page);state.module=available;state.missingKeys=true;await page.goto(paths.security);await expect(page.getByRole('heading',{name:copy.content['passkeys.security.heading'],exact:true})).toBeVisible();await expect(moduleFor(page)).toHaveCount(0);expect(state.gets).toBe(0);
});
test('Management renders a collection of exact links, nullable real dates and independent denied unlink flags',async({page})=>{
 const state=await managed(page);await page.goto(paths.methods);const module=moduleFor(page);await expect(module.getByRole('group',{name:'Google',exact:true})).toHaveCount(2);await expect(module.locator('time')).toHaveCount(1);await expect(module.getByRole('group',{name:'Google',exact:true}).nth(1).getByRole('button',{name:'Unlink',exact:true})).toBeDisabled();
 await module.getByRole('group',{name:'Google',exact:true}).nth(0).getByRole('button',{name:'Unlink',exact:true}).click();await expect(page.getByRole('dialog').getByText(copy.content['federation.unlink.impact.preserve'])).toBeVisible();
 expect(state.posts[0]!.body).toEqual({requestId:expect.any(String),provider:'Google',expectedLinkVersion:2,expectedAccountVersion:7,targetLinkReference:first});
});
for(const provider of ['Google','Microsoft'] as const)test('Fresh '+provider+' link alone admits its fixed clean local challenge without Account/subject/namespace input',async({page})=>{
 const state=await managed(page);await page.route('**/authentication/federation/'+provider.toLowerCase()+'/challenge',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>Public management challenge</title>'}));
 await page.goto(paths.security);await moduleFor(page).getByRole('button',{name:'Link '+provider,exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5193/authentication/federation/'+provider.toLowerCase()+'/challenge');
 expect(state.posts).toHaveLength(1);expect(state.posts[0]!.body).toEqual({requestId:expect.any(String),provider,expectedLinkVersion:0,expectedAccountVersion:7});
 expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});
});
test('Relink targets the exact UNLINKED collection row and its version without a one-per-provider assumption',async({page})=>{
 const state=await managed(page);await page.route('**/authentication/federation/microsoft/challenge',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>Public relink challenge</title>'}));await page.goto(paths.methods);
 await moduleFor(page).getByRole('group',{name:'Microsoft',exact:true}).getByRole('button',{name:'Link again',exact:true}).click();await expect(page).toHaveURL(/\/microsoft\/challenge$/);expect(state.posts[0]!.body).toMatchObject({provider:'Microsoft',targetLinkReference:third,expectedLinkVersion:3,expectedAccountVersion:7});
});
test('Provider-return selected original needs explicit confirmation then the existing exact-owner proof and final result read',async({page})=>{
 const original=operation('LINK');const state=await managed(page,original);await page.goto(paths.security);const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:'Link account',exact:true})).toBeEnabled();expect(state.posts).toHaveLength(0);
 await page.reload();await expect(dialog.getByRole('button',{name:'Link account',exact:true})).toBeEnabled();expect(state.posts).toHaveLength(0);
 await dialog.getByRole('button',{name:'Link account',exact:true}).click();await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();await passwordProof(page);
 await expect(page.getByRole('heading',{name:'Connection updated',exact:true})).toBeVisible();expect(state.proofs).toBe(1);expect(state.posts.filter(x=>x.path==='confirm')).toHaveLength(1);expect(state.posts.some(x=>x.path==='result'&&x.body.operationId===original.operationId&&x.body.requestId===original.requestId)).toBe(true);
 await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();await expect(moduleFor(page).getByRole('button',{name:'Link Google',exact:true})).toBeEnabled();
});
test('Lost management Start uses request-only original result recovery and never reissues its provider challenge',async({page})=>{
 const state=await managed(page);state.loseStart=true;let challenges=0;await page.route('**/authentication/federation/google/challenge',route=>{challenges++;return route.fulfill({status:500})});await page.goto(paths.security);await moduleFor(page).getByRole('button',{name:'Link Google',exact:true}).click();
 await expect(page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true})).toBeEnabled();const firstPost=state.posts[0]!;state.settings.operation!.providerVerified=true;state.settings.operation!.canConfirm=true;
 await page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('dialog').getByRole('button',{name:'Link account',exact:true})).toBeEnabled();expect(state.posts.filter(x=>x.path==='link/start')).toHaveLength(1);expect(state.posts.find(x=>x.path==='result')!.body).toEqual({requestId:firstPost.body.requestId});expect(challenges).toBe(0);
});
test('Unlink confirms the selected version and published effect then changes only that link through existing proof',async({page})=>{
 const original=operation();const state=await managed(page,original);await page.goto(paths.security);await page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true})).toBeVisible();await passwordProof(page);
 await expect(page.getByRole('heading',{name:'Connection updated',exact:true})).toBeVisible();expect(state.settings.links.find(x=>x.linkReference===first)!.state).toBe('UNLINKED');expect(state.settings.links.find(x=>x.linkReference===second)!.state).toBe('ACTIVE');expect(state.proofs).toBe(1);expect(state.posts.filter(x=>x.path==='confirm')).toHaveLength(1);
});
test('Cancel addresses only the selected original and never confirms or requests proof',async({page})=>{
 const original=operation();const state=await managed(page,original);await page.goto(paths.security);await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('heading',{name:'Action not completed',exact:true})).toBeVisible();
 expect(state.posts).toEqual([{path:'cancel',body:{operationId:original.operationId,requestId:original.requestId}}]);expect(state.proofs).toBe(0);await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();await expect(moduleFor(page).getByRole('button',{name:'Link Google',exact:true})).toBeEnabled();
});
test('Unverified provider evidence never enables explicit link confirmation despite a contradictory canConfirm flag',async({page})=>{
 const state=await managed(page,operation('LINK',{providerVerified:false,canConfirm:true}));await page.goto(paths.security);await expect(page.getByRole('dialog').getByRole('button',{name:'Link account',exact:true})).toBeDisabled();expect(state.posts).toHaveLength(0);
});
for(const invalid of ['missing','mismatch','unpublished'] as const)test('Unlink disclosure '+invalid+' disables confirmation even when canConfirm is true',async({page})=>{
 const original=operation();if(invalid==='missing'){delete original.sessionEffect;delete original.impactKey;}if(invalid==='mismatch')original.impactKey='federation.unlink.impact.revokeLinked';const state=await managed(page,original);if(invalid==='unpublished')state.missingImpact=true;
 await page.goto(paths.security);await expect(page.getByRole('dialog').getByText(copy.content['federation.management.detailsUnavailable'])).toBeVisible();await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeDisabled();expect(state.posts).toHaveLength(0);
});
test('Publication withdrawal after unlink review blocks the confirm POST and preserves the original',async({page})=>{
 const original=operation();const state=await managed(page,original);await page.goto(paths.security);await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeEnabled();state.missingImpact=true;
 await page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await expect(page.getByRole('alert')).toHaveText(copy.content['federation.management.changed']);expect(state.posts.filter(x=>x.path==='confirm')).toHaveLength(0);expect(state.settings.operation!.operationId).toBe(original.operationId);
});
test('Wrong proof owner cannot invoke a shared proof or claim a completed link',async({page})=>{
 const state=await managed(page,operation('LINK'));state.wrongProof=true;await page.goto(paths.security);await page.getByRole('dialog').getByRole('button',{name:'Link account',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(copy.content['federation.management.changed']);expect(state.proofs).toBe(0);await expect(page.getByRole('heading',{name:'Connection updated',exact:true})).toHaveCount(0);
});
test('A mismatched original result cannot replace the held selected operation or enable another mutation',async({page})=>{
 const original=operation('LINK',{status:'UNKNOWN',canConfirm:false,providerVerified:false});const state=await managed(page,original);state.wrongResult=true;await page.goto(paths.security);await page.getByRole('dialog').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(copy.content['federation.management.changed']);await expect(moduleFor(page).getByRole('button',{name:'Link Microsoft',exact:true})).toBeDisabled();expect(state.posts.filter(x=>x.path==='link/start')).toHaveLength(0);
});
test('Server-selected revoked-linked-session disclosure is not broadened to all Tunner or Product sessions',async({page})=>{
 await managed(page,operation('UNLINK',{sessionEffect:'RevokeLinkedSessions',impactKey:'federation.unlink.impact.revokeLinked'}));await page.goto(paths.security);await expect(page.getByRole('dialog').getByText(copy.content['federation.unlink.impact.revokeLinked'])).toBeVisible();await expect(page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true})).toBeEnabled();await expect(page.getByText('all sessions',{exact:false})).toHaveCount(0);
});
for(const width of [1440,320])test('Management exact unlink review keyboard accessibility and retained shell at '+width,async({page},testInfo)=>{
 await page.setViewportSize({width,height:1024});await managed(page,operation());await page.goto(paths.security);const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:'Continue',exact:true})).toBeEnabled();await page.evaluate(()=>document.fonts.ready);
 await dialog.getByRole('button',{name:'Cancel',exact:true}).focus();await page.keyboard.press('Shift+Tab');await expect(dialog.getByRole('button',{name:'Continue',exact:true})).toBeFocused();await page.keyboard.press('Tab');await expect(dialog.getByRole('button',{name:'Cancel',exact:true})).toBeFocused();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 const picture=testInfo.outputPath('federation-unlink-'+width+'.png');await page.screenshot({path:picture,fullPage:true});await testInfo.attach('federation-unlink-'+width,{path:picture,contentType:'image/png'});
});
