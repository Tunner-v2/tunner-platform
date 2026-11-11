import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, realpath, lstat} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {createAdmission} from '../sensitive/contract.mjs';
import {parsePrivateJson} from '../../totp/sensitive/private-frames.mjs';
import {version, journeys, childChecks, supervisorChecks, coordinatorChecks, validControl, validChildResult, childAccepted, realAccepted, fixedError, failureOf, observedMap, publicMarker, publicBody} from './contract.mjs';
import {ownedSurface, removeOwnedSurface, validSurfacePath, downloadIdentity, privateMarkers, inspectSurface} from './surface.mjs';
import {runJourney} from './journeys.mjs';

// Public synthetic maps test reducer mechanics. They never enter actual receipts.
const journeyId = 'RECOVERY_GENERATE_PASSWORD_V1';
const publicControl = () => ({version, type: 'runRecoveryPrivate', mode: 'public', journeyId, publicFault: 'happy', sourceCommit: 'a'.repeat(40), sourceCommitted: true, origin: null, mailpitOrigin: null, runId: '0'.repeat(32)});
const liveControl = () => ({...publicControl(), mode: 'live', publicFault: null, origin: 'https://localhost:24443', mailpitOrigin: 'http://127.0.0.1:28025'});
const map = ids => Object.fromEntries(ids.map(id => [id, true]));
const winning = () => ({version, type: 'recoveryPrivateChildResult', journeyId, mode: 'live', publicFault: null, browserVersion: '1.2.3.4', status: 'JOURNEY_OK', stage: 'COMPLETE', failureCode: 'NONE', checks: map(['BROWSER_ENGINE', 'SURFACE_CHECKED', ...journeys[journeyId]]), publicFaultObserved: false, contextClosed: true, browserClosed: true, oracleExited: true, oracleGraceful: true, surfaceClean: true, cleanupCompleted: true});
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
test('unfinished live dispatcher flushes all false facts and rejects before account or value work', async () => {
  const seen = []; await assert.rejects(runJourney({control: liveControl(), check(id, value) { seen.push([id, value]); }}), error => failureOf(error) === 'DEPENDENCY_NOT_READY');
  assert.equal(seen.length, journeys[journeyId].length); assert.equal(seen.every(([, value]) => value === false), true);
});
