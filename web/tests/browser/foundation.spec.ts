import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

for (const surface of ['user', 'admin'] as const) {
  test(`${surface}: source metrics, structural roles and font load`, async ({ page }) => {
    await page.goto('/'); await page.evaluate(() => document.fonts.ready);
    const panel = page.getByTestId(`${surface}-panel`);
    const primary = panel.locator('form .tn-primary-button').first();
    const metrics = await primary.evaluate(element => { const s = getComputedStyle(element); return {height:element.getBoundingClientRect().height,color:s.color,bg:s.backgroundColor,radius:s.borderRadius,size:s.fontSize,weight:s.fontWeight,font:s.fontFamily,shadow:s.boxShadow}; });
    expect(metrics.height).toBe(surface === 'user' ? 44 : 40); expect(metrics.bg).toBe(surface === 'user' ? 'rgb(79, 70, 229)' : 'rgb(9, 9, 10)');
    expect(metrics.color).toBe('rgb(255, 255, 255)'); expect(metrics.radius).toBe('8px'); expect(metrics.size).toBe(surface === 'user' ? '14px' : '12px'); expect(metrics.weight).toBe('600'); expect(metrics.font).toContain(surface === 'user' ? 'Geist Variable' : 'Inter Variable');
    expect(metrics.shadow === 'none').toBe(surface === 'admin');
    const field = panel.locator(`#${surface}-field`);
    const measured = await field.evaluate(element => { const s=getComputedStyle(element); return {height:element.getBoundingClientRect().height,radius:s.borderRadius,border:s.borderColor,padding:s.paddingInlineStart,size:s.fontSize}; });
    expect(measured).toEqual({height:surface === 'user'?44:42,radius:surface === 'user'?'8px':'10px',border:surface==='user'?'rgb(71, 85, 105)':'rgb(97, 105, 117)',padding:'14px',size:'14px'});
    expect(await page.evaluate(() => document.fonts.check('600 14px "Geist Variable"') && document.fonts.check('600 14px "Inter Variable"'))).toBe(true);
    const brand = panel.locator('.tn-brand'); await expect(brand).toHaveText('Tunner'); await expect(brand.locator('svg,img')).toHaveCount(0);
  });
  test(`${surface}: keyboard focus, controlled typing and no implicit submit`, async ({page}) => {
    await page.goto('/'); const panel=page.getByTestId(`${surface}-panel`); const input=panel.locator(`#${surface}-field`);
    await input.focus(); await input.fill('owner updated value'); await expect(input).toHaveValue('owner updated value');
    await page.keyboard.press('Tab'); const button=panel.locator('form .tn-primary-button').first(); await expect(button).toBeFocused();
    expect(await button.evaluate(e=>getComputedStyle(e).outlineStyle)).toBe('solid');
    await page.keyboard.press('Enter'); await expect(page.getByTestId(`${surface}-count`)).toHaveText('1');
    await expect(panel.locator('button:disabled')).toBeDisabled(); await panel.locator('button:disabled').evaluate((element:HTMLButtonElement)=>element.click()); await expect(page.getByTestId(`${surface}-count`)).toHaveText('1');
  });
  test(`${surface}: error descriptions and source-aligned focus`, async ({page}) => {
    await page.goto('/'); const panel=page.getByTestId(`${surface}-panel`); const input=panel.locator(`#${surface}-field`);
    await input.focus(); const style=await input.evaluate(e=>{const s=getComputedStyle(e);return {border:s.borderColor,shadow:s.boxShadow,outline:s.outlineStyle};});
    expect(style.border).toBe(surface==='user'?'rgb(99, 102, 241)':'rgb(20, 22, 24)'); expect(style.outline).toBe('solid'); if(surface==='user') expect(style.shadow).toContain('0.15');
    const error=panel.locator(`#${surface}-error`); await expect(error).toHaveAttribute('aria-invalid','true'); await expect(error).toHaveAttribute('aria-describedby',`${surface}-error-help ${surface}-error-error`); await expect(panel.locator(`#${surface}-error-error`)).toContainText('Enter the value');
  });
  test(`${surface}: publication refresh updates copy and provenance`, async({page})=>{
    await page.goto('/'); const panel=page.getByTestId(`${surface}-panel`); const text=panel.locator('form .tn-primary-button').first().locator('[data-content-key="action"]');
    await expect(text).toHaveAttribute('data-content-version','fixture-v1'); await panel.getByRole('button',{name:'Refresh fixture publication'}).click(); await expect(text).toHaveAttribute('data-content-version','fixture-v2'); await expect(text).toContainText('Updated');
  });
}
test('axe WCAG 2.2 AA reports no violations', async({page})=>{await page.goto('/');await page.evaluate(()=>document.fonts.ready);const results=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(results.violations).toEqual([]);});
test('narrow width reflows long content without clipping', async({page})=>{
  await page.setViewportSize({width:320,height:1100});await page.goto('/');await page.evaluate(()=>document.fonts.ready);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  for (const surface of ['user','admin']) { const long=page.getByTestId(`${surface}-panel`).locator('[data-content-key="long"]');expect(await long.evaluate(e=>e.getBoundingClientRect().height)).toBeGreaterThan(18); }
  await page.screenshot({path:'test-results/narrow-light.png',fullPage:true});
});
test('isolated surface roles and TailAdmin bridge survive side-by-side use', async({page})=>{
  await page.goto('/');for(const surface of ['user','admin']){const panel=page.getByTestId(`${surface}-panel`);const utility=panel.getByRole('button',{name:'Refresh fixture publication'});expect(await utility.evaluate(e=>getComputedStyle(e).backgroundColor)).toBe('rgb(255, 255, 255)');expect(await panel.locator('.tn-input').first().evaluate(e=>getComputedStyle(e).fontFamily)).toContain(surface==='user'?'Geist Variable':'Inter Variable');}
  expect(await page.evaluate(()=>document.documentElement.className)).toBe('');
});
test('fonts and rendering use only local assets and no persisted state', async({page})=>{
  const external:string[]=[]; page.on('request',request=>{if(!request.url().startsWith('http://127.0.0.1:5188')&&!request.url().startsWith('data:')) external.push(request.url());});
  await page.goto('/');await page.evaluate(()=>document.fonts.ready);expect(external).toEqual([]);expect(await page.evaluate(()=>({local:localStorage.length,session:sessionStorage.length}))).toEqual({local:0,session:0});await page.screenshot({path:'test-results/desktop-light.png',fullPage:true});
});

test('nested Admin mode does not leak into either surrounding User control', async({page})=>{
  await page.goto('/');const buttons=page.getByTestId('nested-themes').locator('.tn-primary-button');
  expect(await buttons.evaluateAll(elements=>elements.map(e=>getComputedStyle(e).backgroundColor))).toEqual(['rgb(79, 70, 229)','rgb(9, 9, 10)','rgb(79, 70, 229)']);
  const outerScope=page.getByTestId('nested-themes');await expect(outerScope.getByRole('button',{name:'Nested Admin example'})).toBeVisible();
});
for(const surface of ['user','admin'] as const){
 test(surface+': shared validation handles submit, blur, correction, focus and duplicate submission',async({page})=>{
  await page.goto('/');const panel=page.getByTestId(surface+'-panel');const field=panel.locator('#'+surface+'-field');const form=panel.locator('form');
  await expect(form).toHaveAttribute('novalidate','');await field.fill('');await field.blur();await expect(field).toHaveAttribute('aria-invalid','true');
  await form.evaluate((element:HTMLFormElement)=>element.requestSubmit());await expect(field).toBeFocused();await expect(form.getByRole('alert')).toBeVisible();
  expect(await field.evaluate(e=>getComputedStyle(e).borderColor)).toBe('rgb(185, 28, 28)');
  expect(await field.evaluate(e=>{const css=getComputedStyle(e);return {approvedWidth:css.getPropertyValue('--tn-primitive-invalid-border-width').trim(),renderedWidth:parseFloat(css.borderTopWidth),pixelRatio:devicePixelRatio};})).toEqual({approvedWidth:'1.5px',renderedWidth:Math.floor(1.5*await page.evaluate(()=>devicePixelRatio))/await page.evaluate(()=>devicePixelRatio),pixelRatio:await page.evaluate(()=>devicePixelRatio)});
  const errorId=await field.getAttribute('aria-describedby');expect(errorId).toBeTruthy();
  await field.fill('x'.repeat(201));await expect(field).toHaveAttribute('aria-invalid','true');await field.fill('corrected owner value');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(form.getByRole('alert')).toHaveCount(0);
  await form.evaluate((element:HTMLFormElement)=>{element.requestSubmit();element.requestSubmit();});await expect(field).toBeDisabled();await expect(page.getByTestId(surface+'-count')).toHaveText('10');await expect(field).toBeEnabled();
  const report=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(report.violations).toEqual([]);
  await field.fill('');await form.evaluate((element:HTMLFormElement)=>element.requestSubmit());await expect(field).toBeFocused();await page.screenshot({path:'test-results/shared-'+surface+'-validation-desktop.png',fullPage:true});
  await page.setViewportSize({width:320,height:1100});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'test-results/shared-'+surface+'-validation-mobile.png',fullPage:true});
 });
}
