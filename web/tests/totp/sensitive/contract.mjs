import {lstat, readdir, open, realpath} from 'node:fs/promises';
import {isAbsolute, join, relative, resolve, sep} from 'node:path';
import {tmpdir} from 'node:os';

export const version = 2;
export const browserEngines = Object.freeze(['chromium', 'edge']);
export const publicMarker = 'TUNNER_PUBLIC_NONFUNCTIONAL_CANARY_008';
export const publicUri = 'otpauth://totp/Tunner:PUBLIC-CANARY?secret=PUBLIC-NONFUNCTIONAL-SEED&issuer=Tunner';
export const scenarios = Object.freeze(['happy', 'locator', 'fill', 'assertion', 'unhandled', 'uncaught', 'timeout', 'cleanup', 'supervisor-timeout', 'unexpected-output', 'invalid-result']);
export const statuses = Object.freeze(['PUBLIC_CANARY_OK', 'EXPECTED_ERROR_CONTAINED', 'EXPECTED_FATAL_CONTAINED', 'BOUNDARY_FAILED']);
export const stages = Object.freeze(['START', 'ENVIRONMENT', 'LIBRARY', 'BROWSER', 'PUBLIC_DOM', 'PUBLIC_ACTION', 'SURFACE_SCAN', 'CLEANUP', 'COMPLETE']);
export const failureCodes = Object.freeze(['NONE', 'BOUNDARY_ERROR', 'SUPERVISOR_TIMEOUT', 'CLEANUP_FAILED', 'SURFACE_INVALID', 'SURFACE_LIMIT', 'SURFACE_ACCESS_DENIED', 'SURFACE_READ_FAILED', 'SURFACE_FAILED']);
// Future real-journey code must reduce these predicates to booleans inside the child.
// Their presence here does not claim that the real journey exists or has executed.
export const predicateIds = Object.freeze(['BROWSER_ENGINE', 'PUBLIC_DOM', 'EXPECTED_ERROR', 'FATAL_HANDLER', 'CLEANUP_ATTEMPTED', 'BFF_ISOLATED', 'REQUEST_BINDING', 'OPERATION_BINDING', 'DUPLICATE_COUNTS', 'INACTIVE_BEFORE_ACTIVATION', 'NO_EARLY_SESSION', 'REPLAY_REJECTED', 'RESULT_RECOVERY', 'COOKIE_FLAGS', 'NO_URL_OR_STORAGE_SECRET', 'SOURCE_FRESHNESS', 'PASSWORD_REGRESSION', 'PASSKEY_REGRESSION']);
const markers = [publicMarker, publicUri, 'otpauth://', 'PUBLIC-NONFUNCTIONAL-SEED'].map(value => Buffer.from(value));
const temporaryRoot = resolve(await realpath(process.env.TUNNER_CANARY_OS_TEMP_ROOT ?? tmpdir()));
const exactKeys = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).sort().join('|') === [...keys].sort().join('|');
export function sameCanonicalPath(left, right) { const a = resolve(left), b = resolve(right); return process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b; }
export function hasMarker(bytes) { return markers.some(marker => bytes.includes(marker)); }
export function validResult(value, scenario, browserEngine) {
  return exactKeys(value, ['version', 'scenario', 'browserEngine', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'filesScanned', 'surfaceClean', 'cleanupCompleted']) && value.version === version && value.scenario === scenario && browserEngines.includes(browserEngine) && value.browserEngine === browserEngine && (value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) && statuses.includes(value.status) && stages.includes(value.stage) && failureCodes.includes(value.failureCode) && Number.isSafeInteger(value.filesScanned) && value.filesScanned >= 0 && value.filesScanned <= 10000 && typeof value.surfaceClean === 'boolean' && typeof value.cleanupCompleted === 'boolean' && value.checks !== null && typeof value.checks === 'object' && !Array.isArray(value.checks) && Object.entries(value.checks).every(([key, result]) => predicateIds.includes(key) && typeof result === 'boolean');
}
export function predicatesSatisfied(value, scenario, browserEngine) {
  // A valid diagnostic envelope can legitimately report missing/false checks.
  // Acceptance separately requires the actual predicates for this registered journey.
  if (!scenarios.includes(scenario) || !validResult(value, scenario, browserEngine) || value.browserVersion === null) return false;
  const required = ['BROWSER_ENGINE', 'PUBLIC_DOM', 'CLEANUP_ATTEMPTED'];
  if (['locator', 'fill', 'assertion', 'timeout'].includes(scenario)) required.push('EXPECTED_ERROR');
  if (['unhandled', 'uncaught'].includes(scenario)) required.push('FATAL_HANDLER');
  return Object.values(value.checks).every(result => result === true) && required.every(id => value.checks[id] === true);
}
export function validSurfacePath(surface) {
  const root = temporaryRoot;
  const path = resolve(surface);
  const rel = relative(root, path);
  return isAbsolute(surface) && path.startsWith(root + sep) && !rel.includes(sep) && /^tunner-p2-008-(?:public-)?sensitive-[A-Za-z0-9]+$/.test(rel);
}
export async function scanSurface(surface, {readContents = true} = {}) {
  if (!validSurfacePath(surface) || (await lstat(surface)).isSymbolicLink() || !sameCanonicalPath(await realpath(surface), surface)) throw new Error('SURFACE_INVALID');
  let filesScanned = 0;
  let totalBytes = 0;
  let clean = true;
  const allowedRoot = /^(downloads|traces|playwright-artifacts-[A-Za-z0-9]+|playwright_chromiumdev_profile-[A-Za-z0-9]+)$/;
  const forbiddenArtifact = /(?:\.(?:png|jpe?g|webp|gif|zip|har|webm|mp4|dmp|heapsnapshot|cpuprofile)$|^error-context\.md$|^report\..*\.json$)/i;
  async function visit(directory, depth) {
    if (depth > 25) throw new Error('SURFACE_LIMIT');
    for (const entry of await readdir(directory, {withFileTypes: true})) {
      if (depth === 0 && !allowedRoot.test(entry.name)) clean = false;
      if (forbiddenArtifact.test(entry.name)) clean = false;
      const path = join(directory, entry.name);
      let stat;
      try { stat = await lstat(path); } catch (error) { if (error?.code === 'ENOENT') continue; throw new Error('SURFACE_READ_FAILED'); }
      if (stat.isSymbolicLink()) { clean = false; continue; }
      if (stat.isDirectory()) { await visit(path, depth + 1); continue; }
      if (!stat.isFile()) { clean = false; continue; }
      // Artifact/download roots are allowed only as empty framework-created directories.
      // Hashed or extensionless trace resources must be rejected just like PNG/ZIP files.
      if (!relative(surface, path).split(sep)[0].startsWith('playwright_chromiumdev_profile-')) clean = false;
      filesScanned++;
      totalBytes += stat.size;
      if (filesScanned > 10000 || stat.size > 64 * 1024 * 1024 || totalBytes > 256 * 1024 * 1024) throw new Error('SURFACE_LIMIT');
      if (!readContents || stat.size === 0) continue;
      let file;
      try {
        file = await open(path, 'r');
        const chunk = Buffer.alloc(65536);
        let tail = Buffer.alloc(0);
        for (;;) {
          const {bytesRead} = await file.read(chunk);
          if (!bytesRead) break;
          const bytes = Buffer.concat([tail, chunk.subarray(0, bytesRead)]);
          if (hasMarker(bytes)) clean = false;
          tail = bytes.subarray(Math.max(0, bytes.length - 256));
        }
      } catch (error) {
        // Chromium may delete its own transient files while the live scan runs.
        if (error?.code !== 'ENOENT') {
          const locked = ['EACCES', 'EPERM', 'EBUSY'].includes(error?.code);
          throw new Error(locked ? 'SURFACE_ACCESS_DENIED' : 'SURFACE_READ_FAILED');
        }
      } finally { if (file) await file.close(); }
    }
  }
  await visit(surface, 0);
  return {clean, filesScanned};
}

// Independent v3 registration. V2 public canaries retain their original contract.
export const realVersion = 3;
export const realJourneyStages = Object.freeze(['JOURNEY', 'JOURNEY_ENROLL_BEGIN', 'JOURNEY_ENROLL_DISCLOSURE', 'JOURNEY_ENROLL_RESULT_BINDING', 'JOURNEY_ENROLL_DUPLICATE', 'JOURNEY_ENROLL_DONE', 'JOURNEY_MUTATION_BEGIN', 'JOURNEY_MUTATION_READY', 'JOURNEY_PROOF_ENTRY', 'JOURNEY_PROOF_RESPONSE', 'JOURNEY_PROOF_RECOVERY', 'JOURNEY_PROOF_CONTINUE', 'JOURNEY_PROOF_CONTINUED', 'JOURNEY_MUTATION_CANCEL', 'JOURNEY_MUTATION_CANCELLED']);
export const realStages = Object.freeze(['START', 'ENVIRONMENT', 'LIBRARY', 'BROWSER', 'ORACLE', ...realJourneyStages, 'SURFACE_SCAN', 'CLEANUP', 'COMPLETE']);
export const realFailureCodes = Object.freeze(['NONE', 'INPUT_INVALID', 'BOUNDARY_ERROR', 'PREDICATE_FAILED', 'ORACLE_FAILED', 'SUPERVISOR_TIMEOUT', 'CLEANUP_FAILED', 'SURFACE_FAILED', 'SOURCE_INVALID']);
export const realCommonChecks = Object.freeze(['BROWSER_ENGINE', 'BFF_ISOLATED', 'REQUEST_BINDING', 'OPERATION_BINDING', 'DUPLICATE_COUNTS', 'ONE_TIME_DISCLOSURE', 'INACTIVE_BEFORE_ACTIVATION', 'NATIVE_ORACLE_IDENTITY', 'NATIVE_FIRST_CODE_ACTIVE', 'WRONG_CODE_REJECTED', 'REPLAY_REJECTED', 'RESULT_RECOVERY', 'COOKIE_FLAGS', 'NO_URL_OR_STORAGE_SECRET', 'UI_ASSET_TYPOGRAPHY', 'CLEANUP_ATTEMPTED']);
export const realJourneys = Object.freeze({
  TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1: Object.freeze([...realCommonChecks, 'DISCLOSURE_LOSS_NO_REVEAL', 'NATIVE_PASSWORD_MFA', 'NO_EARLY_SESSION', 'REPLAY_SEQUENCE_REJECTED', 'CONCURRENT_CODE_SINGLE_ACCEPT', 'SOURCE_FRESHNESS', 'PASSWORD_REGRESSION', 'DISABLE_PRESERVES_CONFIGURED_SESSIONS']),
  TOTP_OPERATION_PROOF_CAUSAL_REVOKE_V1: Object.freeze([...realCommonChecks, 'NATIVE_OPERATION_PROOF', 'OWNER_FAILURE_SAME_PROOF', 'SOURCE_FRESHNESS', 'EXPLICIT_CAUSAL_SESSION_EFFECT', 'UNRELATED_FACTOR_UNCHANGED', 'DISABLE_REJECTS_OLD_SOURCE', 'PASSWORD_REGRESSION']),
  TOTP_PASSKEY_MFA_RESET_V1: Object.freeze([...realCommonChecks, 'NATIVE_PASSKEY_MFA', 'NO_EARLY_SESSION', 'SOURCE_FRESHNESS', 'RESET_RETIRE_THEN_SEPARATE_ENROLLMENT', 'UNRELATED_FACTOR_UNCHANGED', 'PASSKEY_REGRESSION', 'PASSWORD_REGRESSION']),
});
export const realPublicFaults = Object.freeze(['oracle-roundtrip', 'oracle-input', 'oracle-exit', 'oracle-timeout', 'oracle-stderr', 'journey-entry', 'browser-error', 'browser-unhandled', 'browser-timeout', 'unexpected-output', 'invalid-result', 'cleanup']);
export const fixtureActions = Object.freeze(['READ_SAFE_COUNTS', 'DENY_OWNER_ONCE', 'DROP_DISCLOSURE_ONCE', 'DROP_ACTIVATION_ONCE', 'DROP_MFA_ONCE']);
export const fixtureCountKeys = Object.freeze(['ownerCommits', 'proofs', 'operations', 'sessions', 'outbox', 'codeAcceptances', 'mfaSessions']);
export function validFixtureRequest(value) { return exactKeys(value, ['version', 'type', 'requestId', 'action']) && value.version === 3 && value.type === 'fixtureRequest' && Number.isSafeInteger(value.requestId) && value.requestId >= 1 && value.requestId <= 16 && fixtureActions.includes(value.action); }
export function validFixtureResult(value) { return exactKeys(value, ['version', 'type', 'requestId', 'status', 'counts']) && value.version === 3 && value.type === 'fixtureResult' && Number.isSafeInteger(value.requestId) && value.requestId >= 1 && value.requestId <= 16 && ['OK', 'FAILED'].includes(value.status) && exactKeys(value.counts, fixtureCountKeys) && Object.values(value.counts).every(count => Number.isSafeInteger(count) && count >= 0 && count <= 1000); }
export const realBudgets = Object.freeze({oracleReady: 10000, oracleCompute: 2000, oracleClose: 3000, browserLaunch: 10000, action: 5000, journey: 180000, publicFault: 20000, cancelGrace: 2000});
export function validPublicProcess(value) { return exactKeys(value, ['version', 'type', 'kind', 'processId']) && value.version === 3 && value.type === 'publicProcess' && value.kind === 'ORACLE' && Number.isSafeInteger(value.processId) && value.processId > 0 && value.processId <= 2147483647; }
export const realExactKeys = exactKeys;
export function registeredRealJourney(id) { return typeof id === 'string' && Object.hasOwn(realJourneys, id); }
const originOnly = (value, protocol, host) => {
  if (typeof value !== 'string') return false;
  try { const url = new URL(value); return url.protocol === protocol && url.hostname === host && url.origin === value && /^\d{4,5}$/.test(url.port) && Number(url.port) >= 1024 && Number(url.port) <= 65535; } catch { return false; }
};
export function validRealControl(value) {
  return exactKeys(value, ['version', 'type', 'journeyId', 'browserEngine', 'origin', 'mailpitOrigin', 'sourceCommit', 'sourceCommitted', 'runId', 'oracleAssemblyPath', 'oracleNativeVersion', 'publicFault']) &&
    value.version === realVersion && value.type === 'run' && registeredRealJourney(value.journeyId) && value.browserEngine === 'chromium' &&
    originOnly(value.origin, 'https:', 'localhost') && originOnly(value.mailpitOrigin, 'http:', '127.0.0.1') &&
    typeof value.sourceCommit === 'string' && /^[a-f0-9]{40}$/.test(value.sourceCommit) && typeof value.sourceCommitted === 'boolean' && typeof value.runId === 'string' && /^[a-f0-9]{32}$/.test(value.runId) &&
    typeof value.oracleAssemblyPath === 'string' && isAbsolute(value.oracleAssemblyPath) && value.oracleNativeVersion === '10.0.13' &&
    (value.publicFault === null || realPublicFaults.includes(value.publicFault));
}
export function validRealChecks(checks, journeyId) {
  return registeredRealJourney(journeyId) && checks !== null && typeof checks === 'object' && !Array.isArray(checks) && Object.entries(checks).every(([key, value]) => realJourneys[journeyId].includes(key) && typeof value === 'boolean');
}
export function realPredicatesSatisfied(checks, journeyId) {
  return validRealChecks(checks, journeyId) && exactKeys(checks, realJourneys[journeyId]) && Object.values(checks).every(value => value === true);
}
export function validRealChildResult(value, control) {
  return exactKeys(value, ['version', 'journeyId', 'browserEngine', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'oracleRequests', 'oracleExited', 'surfaceClean', 'cleanupCompleted', 'publicFaultObserved']) &&
    value.version === realVersion && value.journeyId === control.journeyId && value.browserEngine === control.browserEngine &&
    (value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) &&
    ['REAL_JOURNEY_OK', 'BOUNDARY_FAILED'].includes(value.status) && realStages.includes(value.stage) && realFailureCodes.includes(value.failureCode) && validRealChecks(value.checks, control.journeyId) &&
    Number.isSafeInteger(value.oracleRequests) && value.oracleRequests >= 0 && value.oracleRequests <= 64 && ['oracleExited', 'surfaceClean', 'cleanupCompleted', 'publicFaultObserved'].every(key => typeof value[key] === 'boolean');
}
export function validRealReceipt(value) {
  if (!exactKeys(value, ['version', 'type', 'journeyId', 'browserEngine', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'oracleRequests', 'rawOutputRejected', 'processExited', 'oracleExited', 'surfaceClean', 'surfaceRemoved', 'cleanupCompleted', 'sourceCommit', 'sourceCommitted', 'accepted', 'publicFaultObserved'])) return false;
  const child = Object.fromEntries(['version', 'journeyId', 'browserEngine', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'oracleRequests', 'oracleExited', 'surfaceClean', 'cleanupCompleted', 'publicFaultObserved'].map(key => [key, value[key]]));
  return value.type === 'browserReceipt' && value.browserEngine === 'chromium' && validRealChildResult(child, value) &&
    (value.sourceCommit === null || typeof value.sourceCommit === 'string' && /^[a-f0-9]{40}$/.test(value.sourceCommit)) &&
    ['rawOutputRejected', 'processExited', 'surfaceRemoved', 'sourceCommitted', 'accepted'].every(key => typeof value[key] === 'boolean');
}
export function realReceiptAccepted(value) {
  return validRealReceipt(value) && value.status === 'REAL_JOURNEY_OK' && value.stage === 'COMPLETE' && value.failureCode === 'NONE' && value.browserVersion !== null &&
    realPredicatesSatisfied(value.checks, value.journeyId) && !value.publicFaultObserved && !value.rawOutputRejected && value.processExited && value.oracleExited && value.surfaceClean && value.surfaceRemoved && value.cleanupCompleted && value.sourceCommitted && value.sourceCommit !== null;
}
