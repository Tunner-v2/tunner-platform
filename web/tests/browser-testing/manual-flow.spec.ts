import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
const state=JSON.parse(readFileSync('../artifacts/browser-testing/runtime.json','utf8').replace(/^\uFEFF/,''));
test('real HTTPS registration and SMTP verification complete through the isolated starter',async({page,request})=>{
 const email='starter-'+randomUUID()+'@example.test';
 await page.goto('/registration');await expect(page.getByRole('heading',{name:'Create your Tunner account'})).toBeVisible();
 await expect(page.locator('input[type=date]')).toHaveCount(0);
 await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill('Local unique testing passphrase 2026!');await page.getByLabel('Confirm password').fill('Local unique testing passphrase 2026!');
 await page.getByRole('checkbox').check();const registration=page.waitForResponse(r=>r.url().endsWith('/registration/register')&&r.request().method()==='POST');await page.getByRole('button',{name:'Continue',exact:true}).click();const registered=await registration;expect(registered.status(),await registered.text()).toBe(202);await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();
 let messageId='';await expect.poll(async()=>{const r=await request.get('http://127.0.0.1:'+state.MailpitPort+'/api/v1/messages');const inbox=await r.json();const message=inbox.messages.find((m:{ID:string,To:{Address:string}[]})=>m.To.some(a=>a.Address===email));messageId=message?.ID??'';return Boolean(messageId);},{timeout:20000}).toBe(true);
 const message=await (await request.get('http://127.0.0.1:'+state.MailpitPort+'/api/v1/message/'+messageId)).json();const code=message.Text.match(/code is: (\d{6})/);expect(code).not.toBeNull();
 await page.getByLabel('Verification code').fill(code[1]);await page.getByRole('button',{name:'Verify email',exact:true}).click();await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();
 await expect(page.getByLabel('Primary email')).toHaveValue(email);await page.getByLabel('Full name',{exact:true}).fill('Local Test Person');await page.getByLabel('Display name',{exact:true}).fill('Local Tester');await page.getByLabel('Date of birth (required)').fill('1990-01-01');
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();
 await page.screenshot({path:'../artifacts/browser-testing/ready-browser-test.png',fullPage:true});
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('isolated host retains the real HTTPS origin and antiforgery boundaries',async({request})=>{
 const status=await request.get('/browser-testing/status');expect(status.status()).toBe(200);expect((await status.json()).mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');
 const foreign=await request.post('/registration/register',{headers:{Origin:'https://outside.example.test'},data:{}});expect(foreign.status()).toBe(403);
 const csrf=await request.post('/registration/register',{headers:{Origin:'https://localhost:'+state.ApplicationPort},data:{}});expect(csrf.status()).toBe(400);
});
