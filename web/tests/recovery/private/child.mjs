import {createRequire} from 'node:module';
import {dirname, join, resolve} from 'node:path';
import {lstat, readdir, writeFile} from 'node:fs/promises';
import {createAdmission} from '../sensitive/contract.mjs';
import {createNativeOracle, fixedOracleAssembly} from '../../totp/sensitive/oracle-bridge.mjs';
import {version, budgets, publicMarker, publicBody, fixedError, failureOf, requireTrue, bounded, exactKeys, validInitialize, oracleFaults, childAccepted, childChecks, validFixtureRequest, validFixtureResult, publicFixtureResult, recordPublicDownloadChecks} from './contract.mjs';
import {ownedSurface, privateMarkers, downloadIdentity, inspectSurface} from './surface.mjs';
import {publicEntry, publicJourneyEntry, inspectRequestedDownload, runJourney} from './journeys.mjs';
import {runControllerPublic} from './controller-public.mjs';

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
let pendingDownload;
function wire(owner) {
  owner.on('download', download => {
    downloads.push(download); const accepted = admission.accept(owner);
    pendingDownload?.({download, accepted}); pendingDownload = null;
    if (!accepted && control?.mode !== 'public') { failureCode = 'ADMISSION_REJECTED'; abort.abort(); }
  });
}
async function request(owner) {
  cancel(); const event = new Promise(resolveEvent => { pendingDownload = resolveEvent; });
  await owner.locator('#download').click({timeout: budgets.action});
  const value = await bounded(event, budgets.action, 'ACTION_FAILED'); cancel();
  requireTrue(value.accepted, 'ADMISSION_REJECTED'); return value.download;
}
async function requestOwnedDownload(owner,expected){
 cancel();admission.arm(owner);const count=downloads.length,event=new Promise(resolve=>{pendingDownload=resolve;});
 await owner.getByRole('button',{name:'Download',exact:true}).click({timeout:budgets.action});const value=await bounded(event,budgets.action,'ACTION_FAILED');
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
    stage = 'JOURNEY'; await runJourney({control, page, context, browser, signal: abort.signal, markers, fixtureControl, nativeCode, requestOwnedDownload, inspectOwnedSurface:async()=> (await inspectSurface(root,{markers})).clean, check(id, flag) { if (!childChecks.includes(id) || Object.hasOwn(checks, id) || typeof flag !== 'boolean') throw fixedError('PREDICATE_FAILED'); checks[id] = flag; if (!flag) throw fixedError('PREDICATE_FAILED'); }});
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
    else if (fault === 'child-timeout') { await send({version, type: 'recoveryPrivateArmed', publicFault: fault}); await new Promise(resolveAbort => { if (abort.signal.aborted) resolveAbort(); else abort.signal.addEventListener('abort', resolveAbort, {once: true}); }); publicFaultObserved = true; cancel(); }
    else {
      if (fault !== 'unarmed') admission.arm(page);
      let owner = page; if (fault === 'wrong-page') { owner = await context.newPage(); await publicEntry(owner); }
      const download = await request(owner);
      if (fault === 'outside-identity') { await downloadIdentity(root, resolve(root.path, '..', 'not-an-owned-download')); throw fixedError(); }
      const expected = Buffer.from(publicBody);
      try {
        const identity = await inspectRequestedDownload(download, root, expected, {signal: abort.signal, fault});
        try { await lstat(identity.path); throw fixedError('DELETE_FAILED'); } catch (error) { if (error?.code !== 'ENOENT') throw error; }
        admission.settle(); checks.DOWNLOAD_ADMITTED = true; checks.DOWNLOAD_REMOVED = true;
      } finally { expected.fill(0); }
      if (fault === 'duplicate') await request(page);
      if (fault === 'unexpected-artifact') await writeFile(join(root.path, 'unexpected-public-artifact.txt'), publicMarker, {flag: 'wx'});
      if (fault === 'unexpected-output' || fault === 'invalid-result' || fault === 'cleanup') publicFaultObserved = true;
    }
  }
} catch (error) {
  failureCode = failureOf(error); failedStage = stage;
  const expected = {unarmed: 'ADMISSION_REJECTED', duplicate: 'ADMISSION_REJECTED', 'wrong-page': 'ADMISSION_REJECTED', 'wrong-bytes': 'BYTES_MISMATCH', oversize: 'SIZE_LIMIT', 'outside-identity': 'PATH_REJECTED', 'stream-error': 'STREAM_FAILED', 'delete-error': 'DELETE_FAILED', 'action-timeout': 'ACTION_FAILED', 'child-timeout': 'SUPERVISOR_TIMEOUT', 'fatal-rejection': 'SUPERVISOR_TIMEOUT'};
  if (control?.mode === 'public' && expected[control.publicFault] === failureCode) publicFaultObserved = true;
} finally {
  stage = 'CLEANUP'; admission.close(); pendingDownload = null;
  for (const download of downloads) { try { await bounded(download.cancel(), budgets.close); } catch {} try { await bounded(download.delete(), budgets.close); } catch {} }
  try { if (context) { await bounded(context.close(), budgets.close); contextClosed = true; } } catch {}
  try { if (browser) { await bounded(browser.close(), budgets.close); browserClosed = true; } } catch {}
  try { if (oracle) { const result = await oracle.close(); oracleExited = result.exited; oracleGraceful = result.graceful; } } catch { oracleExited = false; oracleGraceful = false; }
  let downloadsRemoved = downloads.length === 0;
  if (root && downloads.length) { try { downloadsRemoved = (await readdir(join(root.path, 'downloads'))).length === 0; } catch (error) { downloadsRemoved = error?.code === 'ENOENT'; } }
  try { if (root) { surfaceClean = (await inspectSurface(root, {markers})).clean; checks.SURFACE_CHECKED = true; } } catch {}
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
