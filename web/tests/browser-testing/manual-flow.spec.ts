import { test, expect } from '@playwright/test';
import {registerVerifiedAccount,runtime} from './fixtures';
test('real HTTPS registration and SMTP verification complete through the isolated starter',async({page,request})=>{
 await registerVerifiedAccount(page,request);
 await page.screenshot({path:'../artifacts/browser-testing/ready-browser-test.png',fullPage:true});
 expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});
});
test('isolated host retains the real HTTPS origin and antiforgery boundaries',async({request})=>{
 const status=await request.get('/browser-testing/status');expect(status.status()).toBe(200);expect((await status.json()).mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');
 const foreign=await request.post('/registration/register',{headers:{Origin:'https://outside.example.test'},data:{}});expect(foreign.status()).toBe(403);
 const csrf=await request.post('/registration/register',{headers:{Origin:'https://localhost:'+runtime.ApplicationPort},data:{}});expect(csrf.status()).toBe(400);
});
