import {expect} from '@playwright/test';
import type {APIRequestContext,Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import {randomUUID} from 'node:crypto';

export const runtime: {ApplicationPort:number;MailpitPort:number}=JSON.parse(
  readFileSync(new URL('../../../artifacts/browser-testing/runtime.json',import.meta.url),'utf8').replace(/^\uFEFF/,''));

/** Real isolated BFF/SMTP onboarding; verification values remain in memory. */
export async function registerVerifiedAccount(page:Page,request:APIRequestContext,dateOfBirth='1990-01-01'){
  const email='browser-'+randomUUID()+'@example.test';
  const password='Local unique browser passphrase '+randomUUID();
  await page.goto('/registration');
  await expect(page.getByRole('heading',{name:'Create your Tunner account'})).toBeVisible();
  await expect(page.locator('input[type=date]')).toHaveCount(0);
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password',{exact:true}).fill(password);
  await page.getByLabel('Confirm password').fill(password);
  await page.getByRole('checkbox').check();
  const registration=page.waitForResponse(r=>r.url().endsWith('/registration/register')&&r.request().method()==='POST');
  await page.getByRole('button',{name:'Continue',exact:true}).click();
  expect((await registration).status()).toBe(202);
  await expect(page.getByRole('heading',{name:'Check your email'})).toBeVisible();
  let messageId='';
  await expect.poll(async()=>{
    const response=await request.get('http://127.0.0.1:'+runtime.MailpitPort+'/api/v1/search',{params:{query:'to:'+email,limit:10}});
    if(!response.ok())return false;
    const inbox=await response.json() as {messages:Array<{ID:string;To:Array<{Address:string}>}>};
    messageId=inbox.messages.find(message=>message.To.some(address=>address.Address===email))?.ID??'';
    return Boolean(messageId);
  },{timeout:20000}).toBe(true);
  const response=await request.get('http://127.0.0.1:'+runtime.MailpitPort+'/api/v1/message/'+encodeURIComponent(messageId));
  expect(response.ok()).toBe(true);
  const mail=await response.json() as {Text:string};
  const code=mail.Text.match(/code is: (\d{6})/);
  expect(Boolean(code?.[1])).toBe(true);
  await page.getByLabel('Verification code').fill(code![1]!);
  await page.getByRole('button',{name:'Verify email',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Personal Information'})).toBeVisible();
  await expect(page.getByLabel('Primary email')).toHaveValue(email);
  await page.getByLabel('Full name',{exact:true}).fill('Synthetic Browser Test');
  await page.getByLabel('Display name',{exact:true}).fill('Browser Tester');
  await page.getByLabel('Date of birth (required)').fill(dateOfBirth);
  await page.getByRole('button',{name:'Continue',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Your email is verified'})).toBeVisible();
  return {email,password};
}
