import test from 'node:test';
import assert from 'node:assert/strict';
import {PassThrough} from 'node:stream';
import {EventEmitter} from 'node:events';
import {resolve} from 'node:path';
import {parsePrivateJson, PrivateFrameReader, writePrivateFrame} from './private-frames.mjs';
import {realJourneys, realPredicatesSatisfied, validRealChecks, validRealControl, validRealReceipt, realReceiptAccepted, validFixtureRequest, validFixtureResult, fixtureCountKeys, sameCanonicalPath} from './contract.mjs';
import {runRealScenario, sensitiveSourceFacts} from './supervisor.mjs';
import {waitForOwnedProcessClose} from './oracle-bridge.mjs';

const journeyId = Object.keys(realJourneys)[0];
const control = () => ({version: 3, type: 'run', journeyId, browserEngine: 'chromium', origin: 'https://localhost:24443', mailpitOrigin: 'http://127.0.0.1:28025', sourceCommit: 'a'.repeat(40), sourceCommitted: true, runId: 'b'.repeat(32), oracleAssemblyPath: resolve('tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'), oracleNativeVersion: '10.0.13', publicFault: null});
const receipt = () => ({version: 3, type: 'browserReceipt', journeyId, browserEngine: 'chromium', browserVersion: '151.0.7922.34', status: 'REAL_JOURNEY_OK', stage: 'COMPLETE', failureCode: 'NONE', checks: Object.fromEntries(realJourneys[journeyId].map(id => [id, true])), oracleRequests: 1, rawOutputRejected: false, processExited: true, oracleExited: true, surfaceClean: true, surfaceRemoved: true, cleanupCompleted: true, publicFaultObserved: false, sourceCommit: 'a'.repeat(40), sourceCommitted: true, accepted: false});

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
