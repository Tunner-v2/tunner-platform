import {chromium} from '../../../web/node_modules/@playwright/test/index.mjs';
import {AxeBuilder} from '../../../web/node_modules/@axe-core/playwright/dist/index.mjs';
import {fileURLToPath} from 'node:url';
import fs from 'node:fs/promises';
import path from 'node:path';
const root=path.dirname(fileURLToPath(import.meta.url));
const source=await fs.readFile(path.join(root,'preview.tsx'),'utf8');
const allStates=JSON.parse(source.match(/export const states=(\[[^;]+\]);/)[1].replaceAll("'",'"'));
const filter=process.argv.find(a=>a.startsWith('--states='))?.slice(9).split(',');const states=filter?allStates.filter(s=>filter.includes(s)):allStates;
await fs.mkdir(path.join(root,'rendered'),{recursive:true});
const results=[],accessibility=[],keyboard=[];
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 for(const width of [1440,390,320])for(const state of states){
  const context=await browser.newContext({viewport:{width,height:width===1440?1100:844}});const page=await context.newPage();const issues=[];
  page.on('pageerror',error=>issues.push(error.message));
  await page.goto(`http://127.0.0.1:5198/preview.html?state=${state}`);
  await page.waitForSelector('main');await page.evaluate(()=>document.fonts.ready);
  const metrics=await page.evaluate(()=>{const rect=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}};return {viewport:{width:innerWidth,height:innerHeight},overflow:document.documentElement.scrollWidth>innerWidth,bodyWidth:document.body.scrollWidth,dialog:rect('dialog'),status:rect('.totp-status'),details:rect('.details'),danger:rect('.danger'),setupCard:rect('.setup-card'),qr:rect('.qr-example'),key:rect('.manual-key'),code:rect('.setup-code .tn-input'),ready:rect('.ready-card'),login:rect('.totp-login'),fonts:{body:document.fonts.check('14px "Geist Variable"'),mono:document.fonts.check('11px "Geist Mono"'),inter:document.fonts.check('24px "Inter Variable"')},clipped:[...document.querySelectorAll('button,input,.manual-key,.state-notice,.totp-panel,.passkeys-dialog-content,.step-up-card,.totp-login')].filter(e=>e.scrollWidth>e.clientWidth+1).map(e=>e.className)}});
  if(metrics.overflow)issues.push('horizontal-overflow');if(metrics.clipped.length)issues.push('component-clipping');if(Object.values(metrics.fonts).some(v=>!v))issues.push('font-not-loaded');
  await page.screenshot({path:path.join(root,'rendered',`${state}-${width}.png`),fullPage:!metrics.dialog});
  if(width===1440&&metrics.dialog)await page.locator('dialog').screenshot({path:path.join(root,'rendered',`${state}-component-1440.png`)});
  if(['manage-active','setup','setup-error','setup-consumed','ready','remove-sessions','proof-code','proof-error','login-mfa','login-mfa-error'].includes(state)){
   const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();accessibility.push({state,width,violations:a.violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})),incomplete:a.incomplete.map(v=>({id:v.id,count:v.nodes.length}))});
  }
  if(width===320&&metrics.dialog&&!['proof-pending'].includes(state)){
   const first=await page.evaluate(()=>document.activeElement?.tagName+':'+(document.activeElement?.id||document.activeElement?.textContent?.trim()));const count=await page.locator('dialog button:enabled,dialog input:enabled').count();
   for(let i=0;i<count;i++)await page.keyboard.press('Tab');const after=await page.evaluate(()=>document.activeElement?.tagName+':'+(document.activeElement?.id||document.activeElement?.textContent?.trim()));
   await page.keyboard.press('Shift+Tab');const inside=await page.evaluate(()=>Boolean(document.activeElement?.closest('dialog')));keyboard.push({state,width,initial:first,afterCycle:after,cycleWrapped:first===after,reverseRemainsInDialog:inside,boundary:'Shared ModalDialog focus trap in isolated proposal. Escape/action wiring and trigger restoration are not implemented by this static design and need production integration tests.'});
  }
  results.push({state,width,metrics,issues});await context.close();
 }
}finally{await browser.close();}
const boundary='Isolated nonfunctional public-example design review only. No backend/native/provisioning/route/security/Product acceptance.';
let priorChecks={results:[],keyboard:[]},priorAxe={results:[]};if(filter){priorChecks=JSON.parse(await fs.readFile(path.join(root,'rendered/checks.json'),'utf8'));priorAxe=JSON.parse(await fs.readFile(path.join(root,'rendered/accessibility.json'),'utf8'));}
await fs.writeFile(path.join(root,'rendered/checks.json'),JSON.stringify({boundary,results:[...priorChecks.results.filter(r=>!states.includes(r.state)),...results],keyboard:[...priorChecks.keyboard.filter(r=>!states.includes(r.state)),...keyboard]},null,2));
await fs.writeFile(path.join(root,'rendered/accessibility.json'),JSON.stringify({boundary,results:[...priorAxe.results.filter(r=>!states.includes(r.state)),...accessibility]},null,2));
console.log(JSON.stringify({renders:results.length,issues:results.filter(r=>r.issues.length).map(r=>({state:r.state,width:r.width,issues:r.issues})),axeCases:accessibility.length,violations:accessibility.reduce((n,r)=>n+r.violations.length,0),keyboardCases:keyboard.length,keyboardFailures:keyboard.filter(k=>!k.cycleWrapped||!k.reverseRemainsInDialog).length},null,2));
