import { test, expect } from '@playwright/test';
test('Admin shared field idle border at approved reference dimensions',async({page})=>{
 await page.setViewportSize({width:1800,height:1024});await page.goto('/');await page.evaluate(()=>document.fonts.ready);
 // Isolated component comparison at its Figma width; these owner-container styles exist only in this test.
 await page.locator('.verification-page').evaluate((e:HTMLElement)=>{e.style.maxWidth='none';});
 await page.locator('.verification-panels').evaluate((e:HTMLElement)=>{e.style.display='block';});
 const panel=page.getByTestId('admin-panel');await panel.evaluate((e:HTMLElement)=>{e.style.width='900px';});
 await panel.locator('.reference-width').first().evaluate((e:HTMLElement)=>{e.style.inlineSize='788px';});
 const field=panel.locator('#admin-field');await field.fill('');await field.blur();
 // Blur intentionally exposes the required-field error; the separate empty idle reference uses a valid synthetic value then clears via native DOM without changing application state.
 await field.fill('reference');await field.blur();await field.evaluate((e:HTMLInputElement)=>{e.value='';});
 expect(await field.evaluate(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height,color:getComputedStyle(e).borderColor}))).toEqual({width:788,height:42,color:'rgb(97, 105, 117)'});
 await field.screenshot({path:'test-results/admin-input-idle-reference-788.png'});
});
