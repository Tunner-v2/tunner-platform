import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, link, unlink, rename, realpath} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {version, scenarios, publicBody, publicMarker, budgets, createAdmission, validChildResult, requiredChecks, canaryPassed, failureOf, fixedError, validSurfacePath, downloadIdentity, sameDownload, inspectSurface, removeOwnedSurface} from './contract.mjs';

// These are public synthetic contract fixtures, never browser execution evidence.
function happy() {
  return {version, type: 'publicRecoveryResult', scenario: 'happy', browserVersion: '151.0.7922.34', status: 'PUBLIC_BOUNDARY_OK', failureCode: 'NONE', checks: Object.fromEntries(requiredChecks('happy').map(id => [id, true])), downloadEvents: 1, faultObserved: false, cleanupCompleted: true};
}
const parent = () => ({scenario: 'happy', childClosed: true, browserClosedVerified: true, surfaceRemoved: true, sourceCommitted: true, exitCode: 0, outputRejected: false, protocolRejected: false, surfaceClean: true, timedOut: false});
test('one admission cannot be rearmed or used for another page', () => {
  const owner = {}; const other = {}; const admission = createAdmission();
  assert.equal(admission.accept(owner), false);
  assert.throws(() => admission.arm(owner));
  const current = createAdmission(); current.arm(owner); assert.equal(current.accept(other), false); assert.equal(current.accept(owner), false);
});
test('single admission remains one-shot after completion and close', () => {
  const owner = {}; const admission = createAdmission(); admission.arm(owner); assert.equal(admission.accept(owner), true);
  admission.settle(); assert.throws(() => admission.arm(owner)); assert.equal(admission.accept(owner), false); admission.close(); assert.equal(admission.accept(owner), false);
});
test('every missing or false required observation rejects successful qualification', () => {
  assert.equal(canaryPassed(parent(), happy()), true);
  for (const id of requiredChecks('happy')) {
    const missing = happy(); delete missing.checks[id]; assert.equal(canaryPassed(parent(), missing), false);
    const failed = happy(); failed.checks[id] = false; assert.equal(canaryPassed(parent(), failed), false);
  }
});
test('foreign component claims and unregistered fields are rejected', () => {
  const extra = happy(); extra.checks.SAFE_PERSISTENCE_AND_AUDIT = true; assert.equal(validChildResult(extra, 'happy'), false);
  assert.equal(validChildResult({...happy(), arbitrary: publicMarker}, 'happy'), false);
  assert.equal(validChildResult({...happy(), scenario: 'duplicate'}, 'happy'), false);
  assert.equal(validChildResult({...happy(), scenario: 'not-registered'}, 'not-registered'), false);
  assert.equal(canaryPassed(parent(), {...happy(), browserVersion: null}), false);
});
test('parent-owned exit/output/surface/source controls cannot be replaced by a child result', () => {
  for (const key of ['childClosed', 'browserClosedVerified', 'surfaceRemoved', 'sourceCommitted', 'surfaceClean']) assert.equal(canaryPassed({...parent(), [key]: false}, happy()), false);
  for (const key of ['outputRejected', 'protocolRejected', 'timedOut']) assert.equal(canaryPassed({...parent(), [key]: true}, happy()), false);
  assert.equal(canaryPassed({...parent(), exitCode: 1}, happy()), false);
});
test('valid failed envelope is distinct from success and expected-fault qualification', () => {
  const failed = {...happy(), status: 'BOUNDARY_FAILED', failureCode: 'ACTION_FAILED'};
  assert.equal(validChildResult(failed, 'happy'), true); assert.equal(canaryPassed(parent(), failed), false);
  const fault = {...failed, scenario: 'action-timeout', downloadEvents: 0, faultObserved: true, checks: Object.fromEntries(requiredChecks('action-timeout').map(id => [id, true]))};
  assert.equal(canaryPassed({...parent(), scenario: 'action-timeout', exitCode: 1}, fault), true);
  fault.checks.FAULT_OBSERVED = false; assert.equal(canaryPassed({...parent(), scenario: 'action-timeout', exitCode: 1}, fault), false);
});
test('raw errors are reduced without reading their message or stack', () => {
  const raw = {}; Object.defineProperties(raw, {message: {get() { throw new Error('MUST_NOT_READ'); }}, stack: {get() { throw new Error('MUST_NOT_READ'); }}});
  assert.equal(failureOf(raw), 'BOUNDARY_ERROR'); assert.equal(failureOf(fixedError('SIZE_LIMIT')), 'SIZE_LIMIT');
});
async function owned(run) {
  const surface = await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-009-public-sensitive-'));
  try { await mkdir(join(surface, 'downloads')); await run(surface); } finally { assert.equal(await removeOwnedSurface(surface), true); }
}
test('only the exact admitted regular file is exempt during transfer', async () => owned(async surface => {
  const path = join(surface, 'downloads', 'public-file'); await writeFile(path, publicBody);
  const identity = await downloadIdentity(surface, path); assert.equal((await inspectSurface(surface, {admitted: identity})).clean, true);
  assert.equal((await inspectSurface(surface)).clean, false);
  await writeFile(join(surface, 'downloads', 'second-extensionless-file'), 'PUBLIC');
  assert.equal((await inspectSurface(surface, {admitted: identity})).clean, false);
}));
test('outside paths, hard links, oversized files and replaced identities are rejected', async () => owned(async surface => {
  assert.equal(validSurfacePath(join(surface, 'nested')), false);
  await assert.rejects(downloadIdentity(surface, join(surface, '..', 'not-owned')));
  const path = join(surface, 'downloads', 'public-file'); await writeFile(path, publicBody);
  const identity = await downloadIdentity(surface, path);
  await link(path, join(surface, 'public-link')); await assert.rejects(downloadIdentity(surface, path)); await unlink(join(surface, 'public-link'));
  await rename(path, join(surface, 'old-public-file')); await writeFile(path, publicBody); assert.equal(await sameDownload(surface, identity), false);
  await writeFile(path, Buffer.alloc(budgets.maximumDownloadBytes + 1, 80)); await assert.rejects(downloadIdentity(surface, path));
}));
test('profile marker split across scan chunks remains forbidden', async () => owned(async surface => {
  const profile = join(surface, 'playwright_chromiumdev_profile-PUBLIC'); await mkdir(profile);
  await writeFile(join(profile, 'public-data'), Buffer.concat([Buffer.alloc(65535, 80), Buffer.from(publicMarker)]));
  assert.equal((await inspectSurface(surface)).clean, false);
}));
test('registry is public-only with finite registered scenarios', () => {
  assert.equal(scenarios.length, new Set(scenarios).size);
  assert.equal(scenarios.includes('real'), false); assert.equal(publicBody.includes('NOT A RECOVERY CODE'), true);
  assert.equal(canaryPassed({...parent(), sourceCommitted: false}, happy()), false);
});
