import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {createRequire} from 'node:module';
import {execFileSync,spawn} from 'node:child_process';

const root=execFileSync('git',['rev-parse','--show-toplevel'],{encoding:'utf8'}).trim();
const web=path.join(root,'web'),require=createRequire(path.join(web,'package.json'));
const {chromium}=require('@playwright/test'),AxeBuilder=require('@axe-core/playwright').default;
const carrier='c71495cbcb1e0e6f8e1dd89c3f42c59b1ed834a6';
const base='artifacts/p2-009-recovery-20261111/ui-refinement/integration-v2';
const prefix=base+'/mobile-current-c71495c',out=path.join(root,prefix);
const read=p=>fs.readFileSync(path.join(root,p)),hash=b=>createHash('sha256').update(b).digest('hex');
const ref=p=>({path:p,sha256:hash(read(p))});
const write=(p,v)=>fs.writeFileSync(path.join(root,p),JSON.stringify(v,null,2)+'\n');
const head=()=>execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
const original=JSON.parse(read(base+'/implementation-public-c71495c/source-inputs-before.json'));
if(head()!==carrier)throw Error('Capture carrier changed');
for(const row of original.files)if(hash(read(row.path))!==row.sha256)throw Error('Capture input changed');
fs.mkdirSync(path.join(out,'rendered'),{recursive:true});
fs.mkdirSync(path.join(out,'axe'),{recursive:true});
write(prefix+'/source-inputs-before.json',{invocation_sha:carrier,input_count:original.files.length,files:original.files});
let occupied=false;
try{await fetch('http://127.0.0.1:5239/',{signal:AbortSignal.timeout(700)});occupied=true;}catch{}
if(occupied)throw Error('Unknown process owns capture port');
const serverStarted=new Date().toISOString();
const server=spawn(process.execPath,['node_modules/vite/bin/vite.js','--config','fixtures/recovery-codes/vite.config.ts'],{cwd:web,windowsHide:true,stdio:['ignore',fs.openSync(path.join(out,'vite-stdout.log'),'w'),fs.openSync(path.join(out,'vite-stderr.log'),'w')]});
let closed=false;const terminal=new Promise(resolve=>server.once('exit',()=>{closed=true;resolve();}));
let browser,browserClosed=false,error=null;const rows=[];
const rgb=s=>{const n=s.match(/[\d.]+/g)?.map(Number);return n?.length>=3?n.slice(0,3):null;};
const lum=a=>a.map(n=>{const c=n/255;return c<=.04045?c/12.92:((c+.055)/1.055)**2.4;}).reduce((n,v,i)=>n+v*[.2126,.7152,.0722][i],0);
const contrast=(a,b)=>{a=rgb(a);b=rgb(b);if(!a||!b)return null;const x=lum(a),y=lum(b);return(Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
try{
 let ready=false;
 for(let i=0;i<100&&!ready;i++){try{ready=(await fetch('http://127.0.0.1:5239/browser-testing/recovery-codes-assets/',{signal:AbortSignal.timeout(500)})).ok;}catch{}if(!ready)await new Promise(r=>setTimeout(r,100));}
 if(!ready)throw Error('Owned capture fixture unavailable');
 browser=await chromium.launch({channel:'msedge'});
 for(const [width,height] of [[390,844],[390,640],[320,568],[390,480],[320,480],[1366,768]]){
  const name=width+'x'+height,context=await browser.newContext({viewport:{width,height},acceptDownloads:false,serviceWorkers:'block'});
  const page=await context.newPage(),fontReplies=[],fontWork=[];let ownerCalls=0,downloadEvents=0;
  page.on('download',()=>downloadEvents++);
  page.on('request',request=>{if(/^\/(account|authentication)\//.test(new URL(request.url()).pathname))ownerCalls++;});
  page.on('response',response=>{if(/\.(woff2?|ttf)(?:\?|$)/.test(new URL(response.url()).pathname))fontWork.push((async()=>{const headers=await response.allHeaders();fontReplies.push({pathname:new URL(response.url()).pathname,status:response.status(),content_type:headers['content-type'],sha256:hash(await response.body())});})());});
  try{
   await page.goto('http://127.0.0.1:5239/browser-testing/recovery-codes-assets/');
   await page.getByRole('button',{name:'Show eight samples',exact:true}).click();
   await page.evaluate(()=>document.fonts.ready);
   await page.keyboard.press('Tab');
   const downloadFocused=await page.getByRole('button',{name:'Download',exact:true}).evaluate(e=>e===document.activeElement);
   await page.keyboard.press('Enter');
   await page.getByText('Download requested',{exact:true}).waitFor();
   await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
   const geometry=await page.getByRole('button',{name:'Done',exact:true}).evaluate(button=>{
    const dialog=button.closest('dialog'),b=button.getBoundingClientRect(),d=dialog.getBoundingClientRect(),s=getComputedStyle(button);
    const top=Math.max(d.top+dialog.clientTop,0),bottom=Math.min(d.top+dialog.clientTop+dialog.clientHeight,innerHeight),left=Math.max(d.left+dialog.clientLeft,0),right=Math.min(d.left+dialog.clientLeft+dialog.clientWidth,innerWidth);
    const ring=parseFloat(s.outlineWidth)+Math.max(0,parseFloat(s.outlineOffset));
    return{focused:button===document.activeElement,button:{top:b.top,bottom:b.bottom,left:b.left,right:b.right},clip:{top,bottom,left,right},fully_visible:b.top>=top-1&&b.bottom<=bottom+1,ring_fully_visible:b.top-ring>=top-1&&b.bottom+ring<=bottom+1&&b.left-ring>=left-1&&b.right+ring<=right+1,scroll_top:dialog.scrollTop,outline:{color:s.outlineColor,width:s.outlineWidth,style:s.outlineStyle,offset:s.outlineOffset,adjacent_background:getComputedStyle(dialog).backgroundColor},page_overflow:document.documentElement.scrollWidth>innerWidth,dialog_overflow_x:dialog.scrollWidth>dialog.clientWidth+1};
   });
   const picture=prefix+'/rendered/requested-'+name+'.png';
   await page.screenshot({path:path.join(root,picture)});
   const axe=await new AxeBuilder({page}).include('dialog').analyze(),axePath=prefix+'/axe/requested-'+name+'.json';
   write(axePath,axe);
   const manual=[];
   for(const rule of axe.incomplete)for(const node of rule.nodes){
    const target=node.target;
    if(rule.id!=='color-contrast'||target.length!==1||typeof target[0]!=='string'){manual.push({rule:rule.id,target,result:'UNRESOLVED',reason:'No applicable bounded manual procedure'});continue;}
    const element=page.locator(target[0]);
    if(await element.count()!==1){manual.push({rule:rule.id,target,result:'UNRESOLVED',reason:'Target identity unavailable'});continue;}
    await element.scrollIntoViewIfNeeded();
    const probe=await element.evaluate(node=>{
     const style=getComputedStyle(node),layers=[];let background=null;
     for(let e=node;e;e=e.parentElement){const s=getComputedStyle(e);layers.push({tag:e.tagName.toLowerCase(),background:s.backgroundColor,opacity:s.opacity,image:s.backgroundImage});if(!background&&s.backgroundColor!=='transparent'&&s.backgroundColor!=='rgba(0, 0, 0, 0)')background=s.backgroundColor;}
     const walker=document.createTreeWalker(node,NodeFilter.SHOW_TEXT),lines=[];
     for(let text=walker.nextNode();text;text=walker.nextNode()){const range=document.createRange();range.selectNodeContents(text);for(const r of range.getClientRects()){if(!r.width||!r.height)continue;const hit=document.elementFromPoint(r.left+r.width/2,r.top+r.height/2);lines.push({top:r.top,bottom:r.bottom,left:r.left,right:r.right,unobscured:!!hit&&(hit===node||node.contains(hit)||hit.contains(node))});}}
     return{foreground:style.color,background,font_size:style.fontSize,font_weight:style.fontWeight,layers,lines,inactive:!!node.closest('button:disabled')};
    });
    const value=probe.background?contrast(probe.foreground,probe.background):null;
    const required=parseFloat(probe.font_size)>=24||parseFloat(probe.font_size)>=18.6667&&parseInt(probe.font_weight)>=700?3:4.5;
    const opaque=probe.layers.every(l=>l.opacity==='1'&&l.image==='none'),unobscured=probe.lines.length>0&&probe.lines.every(l=>l.unobscured);
    manual.push({rule:rule.id,target,...probe,contrast_ratio:value,required,opaque_ancestry:opaque,text_unobscured:unobscured,audit_only_scroll:true,result:probe.inactive?'NOT_APPLICABLE_INACTIVE_CONTROL':value!==null&&value>=required&&opaque&&unobscured?'PASS_MANUAL_EXACT_TARGET':'UNRESOLVED',scope:'Raw Axe remains incomplete. Audit-only target scroll is separate from the preceding user-keyboard visibility proof.'});
   }
   await Promise.all(fontWork);
   const fonts=await page.evaluate(()=>({status:document.fonts.status,faces:Array.from(document.fonts).map(f=>({family:f.family,status:f.status,weight:f.weight,style:f.style}))}));
   const focusRatio=contrast(geometry.outline.color,geometry.outline.adjacent_background);
   const focusPass=geometry.ring_fully_visible&&geometry.outline.style!=='none'&&parseFloat(geometry.outline.width)>=2&&focusRatio>=3;
   rows.push({name,viewport:{width,height},download_focused_before_enter:downloadFocused,geometry,keyboard_result:downloadFocused&&geometry.focused&&geometry.fully_visible&&!geometry.page_overflow&&!geometry.dialog_overflow_x?'PASS':'FAIL',focus_outline:{contrast_ratio:focusRatio,required:3,result:focusPass?'PASS_SCOPED':'UNRESOLVED'},screenshot:ref(picture),axe:{...ref(axePath),violations:axe.violations.map(v=>({id:v.id,impact:v.impact,targets:v.nodes.map(n=>n.target)})),incomplete:axe.incomplete.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})),passes:axe.passes.length},manual_exact_targets:manual,fonts,font_responses:fontReplies,owner_http_requests:ownerCalls,download_events:downloadEvents,fixture_download_callback_count:await page.locator('[data-fixture-count="downloads"]').textContent()});
   console.log(JSON.stringify({viewport:name,keyboard:rows.at(-1).keyboard_result,focus:rows.at(-1).focus_outline.result,violations:axe.violations.length,incomplete_targets:manual.length,unresolved:manual.filter(n=>n.result==='UNRESOLVED').length}));
  }finally{await context.close();}
 }
}catch(e){error=String(e);}
finally{
 if(browser){await browser.close();browserClosed=true;}
 if(!closed)server.kill('SIGTERM');
 await Promise.race([terminal,new Promise(r=>setTimeout(r,5000))]);
}
const after=original.files.map(f=>({...f,sha256:hash(read(f.path))})),equal=head()===carrier&&after.every((f,i)=>f.sha256===original.files[i].sha256);
write(prefix+'/source-inputs-after.json',{invocation_sha:head(),input_count:after.length,all_declared_inputs_equal:equal,files:after});
const result={scope_id:'TUN-P2-009',defect_id:'DEF-TUN-P2-009-UI-001',invocation_sha:carrier,recorded_at:new Date().toISOString(),result:error?'CAPTURE_ERROR':'OBSERVATIONS_COMPLETE',error,source_before:ref(prefix+'/source-inputs-before.json'),source_after:ref(prefix+'/source-inputs-after.json'),all_declared_inputs_equal:equal,helper:ref(base+'/capture-current-mobile.mjs'),rows,totals:{captures:rows.length,keyboard_pass:rows.filter(r=>r.keyboard_result==='PASS').length,keyboard_fail:rows.filter(r=>r.keyboard_result!=='PASS').length,axe_violations:rows.reduce((n,r)=>n+r.axe.violations.length,0),raw_incomplete_targets:rows.reduce((n,r)=>n+r.manual_exact_targets.length,0),unresolved_targets:rows.reduce((n,r)=>n+r.manual_exact_targets.filter(t=>t.result==='UNRESOLVED').length,0)},closure:{owned_vite_pid:server.pid,started_at:serverStarted,server_closed:closed,browser_closed:browserClosed,unknown_process_stopped:false},scope:'Public controlled actual React modal only,39-character nonfunctional samples; same-source52 includes integrated controller mocks. No actual file, native save dialog, private payload, native owner, whole UI acceptance or final role acceptance.',defect_disposition:'PROPOSED_SCOPED_RESOLUTION_ONLY; root reviews and materializes governed status'};
write(prefix+'/observations.json',result);
console.log(JSON.stringify({index:ref(prefix+'/observations.json'),totals:result.totals,all_inputs_equal:equal,closure:result.closure}));
if(error||!equal||!closed||!browserClosed||rows.length!==6||result.totals.keyboard_fail||result.totals.axe_violations||result.totals.unresolved_targets)process.exitCode=1;
