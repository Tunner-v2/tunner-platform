import {test,expect} from '@playwright/test';
import type {Page} from '@playwright/test';

const fixture='/browser-testing/recovery-codes-assets/';
const marker='XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX';
const count=(page:Page,name:string)=>page.locator('[data-fixture-count="'+name+'"]');
async function open(page:Page,name:string){await page.goto(fixture);await page.getByRole('button',{name,exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();}

test('mounting the controlled component causes no owner calls or local request',async({page})=>{
 const ownerCalls:string[]=[];page.on('request',request=>{if(/^\/(account|authentication)\//.test(new URL(request.url()).pathname))ownerCalls.push(request.url());});
 await page.goto(fixture);await expect(page.getByRole('heading',{name:'Backup codes',exact:true})).toBeVisible();
 await expect(count(page,'starts')).toHaveText('0');await expect(count(page,'downloads')).toHaveText('0');await expect(page.getByRole('dialog')).toHaveCount(0);expect(ownerCalls).toEqual([]);
});

test('missing or stale impact cannot continue; current owner text is preserved',async({page})=>{
 await open(page,'Show missing impact');await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await expect(page.getByText('Replacement details are unavailable. Check status before continuing.',{exact:true})).toBeVisible();await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByRole('button',{name:'Show stale review',exact:true}).click();await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByRole('button',{name:'Show current review',exact:true}).click();await expect(page.getByText('PUBLIC OWNER IMPACT — this nonfunctional fixture selects no replacement or session policy.',{exact:true})).toBeVisible();await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(count(page,'continues')).toHaveText('1');
});

test('blocked review checks the original owner without enabling Continue',async({page})=>{
 await open(page,'Show missing impact');await page.getByRole('button',{name:'Check status',exact:true}).click();await expect(count(page,'checks')).toHaveText('1');await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await expect(count(page,'continues')).toHaveText('0');await expect(count(page,'starts')).toHaveText('0');await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeEnabled();
});

test('requested callback occurs once and status announces no sample or saved claim',async({page})=>{
 await open(page,'Show eight samples');const button=page.getByRole('button',{name:'Download',exact:true});await button.focus();await page.keyboard.press('Enter');
 await expect(button).toBeDisabled();await expect(count(page,'downloads')).toHaveText('1');await expect(page.getByRole('button',{name:'Done',exact:true})).toBeFocused();
 await expect(page.getByRole('status')).toContainText('Download requested');await expect(page.getByRole('status')).toContainText('Tunner cannot confirm that the file was saved.');await expect(page.getByRole('status')).not.toContainText(marker);
 await button.dispatchEvent('click');await expect(count(page,'downloads')).toHaveText('1');await expect(page.locator('.recovery-codes-grid code')).toHaveText(Array.from({length:8},()=>marker));
});

test('Escape clears the presentation and returns keyboard focus to its actual trigger',async({page})=>{
 await open(page,'Show eight samples');await expect(page.locator('.recovery-codes-grid code')).toHaveCount(8);await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByText(marker,{exact:true})).toHaveCount(0);await expect(count(page,'closes')).toHaveText('1');await expect(page.getByRole('button',{name:'Show eight samples',exact:true})).toBeFocused();
});

test('busy review blocks Escape and all actions',async({page})=>{
 await open(page,'Show busy review');await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('button',{name:'Cancel',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Check status',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Continue',exact:true})).toBeDisabled();await expect(count(page,'checks')).toHaveText('0');await expect(count(page,'closes')).toHaveText('0');await expect(count(page,'continues')).toHaveText('0');
});

test('lost body stays neutral and distinct from known presentation; neither offers download',async({page})=>{
 await open(page,'Show lost body');await expect(page.getByRole('heading',{name:'These codes are unavailable',exact:true})).toBeVisible();await expect(page.getByRole('dialog')).not.toContainText('already shown');await expect(page.getByRole('button',{name:'Download',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Close',exact:true}).click();
 await page.getByRole('button',{name:'Show known presentation',exact:true}).click();await expect(page.getByRole('heading',{name:'These codes were already shown',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Download',exact:true})).toHaveCount(0);await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);
});

test('unknown Check status invokes its original-operation callback without generating or revealing',async({page})=>{
 await open(page,'Show unknown result');await page.getByRole('button',{name:'Check status',exact:true}).click();await expect(count(page,'checks')).toHaveText('1');await expect(count(page,'starts')).toHaveText('0');await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);await expect(page.getByRole('button',{name:'Download',exact:true})).toHaveCount(0);
});

test('variable controlled sample count and no-download choice do not imply six codes',async({page})=>{
 await open(page,'Show three samples');await expect(page.locator('.recovery-codes-grid code')).toHaveCount(3);await expect(page.getByRole('button',{name:'Download',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Done',exact:true}).click();await expect(count(page,'downloads')).toHaveText('0');await expect(page.locator('.recovery-codes-grid')).toHaveCount(0);
});

test('shared modal traps Tab in both directions around requested controls',async({page})=>{
 await open(page,'Show eight samples');const download=page.getByRole('button',{name:'Download',exact:true}),done=page.getByRole('button',{name:'Done',exact:true});await done.focus();await page.keyboard.press('Tab');await expect(download).toBeFocused();await page.keyboard.press('Shift+Tab');await expect(done).toBeFocused();
});
test('both authoritative card actions retain distinct callbacks in the same card',async({page})=>{
 await page.goto(fixture);await page.getByRole('button',{name:'Show both card actions',exact:true}).click();await page.getByRole('button',{name:'Generate codes',exact:true}).click();await expect(count(page,'last-action')).toHaveText('generate');await page.getByRole('button',{name:'Replace codes',exact:true}).click();await expect(count(page,'last-action')).toHaveText('replace');await expect(count(page,'starts')).toHaveText('2');await expect(page.locator('.recovery-codes-card')).toHaveCount(1);
});
test('a pending original request exposes Check status and blocks fresh card starts',async({page})=>{
 await page.goto(fixture);await page.getByRole('button',{name:'Show pending request',exact:true}).click();await expect(page.getByRole('button',{name:'Generate codes',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'Replace codes',exact:true})).toBeDisabled();await page.locator('.recovery-codes-card').getByRole('button',{name:'Check status',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('We couldn’t confirm the result');await expect(count(page,'starts')).toHaveText('0');await expect(count(page,'checks')).toHaveText('1');
});

for(const [width,height] of [[390,844],[390,640],[320,568],[390,480],[320,480],[1366,768]] as const){
 test('requested Download keeps keyboard Done fully inside the scroll viewport at '+width+'x'+height,async({page})=>{
  await page.setViewportSize({width,height});await open(page,'Show eight samples');await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Download',exact:true})).toBeFocused();await page.keyboard.press('Enter');await expect(page.getByRole('status')).toContainText('Download requested');
  const done=page.getByRole('button',{name:'Done',exact:true});await expect(done).toBeFocused();await expect.poll(()=>done.evaluate(button=>{const dialog=button.closest('dialog')!,box=button.getBoundingClientRect(),frame=dialog.getBoundingClientRect(),top=Math.max(frame.top+dialog.clientTop,0),bottom=Math.min(frame.top+dialog.clientTop+dialog.clientHeight,innerHeight);return box.top>=top-1&&box.bottom<=bottom+1;})).toBe(true);
 });
}

for(const width of [1440,1366,1101,1100,768,390,320]){
 test('39-character public samples reflow without clipping at '+width,async({page})=>{
  await page.setViewportSize({width,height:width===1366?768:1100});await open(page,'Show eight samples');await expect(page.locator('.recovery-codes-grid code')).toHaveText(Array.from({length:8},()=>marker));
  const geometry=await page.locator('.recovery-codes-grid').evaluate(grid=>{const dialog=grid.closest('dialog')!,box=dialog.getBoundingClientRect();return{pageOverflow:document.documentElement.scrollWidth>innerWidth,dialogOverflow:dialog.scrollWidth>dialog.clientWidth+1,gridOverflow:grid.scrollWidth>grid.clientWidth+1,inside:box.left>=0&&box.right<=innerWidth+1,codes:Array.from(grid.querySelectorAll('code')).every(code=>{const cell=code.parentElement!.getBoundingClientRect();return Array.from(code.getClientRects()).every(line=>line.left>=cell.left-1&&line.right<=cell.right+1);})};});
  expect(geometry).toEqual({pageOverflow:false,dialogOverflow:false,gridOverflow:false,inside:true,codes:true});await page.getByRole('button',{name:'Done',exact:true}).focus();await page.keyboard.press('Enter');await expect(page.getByRole('dialog')).toHaveCount(0);
 });
 test('long server-impact text reflows and actions remain reachable at '+width,async({page})=>{
  await page.setViewportSize({width,height:width===1366?768:1100});await open(page,'Show long review');const dialog=page.getByRole('dialog');await expect(dialog).toContainText('PUBLIC OWNER IMPACT');
  expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);await page.getByRole('button',{name:'Continue',exact:true}).focus();await page.keyboard.press('Enter');await expect(count(page,'continues')).toHaveText('1');await page.getByRole('button',{name:'Cancel',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog).toHaveCount(0);
 });
}
