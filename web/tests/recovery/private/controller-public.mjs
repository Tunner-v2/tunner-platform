import {spawn,execFileSync} from 'node:child_process';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
import {randomUUID} from 'node:crypto';
import {fixedError,requireTrue,budgets,bounded} from './contract.mjs';
import {stopOwnedChild} from '../../totp/sensitive/supervisor.mjs';

// Only the literal public fault imports this fixture. Real paths never mock HTTP.
export async function runControllerPublic({page,requestOwnedDownload,signal,onOwnedServer,onServerExit,remember}){
 const root=execFileSync('git',['rev-parse','--show-toplevel'],{encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();
 const origin='http://127.0.0.1:5289',entry=origin+'/browser-testing/recovery-private-assets/';
 let occupied=false;try{await fetch(origin,{signal:AbortSignal.timeout(500)});occupied=true;}catch{}requireTrue(!occupied,'DEPENDENCY_NOT_READY');
 const published=copy=>Object.fromEntries(Object.entries(copy).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'PUBLIC-ONLY',versionRef:'PUBLIC-V1',publicationRef:'NOT-PRODUCTION'}]));
 const recovery=JSON.parse(await readFile(join(root,'web/apps/user/recovery-codes.content.proposal.json'),'utf8'));
 const step=JSON.parse(await readFile(join(root,'web/apps/user/step-up.content.proposal.json'),'utf8'));
 const ownerRequestId=randomUUID(),proofId=randomUUID(),codes=['A'.repeat(32),'B'.repeat(32)];remember(...codes);let op=null,downloadCount=0;
 page.on('download',()=>downloadCount++);
 const server=spawn(process.execPath,['node_modules/vite/bin/vite.js','--config','fixtures/recovery-private/vite.config.ts'],{cwd:join(root,'web'),env:process.env,windowsHide:true,detached:process.platform!=='win32',stdio:['ignore','pipe','pipe']});
 let closed=false,spawnFailed=false,bytes=0;const terminal=new Promise(r=>server.once('close',()=>{closed=true;onServerExit();r();}));
 server.once('error',()=>{spawnFailed=true;});
 for(const pipe of [server.stdout,server.stderr])pipe.on('data',chunk=>{bytes+=chunk.length;if(bytes>65536)server.kill();});
 try{
  requireTrue(Number.isSafeInteger(server.pid)&&server.pid>0,'DEPENDENCY_NOT_READY');onOwnedServer(server.pid);
  // Public observation only: call the real native functions and retain no URL in receipts.
  await page.addInitScript(()=>{const create=URL.createObjectURL.bind(URL),revoke=URL.revokeObjectURL.bind(URL),active=new Set();const facts={created:0,revoked:0};Object.defineProperty(window,'recoveryPublicUrlFacts',{value:facts});URL.createObjectURL=blob=>{const url=create(blob);facts.created++;active.add(url);return url;};URL.revokeObjectURL=url=>{if(active.delete(url))facts.revoked++;revoke(url);};});
  let ready=false;for(let i=0;i<100&&!ready;i++){if(signal?.aborted)throw fixedError('SUPERVISOR_TIMEOUT');if(closed||spawnFailed)throw fixedError('DEPENDENCY_NOT_READY');try{ready=(await fetch(entry,{signal:AbortSignal.timeout(500)})).ok&&!closed&&!spawnFailed&&server.exitCode===null;}catch{}if(!ready)await new Promise(r=>setTimeout(r,100));}requireTrue(ready&&!closed&&!spawnFailed,'DEPENDENCY_NOT_READY');
  await page.route('**/account/recovery-codes/**',route=>{
   const pathname=new URL(route.request().url()).pathname;
   if(pathname.endsWith('/options'))return route.fulfill({json:{csrfToken:'PUBLIC-CSRF',ownerRequestId,expectedVersion:1,policyVersion:'PUBLIC-POLICY',formatProfileVersion:'NATIVE_HEX32_V1',deliveryProfileVersion:'PUBLIC-DELIVERY',codesPerSet:2,maximumCodesPerSet:2,canGenerate:!op,canReplace:false,activeSetCount:op?.status==='COMPLETED'?1:0,remainingCodeCount:op?.status==='COMPLETED'?2:0,locale:'en',content:published({...recovery.content,'public.impact':'PUBLIC NONFUNCTIONAL IMPACT'}),recovery:op?{...op,impact:{...op.impact,canContinue:op.status==='PENDING_PROOF'}}:null}});
   if(pathname.endsWith('/generate/start')){op={status:'PENDING_PROOF',ownerRequestId,operationReference:randomUUID().replaceAll('-',''),setReference:randomUUID(),ownerContextDigest:'A'.repeat(64),deliveryProfileVersion:'PUBLIC-DELIVERY',formatProfileVersion:'NATIVE_HEX32_V1',codesPerSet:2,maximumCodesPerSet:2,expiresAt:'2030-01-01T00:00:00Z',expectedVersion:1,kind:'GENERATE',impact:{impactKey:'public.impact',factsDigest:'B'.repeat(64),publicationVersion:'PUBLIC-V1',publicationDigest:'C'.repeat(64),publicationValidity:{from:'2026-01-01T00:00:00Z',includesFrom:true,until:null,includesUntil:false},canContinue:false,priorSetCount:0,remainingCodeCount:0,sessionEffect:'NO_SESSION_MUTATION',replacementMechanism:'ATOMIC_RETIRE_EXACT_EXPECTED_SETS_AT_ISSUE_COMMIT_V1'},codesUnavailable:false};return route.fulfill({json:op});}
   return route.fulfill({json:op});
  });
  await page.route('**/authentication/step-up/**',route=>{
   if(new URL(route.request().url()).pathname.endsWith('/options'))return route.fulfill({json:op?.status==='COMPLETED'?{status:'COMPLETED',requestId:proofId,operationReference:op.operationReference}:{status:'PENDING',requestId:proofId,operationReference:op.operationReference,csrfToken:'PUBLIC-PROOF',passwordMaximumLength:128,passwordAvailable:true,passkeyAvailable:false,totpAvailable:false,locale:'en',content:published(step.content)}});
   requireTrue(new URL(route.request().url()).pathname==='/authentication/step-up/password');
   const delivery={kind:'RECOVERY_CODES_FIRST_DELIVERY_V1',ownerRequestId,stepUpRequestId:proofId,operationReference:op.operationReference,setReference:op.setReference,ownerContextDigest:op.ownerContextDigest,deliveryProfileVersion:op.deliveryProfileVersion,formatProfileVersion:op.formatProfileVersion,codes};
   op={...op,status:'COMPLETED',codesUnavailable:true,impact:{...op.impact,canContinue:false}};return route.fulfill({json:{status:'COMPLETED',signInRequired:false,operationReference:op.operationReference,recoveryCodeFirstDelivery:delivery}});
  });
  await page.goto(entry);await page.getByRole('heading',{name:'Public production-controller rehearsal',exact:true}).waitFor();requireTrue(!closed&&!spawnFailed);await page.getByRole('button',{name:'Generate codes',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true}).click();await page.getByLabel('Password',{exact:true}).fill('PUBLIC NONFUNCTIONAL PASSWORD');await page.getByRole('button',{name:'Confirm',exact:true}).click();
  await page.locator('.recovery-codes-grid code').first().waitFor();requireTrue(JSON.stringify(await page.locator('.recovery-codes-grid code').allTextContents())===JSON.stringify(codes));
  const expected=Buffer.from(codes.join('\n')+'\n');try{await requestOwnedDownload(page,expected);}finally{expected.fill(0);}
  requireTrue(downloadCount===1&&await page.getByRole('button',{name:'Download',exact:true}).isDisabled());
  await page.getByRole('button',{name:'Download',exact:true}).dispatchEvent('click');await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));requireTrue(downloadCount===1);
  const status=await page.getByRole('status').textContent();requireTrue(status.includes('Download requested')&&status.includes('cannot confirm that the file was saved')&&!codes.some(c=>status.includes(c)));
  await page.evaluate(()=>dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true})));requireTrue(await page.locator('.recovery-codes-grid code').count()===0);
  requireTrue(await page.evaluate(()=>window.recoveryPublicUrlFacts.created===1&&window.recoveryPublicUrlFacts.revoked===1));
  await page.evaluate(()=>dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true})));await page.reload();requireTrue(await page.locator('.recovery-codes-grid code').count()===0);
  return true;
 }finally{
  await stopOwnedChild(server);await bounded(terminal,budgets.close,'CLEANUP_FAILED');requireTrue(closed,'CLEANUP_FAILED');
 }
}
