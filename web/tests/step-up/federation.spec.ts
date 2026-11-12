import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/step-up.content.proposal.json' with {type:'json'};
import type {StepUpBootstrap} from '../../apps/user/step-up';

const requestId='12121212-3434-4567-8989-121212121212',attemptId='23232323-4545-4678-8989-232323232323',linkId='34343434-5656-4789-8989-343434343434',secondLink='45454545-6767-4890-8989-454545454545';
const operationReference='56565656787849018989565656565656';
type Options=NonNullable<StepUpBootstrap['federation']>;
type Recovery=NonNullable<Options['recovery']>;
const restored=(status:Recovery['status']='PROOF_ISSUED'):Recovery=>({requestId,attemptId,provider:'Google',linkReference:linkId,status,expiresAt:'2099-01-01T00:00:00Z',errorCode:null});
async function setup(page:Page,recovery:Recovery|null=null){
 const choices:Options['choices']=[{provider:'Google',linkReference:linkId},{provider:'Google',linkReference:secondLink},{provider:'Microsoft',linkReference:'67676767-8989-4012-8989-676767676767'}];
 const state={id:requestId,choices,recovery,enabled:true,missingKeys:false,missingGoogle:false,ownerDone:false,loseContinue:false,leakOnRead:false,deliveryOnContinue:false,wrong:'' as ''|'requestId'|'attemptId'|'operationReference'|'linkReference',wrongCancel:false,options:0,start:0,result:0,continues:0,cancel:0,credentialPosts:0,bodies:[] as Record<string,unknown>[]};
 const content=()=>Object.fromEntries(Object.entries(proposal.content).filter(([key])=>!state.missingKeys||!key.startsWith('stepUp.federation.')).filter(([key])=>!state.missingGoogle||key!=='stepUp.federation.Google').map(([key,text])=>[key,{key,text,state:'PUBLISHED',locale:'en',variantRef:'public-fixture-variant',versionRef:'public-fixture-version',publicationRef:'PUBLIC-SYNTHETIC-ONLY'}]));
 await page.route('**/authentication/step-up/options',route=>{state.options++;return route.fulfill({json:{status:'PENDING',requestId:state.id,operationReference,csrfToken:'fixture-original-csrf',passwordMaximumLength:128,passwordAvailable:true,locale:'en',content:content(),...(state.enabled?{federation:{choices:state.choices,recovery:state.recovery}}:{})}})});
 await page.route('**/authentication/session',route=>route.fulfill({json:{authenticated:true,csrfToken:'fixture-current-principal-csrf'}}));
 await page.route('**/authentication/step-up/password',route=>{state.credentialPosts++;return route.fulfill({status:500})});
 await page.route('**/authentication/step-up/federation/**',async route=>{
  const action=new URL(route.request().url()).pathname.split('/').at(-1),body=route.request().postDataJSON() as Record<string,unknown>;state.bodies.push(body);expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-current-principal-csrf');
  if(action==='start'){
   state.start++;expect(Object.keys(body).sort()).toEqual(['linkReference','provider','requestId']);state.recovery={requestId:String(body.requestId),attemptId,provider:body.provider as 'Google'|'Microsoft',linkReference:String(body.linkReference),status:'PREPARED',expiresAt:'2099-01-01T00:00:00Z',errorCode:null};
   await route.fulfill({json:{requestId:state.id,operationReference,federation:state.recovery,challengePath:'/authentication/federation/'+String(body.provider).toLowerCase()+'/challenge'}});return;
  }
  expect(body).toEqual({requestId:state.recovery?.requestId??requestId});
  if(action==='cancel'){
   state.cancel++;const ended={status:'REJECTED',requestId:state.wrongCancel?'78787878-9090-4123-8989-787878787878':state.id,operationReference};state.recovery=null;state.id='89898989-0101-4234-8989-898989898989';await route.fulfill({json:ended});return;
  }
  if(action==='continue'){
   state.continues++;state.ownerDone=true;if(state.loseContinue){state.loseContinue=false;await route.abort('connectionreset');return;}
   await route.fulfill({json:{status:'COMPLETED',operationReference,signInRequired:false,...(state.deliveryOnContinue?{recoveryCodeFirstDelivery:{codes:['PUBLIC-SYNTHETIC-SAMPLE']}}:{})}});return;
  }
  state.result++;
  if(state.ownerDone){await route.fulfill({json:{status:'COMPLETED',operationReference}});return;}
  const response={requestId:state.id,operationReference,federation:state.recovery,...(state.leakOnRead?{recoveryCodeFirstDelivery:{codes:['PUBLIC-SYNTHETIC-SAMPLE']}}:{})};
  if(state.wrong==='requestId')response.requestId='90909090-1212-4345-8989-909090909090';if(state.wrong==='operationReference')response.operationReference='00000000000000000000000000000001';
  if(state.wrong==='attemptId'&&response.federation)response.federation={...response.federation,attemptId:'90909090-1212-4345-8989-909090909090'};
  if(state.wrong==='linkReference'&&response.federation)response.federation={...response.federation,linkReference:secondLink};
  await route.fulfill({json:response});
 });
 return state;
}
async function open(page:Page){await page.goto('/browser-testing/step-up-assets/');await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();}

test('Absent federation capability keeps the old chooser independent of every new publication',async({page})=>{
 const state=await setup(page);state.enabled=false;state.missingKeys=true;await open(page);await expect(page.getByRole('button',{name:/Google/})).toHaveCount(0);await expect(page.getByRole('button',{name:'Other method',exact:true})).toBeEnabled();expect(state.start).toBe(0);
});
test('Only an actually offered provider requires its conditional label publication',async({page})=>{
 const state=await setup(page);state.choices=state.choices.filter(value=>value.provider==='Microsoft');state.missingGoogle=true;await open(page);await expect(page.getByRole('button',{name:/Microsoft/})).toHaveCount(1);await expect(page.getByRole('button',{name:/Google/})).toHaveCount(0);
});
test('Offered federation without its published method content fails closed',async({page})=>{
 const state=await setup(page);state.missingKeys=true;await page.goto('/browser-testing/step-up-assets/');await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('status')).toHaveText('Verification unavailable. Sign in, then try again.');await expect(page.getByRole('dialog')).toHaveCount(0);expect(state.start).toBe(0);
});
for(const provider of ['Google','Microsoft'] as const)test(provider+' fresh selection uses the offered link, existing request and only a fixed clean challenge',async({page})=>{
 const state=await setup(page);await page.route('**/authentication/federation/'+provider.toLowerCase()+'/challenge',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>Public proof challenge</title>'}));await open(page);
 const rows=page.getByRole('button',{name:new RegExp(provider)});await expect(rows).toHaveCount(provider==='Google'?2:1);await rows.nth(provider==='Google'?1:0).click();await expect(page).toHaveURL('http://127.0.0.1:5192/authentication/federation/'+provider.toLowerCase()+'/challenge');
 expect(state.bodies[0]).toEqual({requestId,provider,linkReference:provider==='Google'?secondLink:state.choices.find(value=>value.provider==='Microsoft')!.linkReference});expect(state.start).toBe(1);
 expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});
});
test('PROOF_ISSUED is not owner completion and result remains read-only until explicit continuation',async({page})=>{
 const state=await setup(page,restored());await open(page);await expect(page.getByRole('button',{name:'Continue this verification',exact:true})).toBeEnabled();expect(state.start).toBe(0);expect(state.result).toBe(0);expect(state.continues).toBe(0);
 await page.getByRole('button',{name:'Check verification status',exact:true}).click();await expect.poll(()=>state.result).toBe(1);await expect(page.getByRole('dialog')).toBeVisible();expect(state.ownerDone).toBe(false);expect(state.continues).toBe(0);
 await page.getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Verification completed');expect(state.continues).toBe(1);expect(state.result).toBe(2);
});
test('Lost continuation requires a safe owner-result read instead of automatic mutation retry',async({page})=>{
 const state=await setup(page,restored());state.loseContinue=true;await open(page);await page.getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('button',{name:'Check verification status',exact:true})).toBeEnabled();expect(state.continues).toBe(1);
 await page.getByRole('button',{name:'Check verification status',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Verification completed');expect(state.continues).toBe(1);expect(state.start).toBe(0);
});
for(const wrong of ['requestId','attemptId','operationReference','linkReference'] as const)test('Result cannot replace the retained '+wrong,async({page})=>{
 const state=await setup(page,restored());state.wrong=wrong;await open(page);await page.getByRole('button',{name:'Check verification status',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.changed']);await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('button',{name:'Continue this verification',exact:true})).toHaveCount(0);expect(state.continues).toBe(0);
});
test('Source withdrawal during explicit continuation preflight never invokes the owner',async({page})=>{
 const state=await setup(page,restored());await open(page);state.recovery=restored('EXPIRED');await page.getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('button',{name:'Continue this verification',exact:true})).toHaveCount(0);await expect(page.getByRole('dialog')).toBeVisible();expect(state.continues).toBe(0);expect(state.ownerDone).toBe(false);
});
test('Exact method cancellation closes the old dialog; only explicit reopen fetches a new request',async({page})=>{
 const state=await setup(page,restored('VERIFIED'));await open(page);const options=state.options;await page.getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByRole('status')).toHaveText('Cancelled');await expect(page.getByRole('button',{name:'Verify identity',exact:true})).toBeFocused();expect(state.options).toBe(options);expect(state.continues).toBe(0);expect(state.ownerDone).toBe(false);
 await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();expect(state.options).toBe(options+1);expect(state.start).toBe(0);expect(state.recovery).toBeNull();
});
test('An unrelated cancellation acknowledgement cannot dismiss the held proof',async({page})=>{
 const state=await setup(page,restored());state.wrongCancel=true;await open(page);await page.getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.changed']);await expect(page.getByRole('dialog')).toBeVisible();expect(state.continues).toBe(0);
});
test('Read-only recovery cannot carry first-delivery data into the UI',async({page})=>{
 const state=await setup(page,restored());state.leakOnRead=true;await open(page);await page.getByRole('button',{name:'Check verification status',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.unavailable']);await expect(page.getByText('PUBLIC-SYNTHETIC-SAMPLE',{exact:false})).toHaveCount(0);expect(state.continues).toBe(0);
});
test('Continue delegates unexpected first delivery to the existing parser and cannot accept it without a receiver',async({page})=>{
 const state=await setup(page,restored());state.deliveryOnContinue=true;await open(page);await page.getByRole('button',{name:'Continue this verification',exact:true}).click();await expect(page.getByRole('alert')).toHaveText(proposal.content['stepUp.error.unavailable']);await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByText('PUBLIC-SYNTHETIC-SAMPLE',{exact:false})).toHaveCount(0);expect(state.continues).toBe(1);
});
test('Pending provider selection fences duplicate and sibling method actions until the original result can be checked',async({page})=>{
 const state=await setup(page);let release:()=>void=()=>{};const gate=new Promise<void>(resolve=>{release=resolve});
 await page.route('**/authentication/step-up/federation/start',async route=>{state.start++;await gate;await route.abort('connectionreset')});await open(page);
 await page.getByRole('dialog').evaluate(dialog=>{const choices=[...dialog.querySelectorAll<HTMLButtonElement>('.step-up-choice')];const google=choices.find(button=>button.textContent?.includes('Google'));const other=choices.find(button=>button.textContent?.includes('Other method'));google?.click();other?.click();google?.click()});
 await expect.poll(()=>state.start).toBe(1);await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeDisabled();await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByLabel('Password',{exact:true})).toHaveCount(0);
 release();await expect(page.getByRole('button',{name:'Check verification status',exact:true})).toBeEnabled();expect(state.start).toBe(1);expect(state.credentialPosts).toBe(0);expect(state.continues).toBe(0);
});
for(const width of [1440,320])test('Federated collection chooser and explicit continuation retain keyboard and accessible geometry at '+width,async({page},testInfo)=>{
 await page.setViewportSize({width,height:1024});const state=await setup(page);await open(page);await page.evaluate(()=>document.fonts.ready);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
 const chooser=testInfo.outputPath('federation-chooser-'+width+'.png');await page.screenshot({path:chooser,fullPage:true});await testInfo.attach('federation-chooser-'+width,{path:chooser,contentType:'image/png'});
 await page.getByRole('button',{name:'Cancel',exact:true}).focus();await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:/Google/}).nth(0)).toBeFocused();
 await page.keyboard.press('Escape');state.recovery=restored();await page.getByRole('button',{name:'Verify identity',exact:true}).click();await expect(page.getByRole('button',{name:'Continue this verification',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Cancel',exact:true}).focus();await page.keyboard.press('Shift+Tab');await expect(page.getByRole('button',{name:'Continue this verification',exact:true})).toBeFocused();
 const issued=testInfo.outputPath('federation-issued-'+width+'.png');await page.screenshot({path:issued,fullPage:true});await testInfo.attach('federation-issued-'+width,{path:issued,contentType:'image/png'});
});
