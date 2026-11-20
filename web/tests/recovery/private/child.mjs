import {createRequire} from 'node:module';
import {dirname, join, resolve} from 'node:path';
import {lstat, readdir, writeFile} from 'node:fs/promises';
import {createAdmission} from '../sensitive/contract.mjs';
import {createNativeOracle, fixedOracleAssembly} from '../../totp/sensitive/oracle-bridge.mjs';
import {version, budgets, passkeySetupStages, publicMarker, publicBody, fixedError, failureOf, requireTrue, bounded, exactKeys, validInitialize, oracleFaults, childAccepted, childChecks, validFixtureRequest, validFixtureResult, publicFixtureResult, recordPublicDownloadChecks, publicCancelFaults, publicStageFaults, recordPublicStageCheck, publicExtraFaults, requireTwoDownloadRejection} from './contract.mjs';
import {ownedSurface, privateMarkers, downloadIdentity, inspectSurface} from './surface.mjs';
import {publicEntry, publicJourneyEntry, inspectRequestedDownload, runJourney} from './journeys.mjs';
import {runControllerPublic} from './controller-public.mjs';
import {createRecoveryAccountMechanics} from '../../totp/sensitive/real-journey.mjs';

const abort = new AbortController(), checks = Object.create(null), admission = createAdmission(), downloads = [], markers = privateMarkers();
let control, root, browser, context, page, oracle, browserVersion = null, stage = 'START', failedStage, failureCode = 'NONE';
let fatal = false, publicFaultObserved = false, contextClosed = false, browserClosed = false, serverExited=true, oracleExited = true, oracleGraceful = true, surfaceClean = false, cleanupCompleted = false;
const send = value => bounded(new Promise(resolveSend => { try { if (process.connected) process.send(value, () => resolveSend()); else resolveSend(); } catch { resolveSend(); } }), budgets.ipc).catch(() => {});
const cancel = () => { if (abort.signal.aborted) throw fixedError('SUPERVISOR_TIMEOUT'); };
const onFatal = () => { fatal = true; abort.abort(); };
process.on('uncaughtException', onFatal); process.on('unhandledRejection', onFatal);
let fixtureSequence=0,fixturePending;
const fixtureControl=async action=>{cancel();if(fixturePending)throw fixedError('PROTOCOL_FAILED');const request={version,type:'recoveryFixtureRequest',requestId:++fixtureSequence,action};requireTrue(validFixtureRequest(request),'PROTOCOL_FAILED');try{return await bounded(new Promise((resolve,reject)=>{fixturePending={requestId:request.requestId,resolve,reject};void send(request);}),budgets.relay,'PROTOCOL_FAILED');}finally{fixturePending=null;}};
const initial = new Promise((resolveInitial, reject) => {
  const timer = setTimeout(() => reject(fixedError('INPUT_INVALID')), budgets.initialize);
  process.on('message', value => {
    if (exactKeys(value, ['version', 'type']) && value.version === version && value.type === 'cancelRecoveryPrivate') { abort.abort(); fixturePending?.reject(fixedError('SUPERVISOR_TIMEOUT')); return; }
    if(validFixtureResult(value)&&fixturePending&&value.requestId===fixturePending.requestId){fixturePending.resolve(value);return;}
    if (!control && validInitialize(value)) { control = value.control; clearTimeout(timer); resolveInitial(value.surface); return; }
    onFatal();
  });
});
const downloadAdmissions=[],publicSimDeletedDownloads=new Set();let pendingDownload,downloadActionTask,downloadWaitSettled=true,downloadActionSettled=true;const publicTransferFacts={};
function wire(owner) {
  owner.on('download', download => {
    downloads.push(download); const accepted = admission.accept(owner);downloadAdmissions.push(accepted);
    pendingDownload?.({download, accepted}); pendingDownload = null;
    if (!accepted && control?.mode !== 'public') { failureCode = 'ADMISSION_REJECTED'; abort.abort(); }
  });
}
async function waitForDownload(action){
 cancel();requireTrue(!pendingDownload,'PROTOCOL_FAILED');downloadWaitSettled=false;downloadActionSettled=false;
 let rejectEvent;const event=new Promise((resolve,reject)=>{pendingDownload=resolve;rejectEvent=reject;});event.catch(()=>{});
 const interrupted=()=>rejectEvent(fixedError('SUPERVISOR_TIMEOUT'));abort.signal.addEventListener('abort',interrupted,{once:true});
 downloadActionTask=Promise.resolve().then(action).finally(()=>{downloadActionSettled=true;});downloadActionTask.catch(()=>{});
 try{await bounded(downloadActionTask,budgets.action,'ACTION_FAILED');const value=await bounded(event,budgets.action,'ACTION_FAILED');cancel();return value;}
 finally{abort.signal.removeEventListener('abort',interrupted);pendingDownload=null;downloadWaitSettled=true;}
}
async function request(owner){
 const value=await waitForDownload(()=>owner.locator('#download').click({timeout:budgets.action}));
 requireTrue(value.accepted,'ADMISSION_REJECTED');return value.download;
}
async function rejectExtraDownload(owner){
 try{await request(owner);throw fixedError('PREDICATE_FAILED');}
 catch(error){requireTrue(failureOf(error)==='ADMISSION_REJECTED','PREDICATE_FAILED');requireTwoDownloadRejection(downloads.length,downloadAdmissions);throw fixedError('ADMISSION_REJECTED');}
}
async function requestOwnedDownload(owner,expected){
 cancel();admission.arm(owner);const count=downloads.length;
 const value=await waitForDownload(()=>owner.getByRole('button',{name:'Download',exact:true}).click({timeout:budgets.action}));
 const capability=value.download.url(),expectedOrigin=new URL(owner.url()).origin;
 requireTrue(value.accepted&&value.download.suggestedFilename()==='tunner-backup-codes.txt'&&capability.startsWith('blob:'+expectedOrigin+'/')&&/^[a-f0-9-]{36}$/.test(capability.slice(('blob:'+expectedOrigin+'/').length)),'ADMISSION_REJECTED');
 await inspectRequestedDownload(value.download,root,expected,{signal:abort.signal});admission.settle();
 await owner.getByRole('button',{name:'Download',exact:true}).dispatchEvent('click');await owner.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));requireTrue(downloads.length===count+1,'ADMISSION_REJECTED');
 recordPublicDownloadChecks(control.mode,checks);
}

const nativeCode=async(seed,offset=0)=>{if(!oracle){oracleExited=false;oracleGraceful=false;oracle=await createNativeOracle({oracleAssemblyPath:fixedOracleAssembly,oracleNativeVersion:'10.0.13',publicFault:null},root.path,abort.signal);}return oracle.nativeCode(seed,offset);};
try {
  const surface = await initial; stage = 'ENVIRONMENT';
  requireTrue(process.argv.length === 3 && process.argv[2] === '--recovery-private-child-v1', 'INPUT_INVALID');
  root = await ownedSurface(surface); cancel(); markers.remember(publicMarker);
  stage = 'LIBRARY'; const require = createRequire(import.meta.url), from = dirname(require.resolve('@playwright/test/package.json'));
  const packagePath = require.resolve('playwright/package.json', {paths: [from]}); requireTrue(require(packagePath).version === '1.62.1');
  const {chromium} = require(require.resolve('playwright', {paths: [from]}));
  const core = require.resolve('playwright-core/package.json', {paths: [dirname(packagePath)]});
  stage = 'BROWSER'; browser = await chromium.launch({headless: true, timeout: budgets.launch, downloadsPath: join(root.path, 'downloads'), tracesDir: join(root.path, 'traces')});
  browserVersion = browser.version(); checks.BROWSER_ENGINE = browserVersion === require(join(dirname(core), 'browsers.json')).browsers.find(value => value.name === 'chromium-headless-shell')?.browserVersion; requireTrue(checks.BROWSER_ENGINE);
  context = await browser.newContext({acceptDownloads: true, serviceWorkers: 'block'}); context.on('page', wire);
  page = await context.newPage(); page.setDefaultTimeout(budgets.action);
  if (control.mode === 'live') {
    stage = 'JOURNEY'; await runJourney({control, page, context, browser, checkpoint(value){requireTrue(passkeySetupStages.includes(value)||value==='JOURNEY','PROTOCOL_FAILED');stage=value;}, signal: abort.signal, markers, fixtureControl, nativeCode, onContext:ownedContext=>ownedContext.on('page',wire), requestOwnedDownload, inspectOwnedSurface:async()=> (await inspectSurface(root,{markers})).clean, check(id, flag) { if (!childChecks.includes(id) || Object.hasOwn(checks, id) || typeof flag !== 'boolean') throw fixedError('PREDICATE_FAILED'); checks[id] = flag; if (!flag) throw fixedError('PREDICATE_FAILED'); }});
  } else {
    stage = 'PUBLIC_ENTRY'; const fault = control.publicFault;
    const body = fault === 'wrong-bytes' ? 'PUBLIC WRONG NONFUNCTIONAL FIXTURE' : fault === 'oversize' ? publicBody.repeat(Math.ceil((budgets.maximumDownloadBytes + 1) / publicBody.length)) : publicBody;
    await publicEntry(page, body); checks.PUBLIC_ENTRY = true;
    if(fault==='production-controller'){checks.PRODUCTION_CONTROLLER_PUBLIC=await runControllerPublic({page,signal:abort.signal,requestOwnedDownload,remember:(...values)=>markers.remember(...values),onOwnedServer:processId=>{serverExited=false;void send({version,type:'publicProcess',kind:'PUBLIC_SERVER',processId});},onServerExit:()=>{serverExited=true;}});requireTrue(checks.PRODUCTION_CONTROLLER_PUBLIC);publicFaultObserved=true;}
    else if(fault.startsWith('relay-')){const reply=await fixtureControl('READ_SAFE_COUNTS');requireTrue(publicFixtureResult(reply));checks.FIXTURE_RELAY_PUBLIC=true;publicFaultObserved=true;}
    else if (oracleFaults.includes(fault)) {
      stage = 'ORACLE'; oracleExited = false; oracleGraceful = false;
      oracle = await createNativeOracle({oracleAssemblyPath: fixedOracleAssembly, oracleNativeVersion: '10.0.13', publicFault: fault}, root.path, abort.signal);
      if (fault === 'oracle-roundtrip') { const code = await oracle.nativeCode('JBSWY3DPEHPK3PXP'); publicFaultObserved = /^\d{6}$/.test(code); }
      else publicFaultObserved = await oracle.publicFault(fault.slice(7));
      requireTrue(publicFaultObserved, 'ORACLE_FAILED');
    } else if (fault === 'journey-entry') { await publicJourneyEntry(page); checks.ERROR_CONTAINED = true; publicFaultObserved = true; }
    else if (fault === 'missing-predicate' || fault === 'cross-component') publicFaultObserved = true;
    else if (fault === 'action-timeout') { try { await page.locator('#public-does-not-exist').click({timeout: 200}); } catch { publicFaultObserved = true; throw fixedError('ACTION_FAILED'); } }
    else if (fault === 'fatal-rejection') { void Promise.reject(new Error(publicMarker)); await new Promise(resolveTurn => setTimeout(resolveTurn, 20)); publicFaultObserved = fatal; cancel(); }
    else if(fault==='cancel-initiation'){
      admission.arm(page);await page.locator('#download').evaluate(button=>{const copy=button.cloneNode(true);button.replaceWith(copy);copy.addEventListener('click',()=>{copy.dataset.initiated='true';});});
      const waiting=request(page);waiting.catch(()=>{});await page.waitForFunction(()=>document.querySelector('#download').dataset.initiated==='true');
      await bounded(downloadActionTask,budgets.action,'ACTION_FAILED');publicTransferFacts.initiated=true;
      await send({version,type:'recoveryPrivateArmed',publicFault:fault});await waiting;
    }
    else if (fault === 'child-timeout') { await send({version, type: 'recoveryPrivateArmed', publicFault: fault}); await new Promise(resolveAbort => { if (abort.signal.aborted) resolveAbort(); else abort.signal.addEventListener('abort', resolveAbort, {once: true}); }); publicFaultObserved = true; cancel(); }
    else {
      if (fault !== 'unarmed') admission.arm(page);
      let owner = page; if (fault === 'wrong-page') { owner = await context.newPage(); await publicEntry(owner); }
      let simultaneous;
      if(fault==='simultaneous-extra'){
       await page.evaluate(body=>document.querySelector('#download').addEventListener('click',()=>{const url=URL.createObjectURL(new Blob([body],{type:'text/plain'})),anchor=document.createElement('a');anchor.href=url;anchor.download='public-extra.txt';anchor.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}),publicBody);
       simultaneous=page.waitForEvent('download',{predicate:()=>downloads.length>=2,timeout:budgets.action});simultaneous.catch(()=>{});
      }
      const download = await request(owner);
      if(publicExtraFaults.includes(fault))checks.DOWNLOAD_ADMITTED=true;
      if(simultaneous){
       await simultaneous;requireTwoDownloadRejection(downloads.length,downloadAdmissions);
       // Only the two public canary transfers finish native deletion before cancel fallback.
       // Download.delete() waits for completion; any failure keeps the existing finally cleanup.
       await Promise.all(downloads.map(item=>bounded(item.delete(),budgets.close).then(()=>publicSimDeletedDownloads.add(item)).catch(()=>{})));
       throw fixedError('ADMISSION_REJECTED');
      }
      if(publicStageFaults.includes(fault))checks.DOWNLOAD_ADMITTED=true;
      if (fault === 'outside-identity') { await downloadIdentity(root, resolve(root.path, '..', 'not-an-owned-download')); throw fixedError(); }
      const expected = Buffer.from(publicBody);
      try {
        const identity = await inspectRequestedDownload(download, root, expected, {signal:abort.signal,fault,publicFacts:publicTransferFacts,onPublicStage:phase=>{if((fault==='cancel-stream-read'&&phase==='PARTIAL_READ')||(fault==='cancel-delete'&&phase==='DELETE_STARTED'))void send({version,type:'recoveryPrivateArmed',publicFault:fault});}});
        try { await lstat(identity.path); throw fixedError('DELETE_FAILED'); } catch (error) { if (error?.code !== 'ENOENT') throw error; }
        admission.settle(); checks.DOWNLOAD_ADMITTED = true; checks.DOWNLOAD_REMOVED = true;
      } finally { expected.fill(0); }
      if (fault === 'duplicate') await request(page);
      if(fault==='popup-extra'){
       await page.evaluate(()=>{const button=document.createElement('button');button.id='open-public-popup';button.textContent='Open public popup';button.onclick=()=>window.open('about:blank');document.body.append(button);});
       const pending=page.waitForEvent('popup',{timeout:budgets.action});pending.catch(()=>{});await page.locator('#open-public-popup').click();const popup=await pending;await publicEntry(popup);await rejectExtraDownload(popup);
      }
      if(fault==='secondary-context-extra'){
       // Constructor/newPage only: no bootstrap, BFF, fixture account or oracle call.
       // This is the same required recovery onContext capability used by live preparation.
       const mechanics=createRecoveryAccountMechanics({page,context,browser,origin:'https://localhost:24443',mailpitOrigin:'http://127.0.0.1:28025',runId:control.runId,journeyId:control.journeyId,signal:abort.signal,remember:()=>{},nativeCode:async()=>{throw fixedError('DEPENDENCY_NOT_READY');},fixtureControl:async()=>{throw fixedError('DEPENDENCY_NOT_READY');},onContext:ownedContext=>ownedContext.on('page',wire)});
       try{const secondary=await mechanics.newPage();await publicEntry(secondary);await rejectExtraDownload(secondary);}finally{await bounded(mechanics.close(),budgets.close,'CLEANUP_FAILED');}
      }
      if(fault==='iframe-extra'){
       await page.evaluate(()=>{const frame=document.createElement('iframe');frame.id='public-extra-frame';frame.src='about:blank';document.body.append(frame);});
       const handle=await page.locator('#public-extra-frame').elementHandle(),frame=await handle.contentFrame();requireTrue(!!frame);try{await publicEntry(frame);await rejectExtraDownload(frame);}finally{await handle.dispose();}
      }

      if (fault === 'unexpected-artifact') await writeFile(join(root.path, 'unexpected-public-artifact.txt'), publicMarker, {flag: 'wx'});
      if (fault === 'unexpected-output' || fault === 'invalid-result' || fault === 'cleanup') publicFaultObserved = true;
    }
  }
} catch (error) {
  failureCode = failureOf(error); failedStage = stage;
  const expected = {unarmed: 'ADMISSION_REJECTED', duplicate: 'ADMISSION_REJECTED', 'wrong-page': 'ADMISSION_REJECTED', 'wrong-bytes': 'BYTES_MISMATCH', oversize: 'SIZE_LIMIT', 'outside-identity': 'PATH_REJECTED', 'stream-error': 'STREAM_FAILED', 'delete-error': 'DELETE_FAILED', 'action-timeout': 'ACTION_FAILED', 'child-timeout': 'SUPERVISOR_TIMEOUT', 'fatal-rejection': 'SUPERVISOR_TIMEOUT', 'popup-extra':'ADMISSION_REJECTED','iframe-extra':'ADMISSION_REJECTED','simultaneous-extra':'ADMISSION_REJECTED','secondary-context-extra':'ADMISSION_REJECTED','cancel-initiation':'SUPERVISOR_TIMEOUT','stalled-partial-read':'STREAM_FAILED','cancel-stream-read':'SUPERVISOR_TIMEOUT','stalled-delete':'DELETE_FAILED','cancel-delete':'SUPERVISOR_TIMEOUT'};
  if (control?.mode === 'public' && expected[control.publicFault] === failureCode) publicFaultObserved = true;
} finally {
  stage = 'CLEANUP'; admission.close(); pendingDownload = null;
  for (const download of downloads) {
    if (control?.mode === 'public' && control.publicFault === 'simultaneous-extra' && publicSimDeletedDownloads.has(download)) continue;
    try { await bounded(download.cancel(), budgets.close); } catch {}
    try { await bounded(download.delete(), budgets.close); } catch {}
  }
  try { if (context) { await bounded(context.close(), budgets.close); contextClosed = true; } } catch {}
  try { if (browser) { await bounded(browser.close(), budgets.close); browserClosed = true; } } catch {}
  try { if (oracle) { const result = await oracle.close(); oracleExited = result.exited; oracleGraceful = result.graceful; } } catch { oracleExited = false; oracleGraceful = false; }
  let downloadsRemoved = downloads.length === 0;
  if (root && downloads.length) { try { downloadsRemoved = (await readdir(join(root.path, 'downloads'))).length === 0; } catch (error) { downloadsRemoved = error?.code === 'ENOENT'; } }
  try { if (root) { surfaceClean = (await inspectSurface(root, {markers})).clean; checks.SURFACE_CHECKED = true; } } catch {}
  if(control?.mode==='public'&&publicExtraFaults.includes(control.publicFault))checks.DOWNLOAD_REMOVED=downloadsRemoved;
  if(control?.mode==='public'&&publicStageFaults.includes(control.publicFault)){
   const fault=control.publicFault,settled=fault==='cancel-initiation'?publicTransferFacts.initiated===true&&downloadWaitSettled&&downloadActionSettled:fault.includes('read')?publicTransferFacts.partialRead===true&&publicTransferFacts.readSettled===true:publicTransferFacts.deleteStarted===true&&publicTransferFacts.deleteSettled===true&&publicTransferFacts.deleteWaitSettled===true;
   recordPublicStageCheck(control.mode,checks,settled);if(downloads.length)checks.DOWNLOAD_REMOVED=downloadsRemoved;
  }
  cleanupCompleted = contextClosed && browserClosed && serverExited && oracleExited && downloadsRemoved;
  if (control?.publicFault === 'unexpected-artifact' && !surfaceClean) { publicFaultObserved = true; failureCode = 'SURFACE_FAILED'; }
  if (control?.publicFault === 'cleanup') { cleanupCompleted = false; failureCode = 'CLEANUP_FAILED'; }
  if (!surfaceClean && failureCode === 'NONE') failureCode = 'SURFACE_FAILED';
  if (!cleanupCompleted && failureCode === 'NONE') failureCode = 'CLEANUP_FAILED';
  markers.clear();
}
const result = {version, type: 'recoveryPrivateChildResult', journeyId: control?.journeyId ?? 'RECOVERY_GENERATE_PASSWORD_V1', mode: control?.mode ?? 'live', publicFault: control?.publicFault ?? null, browserVersion, status: failureCode === 'NONE' && !fatal ? control?.mode === 'public' ? 'PUBLIC_BOUNDARY_OK' : 'JOURNEY_OK' : 'BOUNDARY_FAILED', stage: failedStage ?? (failureCode === 'NONE' ? 'COMPLETE' : 'CLEANUP'), failureCode, checks, publicFaultObserved, contextClosed, browserClosed, serverExited, oracleExited, oracleGraceful, surfaceClean, cleanupCompleted};
if (control?.publicFault === 'missing-predicate') delete checks.PUBLIC_ENTRY;
if (control?.publicFault === 'cross-component') checks.SOURCE_BUILD_CURRENT = true;
if (contextClosed && browserClosed && serverExited && oracleExited) await send({version, type: 'recoveryPrivateCleanup', journeyId: result.journeyId, contextClosed: true, browserClosed: true, serverExited:true, oracleExited: true});
if (control?.publicFault === 'unexpected-output') process.stderr.write(publicMarker);
await send(control?.publicFault === 'invalid-result' ? {...result, unexpected: publicMarker} : result);
const accepted = control && childAccepted(result, control); process.exitCode = accepted || control?.mode === 'public' && failureCode === 'NONE' ? 0 : 1;
if (process.connected) process.disconnect();
