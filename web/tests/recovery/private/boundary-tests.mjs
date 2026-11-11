import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, realpath, lstat, link, rename, symlink, unlink} from 'node:fs/promises';
import {Readable} from 'node:stream';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {createAdmission} from '../sensitive/contract.mjs';
import {parsePrivateJson} from '../../totp/sensitive/private-frames.mjs';
import {version, journeys, childChecks, supervisorChecks, coordinatorChecks, validControl, validChildResult, childAccepted, realAccepted, fixedError, failureOf, observedMap, publicMarker, publicBody} from './contract.mjs';
import {ownedSurface, removeOwnedSurface, validSurfacePath, downloadIdentity, sameDownload, privateMarkers, inspectSurface} from './surface.mjs';
import {runJourney, inspectRequestedDownload} from './journeys.mjs';

// Public synthetic maps test reducer mechanics. They never enter actual receipts.
const journeyId = 'RECOVERY_GENERATE_PASSWORD_V1';
const publicControl = () => ({version, type: 'runRecoveryPrivate', mode: 'public', journeyId, publicFault: 'happy', sourceCommit: 'a'.repeat(40), sourceCommitted: true, origin: null, mailpitOrigin: null, runId: '0'.repeat(32)});
const liveControl = () => ({...publicControl(), mode: 'live', publicFault: null, origin: 'https://localhost:24443', mailpitOrigin: 'http://127.0.0.1:28025'});
const map = ids => Object.fromEntries(ids.map(id => [id, true]));
const winning = () => ({version, type: 'recoveryPrivateChildResult', journeyId, mode: 'live', publicFault: null, browserVersion: '1.2.3.4', status: 'JOURNEY_OK', stage: 'COMPLETE', failureCode: 'NONE', checks: map(['BROWSER_ENGINE', 'SURFACE_CHECKED', ...journeys[journeyId]]), publicFaultObserved: false, contextClosed: true, browserClosed: true, serverExited:true, oracleExited: true, oracleGraceful: true, surfaceClean: true, cleanupCompleted: true});
async function fixture(body) {
  // Capture exact canonical ownership before assertions or other fallible work.
  const path = await realpath(await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-009-private-')));
  const identity = await ownedSurface(path);
  process.stdout.write(JSON.stringify({type: 'PUBLIC_UNIT_OWNED_ROOT', path, processId: process.pid}) + '\n');
  try { await body(identity); }
  finally { const removed = await removeOwnedSurface(identity); process.stdout.write(JSON.stringify({type: 'PUBLIC_UNIT_CLEANUP', path, removed}) + '\n'); assert.equal(removed, true); }
}
test('only the closed public control admits no origin or operator selector', () => {
  const value = publicControl(); assert.equal(validControl(value), true);
  assert.equal(validControl({...value, module: publicMarker}), false); assert.equal(validControl({...value, origin: 'https://localhost:24443'}), false);
});
test('live origin and source bindings reject query credentials and uncommitted input', () => {
  assert.equal(validControl(liveControl()), true);
  for (const origin of ['https://localhost:24443/?code=PUBLIC', 'https://user@localhost:24443', 'http://localhost:24443', 'https://example.com:24443']) assert.equal(validControl({...liveControl(), origin}), false);
  assert.equal(validControl({...liveControl(), sourceCommitted: false}), false);
});
test('literal registry rejects unknown journey and public fault', () => {
  assert.equal(validControl({...publicControl(), journeyId: publicMarker}), false); assert.equal(validControl({...publicControl(), publicFault: publicMarker}), false);
});
test('valid failure envelope is never a passing child journey', () => {
  const value = {...winning(), status: 'BOUNDARY_FAILED', stage: 'JOURNEY', failureCode: 'PREDICATE_FAILED'};
  assert.equal(validChildResult(value, liveControl()), true); assert.equal(childAccepted(value, liveControl()), false);
});
test('all exact required child facts are necessary even after later true observations', () => {
  const value = winning(); assert.equal(childAccepted(value, liveControl()), true);
  delete value.checks.NO_EARLY_DELIVERY; assert.equal(childAccepted(value, liveControl()), false);
  value.checks.NO_EARLY_DELIVERY = false; assert.equal(childAccepted(value, liveControl()), false);
});
test('actual child surface lifecycle observation is required in the exact admitted live map', () => {
  const value = winning();
  // child.mjs always emits this registered fact after its final owned-surface scan.
  assert.equal(value.checks.SURFACE_CHECKED, true); assert.equal(childAccepted(value, liveControl()), true);
  delete value.checks.SURFACE_CHECKED; assert.equal(validChildResult(value, liveControl()), true); assert.equal(childAccepted(value, liveControl()), false);
  value.checks.SURFACE_CHECKED = false; assert.equal(validChildResult(value, liveControl()), true); assert.equal(childAccepted(value, liveControl()), false);
});
test('unknown or cross-component child checks and private fields reject the envelope', () => {
  assert.equal(validChildResult({...winning(), checks: {...winning().checks, SOURCE_BUILD_CURRENT: true}}, liveControl()), false);
  assert.equal(validChildResult({...winning(), message: publicMarker}, liveControl()), false);
  assert.equal(childChecks.includes('SOURCE_BUILD_CURRENT'), false);
});
test('full acceptance requires each component exact map without echoed extras', () => {
  assert.equal(realAccepted(winning(), liveControl(), map(supervisorChecks), map(coordinatorChecks)), true);
  const coordinator = map(coordinatorChecks); coordinator.OWNED_DATABASE = false; assert.equal(realAccepted(winning(), liveControl(), map(supervisorChecks), coordinator), false);
  assert.equal(realAccepted(winning(), liveControl(), {...map(supervisorChecks), SAFE_OUTBOX: true}, map(coordinatorChecks)), false);
});
test('public expected-fault and uncommitted controls cannot grant real acceptance', () => {
  const value = {...winning(), mode: 'public', publicFault: 'happy', status: 'PUBLIC_BOUNDARY_OK'};
  assert.equal(validChildResult(value, publicControl()), true); assert.equal(childAccepted(value, publicControl()), false);
});
test('raw Error text cannot impersonate a registered safe failure', () => {
  assert.equal(failureOf(new Error('DELETE_FAILED ' + publicMarker)), 'BOUNDARY_ERROR'); assert.equal(failureOf({message: 'DELETE_FAILED'}), 'BOUNDARY_ERROR'); assert.equal(failureOf(fixedError('DELETE_FAILED')), 'DELETE_FAILED');
});
test('all observations flush once despite first false and retain failure', () => {
  const values = observedMap(['FIRST', 'LATER']); values.observe('FIRST', false); values.observe('LATER', true); const seen = [];
  assert.throws(() => values.finish((id, value) => { seen.push([id, value]); if (!value) throw fixedError(); }));
  assert.deepEqual(seen, [['FIRST', false], ['LATER', true]]);
});
test('framing rejects duplicate keys and excess parser nesting without values', () => {
  assert.throws(() => parsePrivateJson(Buffer.from('{"version":1,"version":2}')));
  assert.throws(() => parsePrivateJson(Buffer.from('['.repeat(14) + '0' + ']'.repeat(14))));
});
test('one explicit admission remains page-bound and cannot rearm', () => {
  const owner = {}, other = {}, admission = createAdmission(); admission.arm(owner); assert.equal(admission.accept(other), false); assert.throws(() => admission.arm(owner));
  const one = createAdmission(); one.arm(owner); assert.equal(one.accept(owner), true); one.settle(); assert.equal(one.accept(owner), false); one.close();
});
test('canonical temp resolution accepts both allocation spellings and rejects outside roots', async () => {
  await fixture(async identity => {
    assert.equal(validSurfacePath(identity.path), true); assert.equal(validSurfacePath(join(identity.path, 'child')), false); assert.equal(validSurfacePath(join(await realpath(tmpdir()), 'unowned-public-root')), false);
    // On Windows tmpdir may be 8.3. Resolve before validation, matching actual allocation.
    const raw = join(tmpdir(), identity.path.split(/[\\/]/).at(-1));
    assert.equal((await ownedSurface(await realpath(raw))).path, identity.path);
    assert.equal((await ownedSurface(join(await realpath(tmpdir()), identity.path.split(/[\\/]/).at(-1)))).path, identity.path);
  });
});
test('only one exact regular bounded download identity may be admitted', async () => {
  await fixture(async identity => {
    await mkdir(join(identity.path, 'downloads')); const file = join(identity.path, 'downloads', 'public.txt'); await writeFile(file, publicBody);
    const admitted = await downloadIdentity(identity, file); assert.equal((await inspectSurface(identity, {admitted})).clean, true); assert.equal((await inspectSurface(identity)).clean, false);
    await assert.rejects(downloadIdentity(identity, join(identity.path, 'not-owned.txt')));
  });
});
test('private marker overlap detects public sentinel across an owned profile chunk boundary', async () => {
  await fixture(async identity => {
    const profile = join(identity.path, 'playwright_chromiumdev_profile-Public009'); await mkdir(profile);
    const bytes = Buffer.concat([Buffer.alloc(65530, 32), Buffer.from(publicMarker)]); await writeFile(join(profile, 'public-cache'), bytes); bytes.fill(0);
    const markers = privateMarkers(); markers.remember(publicMarker);
    try { assert.equal((await inspectSurface(identity, {markers})).clean, false); } finally { markers.clear(); }
  });
});
test('guarded registered dispatcher rejects each missing dependency before work or observations', async () => {
  const ids=Object.keys(journeys);assert.equal(ids.length,6);
  for(const id of ids)for(const missing of ['browser','onContext','fixtureControl','nativeCode','requestOwnedDownload','inspectOwnedSurface']){
    let work=0;const seen=[],attempted=()=>{work++;throw fixedError('PREDICATE_FAILED');};
    const input={control:{...liveControl(),journeyId:id},check(key,value){seen.push([key,value]);},browser:{contexts:attempted},onContext:attempted,fixtureControl:attempted,nativeCode:attempted,requestOwnedDownload:attempted,inspectOwnedSurface:attempted};
    Object.defineProperty(input,'page',{enumerable:true,get:attempted});delete input[missing];
    await assert.rejects(runJourney(input),error=>failureOf(error)==='INPUT_INVALID');
    assert.equal(work,0);assert.deepEqual(seen,[]);
  }
});

// Public reducer assertions only; no live coordinator/values/resources.
test('conditional coordinator facts cannot be supplied by the browser or another journey',async()=>{
 const {requiredCoordinatorChecks,validFixtureRequest,validFixtureResult,fixtureCountKeys}=await import('./contract.mjs');
 const counts=Object.fromEntries(fixtureCountKeys.map(k=>[k,0]));
 assert.equal(validFixtureRequest({version:1,type:'recoveryFixtureRequest',requestId:1,action:'READ_SAFE_COUNTS'}),true);
 assert.equal(validFixtureRequest({version:1,type:'recoveryFixtureRequest',requestId:17,action:'READ_SAFE_COUNTS'}),false);
 assert.equal(validFixtureResult({version:1,type:'recoveryFixtureResult',requestId:1,status:'OK',counts}),true);
 assert.equal(validFixtureResult({version:1,type:'recoveryFixtureResult',requestId:1,status:'OK',counts:{...counts,code:'PUBLIC'}}),false);
 assert.equal(requiredCoordinatorChecks('RECOVERY_REPLACE_PASSWORD_V1').includes('EXACT_SUBSET_REPLACEMENT'),true);
 assert.equal(requiredCoordinatorChecks('RECOVERY_GENERATE_PASSWORD_V1').includes('EXACT_SUBSET_REPLACEMENT'),false);
 const id='RECOVERY_REPLACE_PASSWORD_V1',control={...liveControl(),journeyId:id},child={...winning(),journeyId:id,checks:map(['BROWSER_ENGINE','SURFACE_CHECKED',...journeys[id]])};
 const required=map(requiredCoordinatorChecks(id));assert.equal(realAccepted(child,control,map(supervisorChecks),required),true);
 delete required.EXACT_SUBSET_REPLACEMENT;assert.equal(realAccepted(child,control,map(supervisorChecks),required),false);
 required.EXACT_SUBSET_REPLACEMENT=false;assert.equal(realAccepted(child,control,map(supervisorChecks),required),false);
 assert.equal(validChildResult({...child,checks:{...child.checks,EXACT_SUBSET_REPLACEMENT:true}},control),false);
 required.EXACT_SUBSET_REPLACEMENT=true;required.PUBLICATION_DRIFT_ROLLED_BACK=true;assert.equal(realAccepted(child,control,map(supervisorChecks),required),false);
 const tid='RECOVERY_REPLACE_TOTP_V1',tc={...liveControl(),journeyId:tid},tchild={...winning(),journeyId:tid,checks:map(['BROWSER_ENGINE','SURFACE_CHECKED',...journeys[tid]])},tr=map(requiredCoordinatorChecks(tid));assert.equal(realAccepted(tchild,tc,map(supervisorChecks),tr),true);tr.PUBLICATION_DRIFT_ROLLED_BACK=false;assert.equal(realAccepted(tchild,tc,map(supervisorChecks),tr),false);
});
test('public server exit is required independently of browser closure',async()=>{
 const {validCleanup}=await import('./contract.mjs'),value=winning(),control=liveControl();delete value.serverExited;assert.equal(validChildResult(value,control),false);value.serverExited=false;assert.equal(validChildResult(value,control),true);assert.equal(childAccepted(value,control),false);
 assert.equal(validCleanup({version,type:'recoveryPrivateCleanup',journeyId,contextClosed:true,browserClosed:true,serverExited:false,oracleExited:true},control),false);
});
test('public relay roundtrip requires every exact count to be zero',async()=>{
 const {publicFixtureResult,fixtureCountKeys}=await import('./contract.mjs'),counts=Object.fromEntries(fixtureCountKeys.map(k=>[k,0])),reply={version,type:'recoveryFixtureResult',requestId:1,status:'OK',counts};assert.equal(publicFixtureResult(reply),true);
 for(const key of fixtureCountKeys)assert.equal(publicFixtureResult({...reply,counts:{...counts,[key]:1}}),false);
});

test('actual download emitter preserves each exact private journey map and public boundary flags',async()=>{
 const {recordPublicDownloadChecks}=await import('./contract.mjs');
 for(const id of Object.keys(journeys)){
  // Synthetic maps exercise the same emitter called by requestOwnedDownload;
  // they are never substituted for actual browser observations or receipts.
  const control={...liveControl(),journeyId:id},flags=map(['BROWSER_ENGINE','SURFACE_CHECKED',...journeys[id]]),before=Object.keys(flags).sort();
  recordPublicDownloadChecks(control.mode,flags);
  assert.deepEqual(Object.keys(flags).sort(),before);
  assert.equal(Object.hasOwn(flags,'DOWNLOAD_ADMITTED'),false);assert.equal(Object.hasOwn(flags,'DOWNLOAD_REMOVED'),false);
  assert.equal(childAccepted({...winning(),journeyId:id,checks:flags},control),true);
  assert.equal(childAccepted({...winning(),journeyId:id,checks:{...flags,DOWNLOAD_ADMITTED:true,DOWNLOAD_REMOVED:true}},control),false);
 }
 const flags=Object.create(null);recordPublicDownloadChecks('public',flags);
 assert.deepEqual({...flags},{DOWNLOAD_ADMITTED:true,DOWNLOAD_REMOVED:true});
 assert.throws(()=>recordPublicDownloadChecks('LIVE',Object.create(null)));
});

test('stage settlement facts remain public-only and cannot enlarge exact live maps',async()=>{
 const {recordPublicStageCheck}=await import('./contract.mjs');for(const id of Object.keys(journeys)){const control={...liveControl(),journeyId:id},flags=map(['BROWSER_ENGINE','SURFACE_CHECKED',...journeys[id]]);recordPublicStageCheck('live',flags,true);assert.equal(childAccepted({...winning(),journeyId:id,checks:flags},control),true);assert.equal(Object.hasOwn(flags,'STAGE_SETTLED_PUBLIC'),false);}const publicFlags={};recordPublicStageCheck('public',publicFlags,false);assert.equal(publicFlags.STAGE_SETTLED_PUBLIC,false);recordPublicStageCheck('public',publicFlags,true);assert.equal(publicFlags.STAGE_SETTLED_PUBLIC,true);
});
test('new public stage faults require actual settlement and transfer cleanup in reducer',async()=>{
 const {publicQualified}=await import('./public-faults.mjs');
 for(const publicFault of ['stalled-partial-read','stalled-delete','cancel-stream-read','cancel-delete','cancel-initiation']){
  const isCancel=publicFault.startsWith('cancel-'),failureCode=isCancel?'SUPERVISOR_TIMEOUT':publicFault==='stalled-delete'?'DELETE_FAILED':'STREAM_FAILED';
  const receipt={version,type:'recoveryPrivateBrowserReceipt',journeyId,mode:'public',publicFault,sourceCommit:'a'.repeat(40),sourceCommitted:true,sourceFingerprint:'b'.repeat(64),browserVersion:'1.2.3.4',status:'BOUNDARY_FAILED',stage:'PUBLIC_ENTRY',failureCode,checks:{BROWSER_ENGINE:true,PUBLIC_ENTRY:true,SURFACE_CHECKED:true,STAGE_SETTLED_PUBLIC:true,DOWNLOAD_ADMITTED:true,DOWNLOAD_REMOVED:true},supervisor:map(supervisorChecks),childResultAccepted:true,protocolRejected:false,rawOutputRejected:false,timedOut:isCancel,cleanupForced:false,exitCode:1,contextClosed:true,browserClosed:true,serverExited:true,oracleExited:true,oracleGraceful:true,surfaceClean:true,surfaceRemoved:true,cleanupCompleted:true,publicFaultObserved:true,publicOwnership:{surface:null,childPid:null,oraclePid:null,serverPid:null},accepted:false};
  assert.equal(publicQualified(receipt),true);receipt.checks.STAGE_SETTLED_PUBLIC=false;assert.equal(publicQualified(receipt),false);receipt.checks.STAGE_SETTLED_PUBLIC=true;if(publicFault!=='cancel-initiation'){receipt.checks.DOWNLOAD_REMOVED=false;assert.equal(publicQualified(receipt),false);}
 }
});

test('actual extra-transfer guard cannot turn accepted second or missing events into expected rejection',async()=>{
 const {requireTwoDownloadRejection}=await import('./contract.mjs');assert.doesNotThrow(()=>requireTwoDownloadRejection(2,[true,false]));
 for(const states of [[true,true],[false,false],[true],[true,false,false]])assert.throws(()=>requireTwoDownloadRejection(states.length,states),error=>failureOf(error)==='PREDICATE_FAILED');
});
test('actual recovery page inventory rejects closed or unexpected owned surfaces',async()=>{
 const {requireExpectedRecoveryPages}=await import('./prepared-journeys.mjs');let closed=false,context;const page={isClosed:()=>closed,context:()=>context};context={pages:()=>[page]};const browser={contexts:()=>[context]};assert.doesNotThrow(()=>requireExpectedRecoveryPages(browser,[page]));closed=true;assert.throws(()=>requireExpectedRecoveryPages(browser,[page]));closed=false;context.pages=()=>[page,{}];assert.throws(()=>requireExpectedRecoveryPages(browser,[page]));context.pages=()=>[page];browser.contexts=()=>[context,{pages:()=>[]}];assert.throws(()=>requireExpectedRecoveryPages(browser,[page]));
});

test('actual recovery factory installs its context capability before page creation and preserves no-download default',async()=>{
 const {createRecoveryAccountMechanics}=await import('../../totp/sensitive/real-journey.mjs');let installed=false,closed=0,created=0,listener;const page={setDefaultTimeout:()=>{}};
 const context={on(event,callback){assert.equal(event,'page');installed=true;listener=callback;return this;},async newPage(){assert.equal(installed,true);created++;listener(page);return page;},async close(){closed++;}};
 const browser={async newContext(options){assert.equal(options.acceptDownloads,false);assert.equal(options.serviceWorkers,'block');return context;}};
 const input={page:{},context:{},browser,origin:'https://localhost:24443',mailpitOrigin:'http://127.0.0.1:28025',runId:'0'.repeat(32),journeyId,remember:()=>{},nativeCode:async()=>{throw fixedError();},fixtureControl:async()=>{throw fixedError();}};
 assert.throws(()=>createRecoveryAccountMechanics(input));assert.equal(created,0);let wired=0;const mechanics=createRecoveryAccountMechanics({...input,onContext:owned=>owned.on('page',()=>{wired++;})});try{await mechanics.newPage();assert.equal(created,1);assert.equal(wired,1);}finally{await mechanics.close();}assert.equal(closed,1);
});

test('each newly advertised exact public fact is required without changing historical branches',async()=>{
 const {publicQualified}=await import('./public-faults.mjs');
 // Fixed synthetic reducer fixtures only. They never become actual execution receipts.
 const expected=[{"fault":"popup-extra","status":"BOUNDARY_FAILED","failureCode":"ADMISSION_REJECTED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"iframe-extra","status":"BOUNDARY_FAILED","failureCode":"ADMISSION_REJECTED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"simultaneous-extra","status":"BOUNDARY_FAILED","failureCode":"ADMISSION_REJECTED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"cancel-initiation","status":"BOUNDARY_FAILED","failureCode":"SUPERVISOR_TIMEOUT","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":true,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","STAGE_SETTLED_PUBLIC"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"stalled-partial-read","status":"BOUNDARY_FAILED","failureCode":"STREAM_FAILED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","STAGE_SETTLED_PUBLIC","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"cancel-stream-read","status":"BOUNDARY_FAILED","failureCode":"SUPERVISOR_TIMEOUT","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":true,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","STAGE_SETTLED_PUBLIC","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"stalled-delete","status":"BOUNDARY_FAILED","failureCode":"DELETE_FAILED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","STAGE_SETTLED_PUBLIC","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"cancel-delete","status":"BOUNDARY_FAILED","failureCode":"SUPERVISOR_TIMEOUT","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":true,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","STAGE_SETTLED_PUBLIC","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false},{"fault":"secondary-context-extra","status":"BOUNDARY_FAILED","failureCode":"ADMISSION_REJECTED","stage":"PUBLIC_ENTRY","exitCode":1,"timedOut":false,"protocolRejected":false,"rawOutputRejected":false,"publicFaultObserved":true,"requiredTrue":["sourceCommitted","childResultAccepted","contextClosed","browserClosed","serverExited","oracleExited","oracleGraceful","surfaceClean","surfaceRemoved","cleanupCompleted"],"requiredSupervisorTrue":["SOURCE_CURRENT","PROTOCOL_VALID","OUTPUT_CONTAINED","CHILD_CLOSED","ORACLE_CLOSED","CHILD_SURFACES_CLOSED","SURFACE_REMOVED"],"requiredChildTrue":["BROWSER_ENGINE","PUBLIC_ENTRY","SURFACE_CHECKED","DOWNLOAD_ADMITTED","DOWNLOAD_REMOVED"],"requiredFalse":["accepted","cleanupForced"],"executed":false}];
 for(const f of expected){
  const positive={version,type:'recoveryPrivateBrowserReceipt',journeyId,mode:'public',publicFault:f.fault,sourceCommit:'a'.repeat(40),sourceCommitted:true,sourceFingerprint:'b'.repeat(64),browserVersion:'1.2.3.4',status:f.status,stage:f.stage,failureCode:f.failureCode,checks:map(f.requiredChildTrue),supervisor:map(supervisorChecks),childResultAccepted:true,protocolRejected:false,rawOutputRejected:false,timedOut:f.timedOut,cleanupForced:false,exitCode:f.exitCode,contextClosed:true,browserClosed:true,serverExited:true,oracleExited:true,oracleGraceful:true,surfaceClean:true,surfaceRemoved:true,cleanupCompleted:true,publicFaultObserved:true,publicOwnership:{surface:null,childPid:null,oraclePid:null,serverPid:null},accepted:false};
  assert.equal(publicQualified(positive),true,f.fault+' positive');
  for(const key of [...f.requiredTrue,'publicFaultObserved'])assert.equal(publicQualified({...positive,[key]:false}),false,f.fault+' '+key);
  for(const key of f.requiredSupervisorTrue)assert.equal(publicQualified({...positive,supervisor:{...positive.supervisor,[key]:false}}),false,f.fault+' supervisor '+key);
  for(const key of f.requiredChildTrue)assert.equal(publicQualified({...positive,checks:{...positive.checks,[key]:false}}),false,f.fault+' child '+key);
  for(const key of [...f.requiredFalse,'protocolRejected','rawOutputRejected'])assert.equal(publicQualified({...positive,[key]:true}),false,f.fault+' '+key);
  for(const [key,value] of [['stage','COMPLETE'],['status','PUBLIC_BOUNDARY_OK'],['failureCode','NONE'],['exitCode',0],['timedOut',!f.timedOut]])assert.equal(publicQualified({...positive,[key]:value}),false,f.fault+' fixed '+key);
 }
});

test('actual metadata header guard follows session versus Recovery and StepUp ownership',async()=>{
 const {recoveryMetadataProtection}=await import('./prepared-journeys.mjs'),onlyStore={'cache-control':'no-store'},protectedHeaders={...onlyStore,'referrer-policy':'no-referrer'};
 assert.equal(recoveryMetadataProtection('/authentication/session',onlyStore),true);
 for(const pathname of ['/account/recovery-codes/options','/authentication/step-up/options']){assert.equal(recoveryMetadataProtection(pathname,onlyStore),false);assert.equal(recoveryMetadataProtection(pathname,protectedHeaders),true);}
 for(const pathname of ['/authentication/session','/account/recovery-codes/options','/authentication/step-up/options'])assert.equal(recoveryMetadataProtection(pathname,{'referrer-policy':'no-referrer'}),false);
 assert.throws(()=>recoveryMetadataProtection('/unregistered',protectedHeaders));
});

test('current download identity rejects an owned hardlink and replacement at the same pathname',async()=>{
 await fixture(async identity=>{
  const folder=join(identity.path,'downloads');await mkdir(folder);const file=join(folder,'public-file'),alias=join(identity.path,'public-alias');await writeFile(file,publicBody);
  const admitted=await downloadIdentity(identity,file);await link(file,alias);
  try{await assert.rejects(downloadIdentity(identity,file),error=>failureOf(error)==='PATH_REJECTED');}finally{await unlink(alias);}
  await rename(file,join(folder,'public-original'));await writeFile(file,publicBody);
  assert.equal(await sameDownload(identity,admitted),false);
 });
});

test('current download identity rejects a symlink or Windows junction inside its owned temp root',async()=>{
 await fixture(async identity=>{
  const backing=join(identity.path,'public-backing'),folder=join(identity.path,'downloads');await mkdir(backing);await writeFile(join(backing,'public-file'),publicBody);
  await symlink(backing,folder,process.platform==='win32'?'junction':'dir');
  try{assert.equal((await lstat(folder)).isSymbolicLink(),true);assert.equal(await realpath(folder),await realpath(backing));await assert.rejects(downloadIdentity(identity,join(folder,'public-file')),error=>failureOf(error)==='PATH_REJECTED');}
  finally{await unlink(folder);}
 });
});

for(const shape of ['empty','truncated'])test('actual inspector rejects a completed '+shape+' public stream',async()=>{
 await fixture(async identity=>{
  const folder=join(identity.path,'downloads');await mkdir(folder);const file=join(folder,'public-file'),expected=Buffer.from(publicBody),received=shape==='empty'?Buffer.alloc(0):Buffer.from(expected.subarray(0,expected.length-1));await writeFile(file,received);
  const stream=Readable.from([received]);let deletes=0,failure='NONE';const download={path:async()=>file,createReadStream:async()=>stream,delete:async()=>{deletes++;await unlink(file);}};
  try{try{await inspectRequestedDownload(download,identity,expected);}catch(error){failure=failureOf(error);}assert.equal(failure,'BYTES_MISMATCH');assert.equal(stream.destroyed,true);assert.equal(deletes,0);}
  finally{expected.fill(0);received.fill(0);}
 });
});

for(const stage of ['stream','delete'])test('actual '+stage+' exception with public sentinel reduces without exporting its error',async()=>{
 await fixture(async identity=>{
  const folder=join(identity.path,'downloads');await mkdir(folder);const file=join(folder,'public-file'),expected=Buffer.from(publicBody);await writeFile(file,expected);
  let thrown=false,deletes=0,failure='NONE';const stream=stage==='stream'?Readable.from((async function*(){thrown=true;throw new Error(publicMarker);})()):Readable.from([Buffer.from(expected)]);
  const download={path:async()=>file,createReadStream:async()=>stream,delete:async()=>{deletes++;if(stage==='delete'){thrown=true;throw new Error(publicMarker);}await unlink(file);}};
  // Exercise the actual async stage and the same fixed reducer used by raw child catch.
  // Do not retain/read/stringify the Error, its message or stack in a test/report.
  try{try{await inspectRequestedDownload(download,identity,expected);}catch(error){failure=failureOf(error);}assert.equal(thrown,true);assert.equal(failure,'BOUNDARY_ERROR');assert.equal(stream.destroyed,true);assert.equal(deletes,stage==='delete'?1:0);}
  finally{expected.fill(0);}
 });
});
