import {chromium} from '../../../web/node_modules/@playwright/test/index.mjs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import fs from 'node:fs/promises';
const root=path.dirname(fileURLToPath(import.meta.url));const results=[];
const browser=await chromium.launch({channel:'msedge',headless:true});
try{for(const state of ['remove-preserve','remove-sessions','reset-candidate','reset-disable-first','proof-code','proof-error','proof-expired','proof-unknown']){const page=await browser.newPage({viewport:{width:320,height:844}});await page.goto('http://127.0.0.1:5198/preview.html?state='+state);await page.waitForSelector('dialog[open]');
const describe=()=>document.activeElement?.tagName+':'+(document.activeElement?.id||document.activeElement?.textContent?.trim());
const initial=await page.evaluate(describe);await page.keyboard.press('Tab');const first=await page.evaluate(describe);const count=await page.locator('dialog button:enabled,dialog input:enabled').count();for(let i=0;i<count;i++)await page.keyboard.press('Tab');const after=await page.evaluate(describe);await page.keyboard.press('Shift+Tab');const reverse=await page.evaluate(()=>Boolean(document.activeElement?.closest('dialog')));results.push({state,width:320,initial,firstTabStop:first,afterCycle:after,cycleWrapped:first===after,reverseRemainsInDialog:reverse,boundary:'Shared ModalDialog initial title focus and forward/reverse trap only. Static draft actions/Escape callbacks are inert; production owner close/trigger restoration and first-invalid integration remain untested.'});await page.close();}}finally{await browser.close();}
const checks=JSON.parse(await fs.readFile(path.join(root,'rendered/checks.json'),'utf8'));checks.keyboard=results;await fs.writeFile(path.join(root,'rendered/checks.json'),JSON.stringify(checks,null,2));console.log(JSON.stringify({cases:results.length,failures:results.filter(r=>!r.cycleWrapped||!r.reverseRemainsInDialog)},null,2));
