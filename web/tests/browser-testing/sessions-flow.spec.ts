import {expect,test} from '@playwright/test';
import type {Page} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import proposal from '../../apps/user/sessions.content.proposal.json' with {type:'json'};
import {registerVerifiedAccount,runtime} from './fixtures';

// Use the already trusted localhost certificate; this journey does not bypass TLS validation.
test.use({ignoreHTTPSErrors:false});
const text=(key:string)=>(proposal.content as Record<string,string>)[key]!;
async function signIn(page:Page,email:string,password:string){
 await page.goto('/login');
 await expect(page.getByRole('heading',{name:'Welcome back',exact:true})).toBeVisible();
 await page.getByLabel('Email address').fill(email);
 await page.getByLabel('Password',{exact:true}).fill(password);
 const response=page.waitForResponse(value=>value.url().endsWith('/authentication/sign-in')&&value.request().method()==='POST');
 await page.getByRole('button',{name:'Sign in',exact:true}).click();
 expect((await response).status()).toBe(200);
 await expect(page).toHaveURL(/\/signed-in$/);
 await expect(page.getByRole('heading',{name:'You’re signed in',exact:true})).toBeVisible();
}

test('real Sessions proof survives reload and lost response, ends only its target and hands back ordinary verification',async({page,request,browser},info)=>{
 test.setTimeout(90000);
 const credentials=await registerVerifiedAccount(page,request);
 await signIn(page,credentials.email,credentials.password);
 const otherContext=await browser.newContext({baseURL:'https://localhost:'+runtime.ApplicationPort,ignoreHTTPSErrors:false});
 try{
  const other=await otherContext.newPage();await signIn(other,credentials.email,credentials.password);
  await page.setViewportSize({width:1440,height:1100});
  await page.getByRole('link',{name:'Account security',exact:true}).click();
  await expect(page).toHaveURL(/\/account\/security$/);
  const sessionsLink=page.getByRole('link',{name:/^sessions\s*&\s*devices$/i});
  await expect(sessionsLink).toHaveAttribute('href','/account/sessions');await sessionsLink.click();
  await expect(page).toHaveURL(/\/account\/sessions$/);
  const target=page.locator('.sessions-row').filter({has:page.locator('.sessions-pill.active')});
  await expect(target).toHaveCount(1);
  const targetReference=await target.getAttribute('data-session-reference');expect(Boolean(targetReference)).toBe(true);
  await target.getByRole('button',{name:text('account.sessions.information'),exact:true}).click();
  await expect(page).toHaveURL(/\/account\/sessions\/details$/);
  let prepares=0,proofWrites=0;
  page.on('request',value=>{if(value.method()!=='POST')return;
   if(value.url().endsWith('/account/sessions/revocation/prepare'))prepares++;
   if(value.url().endsWith('/authentication/step-up/password'))proofWrites++;
  });
  await page.getByRole('button',{name:text('account.sessions.revoke.action'),exact:true}).click();
  const confirmation=page.getByRole('dialog',{name:text('account.sessions.revoke.confirm.title'),exact:true});
  await expect(confirmation).toBeVisible();expect(prepares).toBe(0);
  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  await page.screenshot({path:info.outputPath('confirmation-1440.png'),fullPage:true});
  await page.setViewportSize({width:320,height:800});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await expect(confirmation.getByRole('button',{name:text('account.sessions.revoke.confirm.action'),exact:true})).toBeVisible();
  await page.screenshot({path:info.outputPath('confirmation-320.png'),fullPage:true});
  await page.setViewportSize({width:1440,height:1100});
  const preparing=page.waitForResponse(value=>value.url().endsWith('/account/sessions/revocation/prepare')&&value.request().method()==='POST');
  await confirmation.getByRole('button',{name:text('account.sessions.revoke.confirm.action'),exact:true}).click();
  const prepared=await preparing;expect(prepared.status()).toBe(200);
  const original=await prepared.json() as {requestId:string;operationReference:string;target:{reference:string;version:string}};
  expect(original.target.reference).toBe(targetReference);expect(typeof original.target.version).toBe('string');
  await expect(page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true})).toBeVisible();
  const cookie=(await page.context().cookies()).find(value=>value.name==='__Host-Tunner.SessionRevocation');
  expect(Boolean(cookie)).toBe(true);expect(cookie?.httpOnly).toBe(true);expect(cookie?.secure).toBe(true);expect(cookie?.sameSite).toBe('Strict');
  const originalExpiry=cookie!.expires;
  await page.reload();
  await expect(page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true})).toBeVisible();
  expect((await page.context().cookies()).find(value=>value.name==='__Host-Tunner.SessionRevocation')?.expires).toBe(originalExpiry);
  expect(prepares).toBe(1);
  const proof=page.getByRole('dialog');await proof.getByRole('button',{name:'Other method',exact:true}).click();
  const password=proof.getByLabel('Password',{exact:true});
  await password.fill('Deliberately wrong synthetic credential');await proof.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(password).toHaveAttribute('aria-invalid','true');await expect(password).toHaveValue('');await expect(password).toBeFocused();
  await page.screenshot({path:info.outputPath('proof-validation.png'),fullPage:true});
  let lostSuccessfulResponse=0;
  await page.route('**/authentication/step-up/password',async route=>{
   const actual=await route.fetch();expect(actual.status()).toBe(200);
   const completed=await actual.json() as {status:string;operationReference:string};
   expect(completed.status).toBe('COMPLETED');expect(completed.operationReference).toBe(original.operationReference);
   lostSuccessfulResponse++;await route.abort();
  });
  await password.fill(credentials.password);await proof.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(page.getByRole('heading',{name:text('account.sessions.revoke.success.title'),exact:true})).toBeVisible();
  expect(prepares).toBe(1);expect(proofWrites).toBe(2);expect(lostSuccessfulResponse).toBe(1);
  await page.unroute('**/authentication/step-up/password');
  expect((await page.request.get('/authentication/session')).status()).toBe(200);
  await other.reload();await expect(other).toHaveURL(/\/login$/);
  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  await page.screenshot({path:info.outputPath('result-1440.png'),fullPage:true});
  await page.setViewportSize({width:320,height:800});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:info.outputPath('result-320.png'),fullPage:true});
  await page.setViewportSize({width:1440,height:1100});await page.reload();
  await expect(page.getByRole('heading',{name:text('account.sessions.revoke.success.title'),exact:true})).toBeVisible();
  expect(prepares).toBe(1);expect(proofWrites).toBe(2);
  expect(await page.evaluate(()=>({query:location.search,fragment:location.hash,local:localStorage.length,session:sessionStorage.length,
   visibleOriginal:document.cookie.includes('__Host-Tunner.SessionRevocation')}))).toEqual({query:'',fragment:'',local:0,session:0,visibleOriginal:false});
  // A retained completed receipt must not mask an existing independent security action.
  await page.goto('/browser-testing/step-up');
  const ordinary=page.waitForResponse(value=>value.url().endsWith('/authentication/step-up/options'));
  await page.getByRole('button',{name:'Verify identity',exact:true}).click();
  const ordinaryView=await(await ordinary).json() as {status:string;operationReference:string};
  expect(ordinaryView.status).toBe('PENDING');expect(ordinaryView.operationReference.startsWith('OP-LOCAL-STEP-UP-')).toBe(true);
  await page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true}).click();
  await page.getByRole('dialog').getByLabel('Password',{exact:true}).fill(credentials.password);
  await page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(page.getByRole('status')).toHaveText('Verification completed');expect(prepares).toBe(1);expect(proofWrites).toBe(3);
 }finally{await otherContext.close();}
});
