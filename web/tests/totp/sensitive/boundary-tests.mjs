import test from 'node:test';
import assert from 'node:assert/strict';
import {PassThrough} from 'node:stream';
import {spawnSync} from 'node:child_process';
import {EventEmitter} from 'node:events';
import {resolve} from 'node:path';
import {parsePrivateJson, PrivateFrameReader, writePrivateFrame} from './private-frames.mjs';
import {realJourneys, realJourneyStages, realPredicatesSatisfied, validRealChecks, validRealControl, validRealReceipt, realReceiptAccepted, validFixtureRequest, validFixtureResult, fixtureCountKeys, sameCanonicalPath} from './contract.mjs';
import {childEnvironment, runRealScenario, sensitiveSourceFacts} from './supervisor.mjs';
import {waitForOwnedProcessClose} from './oracle-bridge.mjs';

const journeyId = Object.keys(realJourneys)[0];
const control = () => ({version: 3, type: 'run', journeyId, browserEngine: 'chromium', origin: 'https://localhost:24443', mailpitOrigin: 'http://127.0.0.1:28025', sourceCommit: 'a'.repeat(40), sourceCommitted: true, runId: 'b'.repeat(32), oracleAssemblyPath: resolve('tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'), oracleNativeVersion: '10.0.13', publicFault: null});
const receipt = () => ({version: 3, type: 'browserReceipt', journeyId, browserEngine: 'chromium', browserVersion: '151.0.7922.34', status: 'REAL_JOURNEY_OK', stage: 'COMPLETE', failureCode: 'NONE', checks: Object.fromEntries(realJourneys[journeyId].map(id => [id, true])), oracleRequests: 1, rawOutputRejected: false, processExited: true, oracleExited: true, surfaceClean: true, surfaceRemoved: true, cleanupCompleted: true, publicFaultObserved: false, sourceCommit: 'a'.repeat(40), sourceCommitted: true, accepted: false});

test('child environment fixes OS trust and excludes operator TLS/debug overrides', () => {
  const overrides = {
    NODE_USE_SYSTEM_CA: '0', NODE_TLS_REJECT_UNAUTHORIZED: '0', NoDe_OpTiOnS: '--require=PUBLIC_FORBIDDEN_PRELOAD',
    NODE_EXTRA_CA_CERTS: 'PUBLIC_FORBIDDEN_CA', sSl_CeRt_FiLe: 'PUBLIC_FORBIDDEN_CA', SSL_CERT_DIR: 'PUBLIC_FORBIDDEN_CA_DIR',
    OPENSSL_CONF: 'PUBLIC_FORBIDDEN_OPENSSL_CONFIG', NODE_USE_ENV_PROXY: '1', HTTPS_PROXY: 'http://public.invalid:9',
    DEBUG: 'PUBLIC_DEBUG', PWDEBUG: '1', NODE_V8_COVERAGE: 'PUBLIC_FORBIDDEN_COVERAGE',
    NODE_REDIRECT_WARNINGS: 'PUBLIC_FORBIDDEN_WARNINGS', PLAYWRIGHT_DEBUG_LOG: 'PUBLIC_FORBIDDEN_LOG'
  };
  const saved = Object.entries(overrides).map(([key]) => [key, process.env[key]]);
  const surface = resolve('PUBLIC_ENVIRONMENT_SURFACE'); let env;
  try { for (const [key, value] of Object.entries(overrides)) process.env[key] = value; env = childEnvironment(surface); }
  finally { for (const [key, value] of saved.reverse()) { if (value === undefined) delete process.env[key]; else process.env[key] = value; } }
  assert.equal(env.NODE_USE_SYSTEM_CA, '1');
  const blocked = Object.keys(overrides).map(key => key.toUpperCase()).filter(key => key !== 'NODE_USE_SYSTEM_CA');
  assert.equal(Object.keys(env).some(key => blocked.includes(key.toUpperCase())), false);
  assert.equal(env.TMP, surface); assert.equal(env.TEMP, surface); assert.equal(env.TMPDIR, surface);
  const child = spawnSync(process.execPath, ['--input-type=module', '-e',
    "const blocked = ['NODE_TLS_REJECT_UNAUTHORIZED','NODE_OPTIONS','NODE_EXTRA_CA_CERTS','SSL_CERT_FILE','SSL_CERT_DIR','OPENSSL_CONF','NODE_USE_ENV_PROXY','HTTPS_PROXY','DEBUG','PWDEBUG','NODE_V8_COVERAGE','NODE_REDIRECT_WARNINGS','PLAYWRIGHT_DEBUG_LOG']; process.stdout.write(JSON.stringify({fixedSystemTrust:process.env.NODE_USE_SYSTEM_CA==='1',operatorOverridesAbsent:!Object.keys(process.env).some(key=>blocked.includes(key.toUpperCase())),temporarySurfaceBound:process.env.TMP===process.env.TEMP&&process.env.TEMP===process.env.TMPDIR}));"
  ], {env, encoding: 'utf8', timeout: 5000, maxBuffer: 1024, windowsHide: true});
  assert.equal(child.error, undefined); assert.equal(child.signal, null); assert.equal(child.status, 0); assert.equal(child.stderr, '');
  assert.deepEqual(JSON.parse(child.stdout), {fixedSystemTrust: true, operatorOverridesAbsent: true, temporarySurfaceBound: true});
});

test('fixed journey steps are failure metadata and cannot substitute for completion', () => {
  assert.deepEqual(realJourneyStages, ['JOURNEY','JOURNEY_ENROLL_BEGIN','JOURNEY_ENROLL_DISCLOSURE','JOURNEY_ENROLL_RESULT_BINDING','JOURNEY_ENROLL_DUPLICATE','JOURNEY_ENROLL_DONE']);
  for (const stage of realJourneyStages) {
    const failed = {...receipt(), status: 'BOUNDARY_FAILED', stage, failureCode: 'PREDICATE_FAILED'};
    assert.equal(validRealReceipt(failed), true); assert.equal(realReceiptAccepted(failed), false);
    assert.equal(realReceiptAccepted({...receipt(), stage}), false);
  }
  for (const stage of ['JOURNEY_ENROLL_UNKNOWN','PUBLIC_ERROR_MESSAGE','https://public.invalid/path',null]) assert.equal(validRealReceipt({...receipt(), stage}), false);
});
test('failed reporting may retain every later observed truth without acquiring success', () => {
  const checks = {...receipt().checks, REPLAY_REJECTED: false, RESULT_RECOVERY: true, UI_ASSET_TYPOGRAPHY: true};
  const failed = {...receipt(), status: 'BOUNDARY_FAILED', stage: 'JOURNEY_ENROLL_DONE', failureCode: 'PREDICATE_FAILED', checks};
  assert.equal(validRealChecks(checks, journeyId), true); assert.equal(validRealReceipt(failed), true);
  assert.equal(failed.checks.RESULT_RECOVERY, true); assert.equal(failed.checks.UI_ASSET_TYPOGRAPHY, true);
  assert.equal(realPredicatesSatisfied(checks, journeyId), false); assert.equal(realReceiptAccepted(failed), false);
  assert.equal(realReceiptAccepted({...failed, accepted: true, status: 'REAL_JOURNEY_OK', stage: 'COMPLETE', failureCode: 'NONE'}), false);
});

test('each static journey has unique required browser predicates', () => { for (const ids of Object.values(realJourneys)) assert.equal(new Set(ids).size, ids.length); });
test('only a complete executed predicate map is accepted', () => {
  assert.equal(realPredicatesSatisfied(receipt().checks, journeyId), true);
  assert.equal(realPredicatesSatisfied({}, journeyId), false);
  for (const id of realJourneys[journeyId]) { const x = receipt().checks; delete x[id]; assert.equal(realPredicatesSatisfied(x, journeyId), false); }
});
test('false, string, unknown and coordinator-owned predicates reject', () => {
  for (const checks of [{...receipt().checks, BFF_ISOLATED: false}, {...receipt().checks, BFF_ISOLATED: 'true'}, {...receipt().checks, TOKEN_REVOKED: true}, {...receipt().checks, invented: true}]) assert.equal(realPredicatesSatisfied(checks, journeyId), false);
});
test('valid failure envelopes remain representable without acquiring success', () => {
  const x = {...receipt(), checks: {BROWSER_ENGINE: true}, status: 'BOUNDARY_FAILED', failureCode: 'BOUNDARY_ERROR', stage: 'JOURNEY'};
  assert.equal(validRealReceipt(x), true); assert.equal(realReceiptAccepted(x), false);
});
test('cleanup, output, source and public-fault failures cannot be approved', () => {
  assert.equal(realReceiptAccepted(receipt()), true);
  for (const [key, value] of [['cleanupCompleted', false], ['oracleExited', false], ['processExited', false], ['surfaceClean', false], ['surfaceRemoved', false], ['sourceCommitted', false], ['sourceCommit', null], ['rawOutputRejected', true], ['publicFaultObserved', true]]) assert.equal(realReceiptAccepted({...receipt(), [key]: value}), false);
});
test('outer accepted flag is never used as the acceptance proof', () => { assert.equal(realReceiptAccepted({...receipt(), accepted: true, checks: {}}), false); });
test('control rejects arbitrary dispatch, remote endpoints and unknown fields', () => {
  assert.equal(validRealControl(control()), true);
  for (const x of [{...control(), journeyId: 'constructor'}, {...control(), module: './anything.mjs'}, {...control(), browserEngine: 'edge'}, {...control(), origin: 'https://remote.example:24443'}, {...control(), origin: 'https://localhost:24443/?secret=PUBLIC'}, {...control(), mailpitOrigin: 'http://127.0.0.1:28025/path'}, {...control(), sourceCommit: ['a'.repeat(40)]}, {...control(), publicFault: 'eval'}]) assert.equal(validRealControl(x), false);
});
test('arbitrary DLL and uncommitted real source reject before process creation', async () => {
  const source = sensitiveSourceFacts();
  const arbitrary = await runRealScenario({...control(), ...source, oracleAssemblyPath: resolve('tools/browser-testing/bin/Debug/net10.0/other.dll'), publicFault: 'oracle-roundtrip'});
  assert.equal(arbitrary.accepted, false); assert.equal(arbitrary.processExited, false); assert.equal(arbitrary.failureCode, 'SOURCE_INVALID');
  const uncommitted = await runRealScenario({...control(), ...source, sourceCommitted: false});
  assert.equal(uncommitted.accepted, false); assert.equal(uncommitted.processExited, false); assert.equal(uncommitted.failureCode, 'SOURCE_INVALID');
});
test('private fixture relay accepts only bounded fixed metadata', () => {
  assert.equal(validFixtureRequest({version: 3, type: 'fixtureRequest', requestId: 1, action: 'READ_SAFE_COUNTS'}), true);
  for (const action of ['SQL', 'INVALIDATE_SOURCE_ONCE', 'constructor']) assert.equal(validFixtureRequest({version: 3, type: 'fixtureRequest', requestId: 1, action}), false);
  const x = {version: 3, type: 'fixtureResult', requestId: 1, status: 'OK', counts: Object.fromEntries(fixtureCountKeys.map(key => [key, 0]))};
  assert.equal(validFixtureResult(x), true); assert.equal(validFixtureResult({...x, seed: 'PUBLIC'}), false); assert.equal(validFixtureResult({...x, counts: {...x.counts, sessions: 1001}}), false);
});
test('private JSON rejects duplicate keys, malformed encoding and trailing values', () => {
  for (const value of ['{"code":"PUBLIC","code":"OTHER"}', '{"x":{"a":1,"a":2}}', '{"x":1}{}', '{"x":NaN}', '[1,]', '{"x":"\\q"}', '{"x":01}']) assert.throws(() => parsePrivateJson(Buffer.from(value)), /^Error: FRAME_INVALID$/);
  assert.throws(() => parsePrivateJson(Buffer.from([0xff])), /^Error: FRAME_INVALID$/);
  assert.equal(parsePrivateJson(Buffer.from('{"x":[true,false,null,2.5]}')).x[3], 2.5);
});
test('private framing survives split header/payload without displaying values', async () => {
  const stream = new PassThrough(); const reader = new PrivateFrameReader(stream); const next = reader.next(1000);
  const payload = Buffer.from('{"version":1,"type":"ready"}'); const header = Buffer.alloc(4); header.writeUInt32BE(payload.length);
  stream.write(header.subarray(0, 2)); stream.write(Buffer.concat([header.subarray(2), payload.subarray(0, 3)])); stream.write(payload.subarray(3));
  const value = await next; assert.equal(value.type, 'ready'); assert.equal(reader.drained, true); reader.dispose(); stream.destroy();
});
test('oversize/zero/truncated frames and EOF fail closed', async () => {
  for (const length of [0, 2049]) {
    const stream = new PassThrough(); const reader = new PrivateFrameReader(stream); const next = reader.next(1000); const header = Buffer.alloc(4); header.writeUInt32BE(length); stream.end(header); await assert.rejects(next, /^Error: FRAME_INVALID$/); reader.dispose();
  }
  const stream = new PassThrough(); const reader = new PrivateFrameReader(stream); const next = reader.next(1000); stream.end(Buffer.from([0, 0, 0, 5, 123])); await assert.rejects(next, /^Error: FRAME_INVALID$/); reader.dispose();
  const eof = new PassThrough(); const eofReader = new PrivateFrameReader(eof); const pending = eofReader.next(1000); eof.end(); await assert.rejects(pending, /^Error: FRAME_INVALID$/); assert.equal(eofReader.failureReason, 'EOF'); eofReader.dispose();
});
test('timeout and cancellation discard partial private buffers', async () => {
  for (const cancel of [false, true]) {
    const stream = new PassThrough(); const reader = new PrivateFrameReader(stream); const signal = new AbortController(); const next = reader.next(20, signal.signal);
    stream.write(Buffer.from([0, 0])); if (cancel) signal.abort(); await assert.rejects(next, /^Error: FRAME_INVALID$/); assert.equal(reader.buffer.length, 0); assert.equal(reader.failureReason, cancel ? 'CANCELED' : 'TIMEOUT'); reader.dispose(); stream.destroy();
  }
});
test('private writer enforces payload cap and emits one framed object', async () => {
  const stream = new PassThrough(); const reader = new PrivateFrameReader(stream); const next = reader.next(1000);
  await writePrivateFrame(stream, {version: 1, type: 'stop'}); assert.equal((await next).type, 'stop');
  await assert.rejects(writePrivateFrame(stream, {value: 'PUBLIC'.repeat(1000)}), /^Error: FRAME_INVALID$/); reader.dispose(); stream.destroy();
});
test('unsolicited frame flood is discarded and cannot resume after failure', async () => {
  const stream = new PassThrough(); const reader = new PrivateFrameReader(stream, {queue: 1});
  await writePrivateFrame(stream, {public: 1}); await writePrivateFrame(stream, {public: 2});
  assert.equal(reader.failed, true); assert.equal(reader.frames.length, 0);
  await writePrivateFrame(stream, {public: 3}); assert.equal(reader.frames.length, 0);
  await assert.rejects(reader.next(1000), /^Error: FRAME_INVALID$/); reader.dispose(); stream.destroy();
});
test('oracle cleanup waits for close after exit or signal metadata', async () => {
  for (const metadata of [{exitCode: 0, signalCode: null}, {exitCode: null, signalCode: 'SIGTERM'}]) {
    const child = Object.assign(new EventEmitter(), metadata); const state = {closed: false}; let settled = false;
    const result = waitForOwnedProcessClose(child, state, 1000); void result.then(() => { settled = true; });
    await new Promise(resolve => setTimeout(resolve, 5)); assert.equal(settled, false); assert.equal(state.closed, false);
    child.emit('close'); assert.equal(await result, true); assert.equal(state.closed, true);
  }
  const child = Object.assign(new EventEmitter(), {exitCode: 0, signalCode: null}); const state = {closed: false};
  assert.equal(await waitForOwnedProcessClose(child, state, 10), false); assert.equal(state.closed, false);
});
test('canonical path case folding follows the actual operating system', () => {
  assert.equal(sameCanonicalPath(resolve('OwnedPath'), resolve('OwnedPath')), true);
  assert.equal(sameCanonicalPath(resolve('OwnedPath'), resolve('ownedpath')), process.platform === 'win32');
});
