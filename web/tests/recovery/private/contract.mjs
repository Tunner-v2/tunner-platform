import {isAbsolute} from 'node:path';

// Independent private registration. Public recovery and every TOTP contract stay unchanged.
export const version = 1;
export const publicMarker = 'TUNNER_PUBLIC_NONFUNCTIONAL_RECOVERY_PRIVATE_009';
export const publicBody = publicMarker + '\nNOT A RECOVERY CODE. PUBLIC FIXTURE ONLY.\n';
export const budgets = Object.freeze({initialize: 10000, launch: 10000, action: 5000, relay: 4000, ipc: 1000, cancel: 2000, close: 3000, publicScenario: 25000, publicMatrix: 180000, journey: 180000, maximumFrame: 16384, maximumDownloadBytes: 65536});
export const stages = Object.freeze(['START', 'ENVIRONMENT', 'LIBRARY', 'BROWSER', 'PUBLIC_ENTRY', 'ORACLE', 'JOURNEY', 'SURFACE', 'CLEANUP', 'COMPLETE']);
export const failures = Object.freeze(['NONE', 'INPUT_INVALID', 'SOURCE_INVALID', 'DEPENDENCY_NOT_READY', 'PROTOCOL_FAILED', 'BOUNDARY_ERROR', 'PREDICATE_FAILED', 'ADMISSION_REJECTED', 'PATH_REJECTED', 'SIZE_LIMIT', 'BYTES_MISMATCH', 'STREAM_FAILED', 'DELETE_FAILED', 'ACTION_FAILED', 'ORACLE_FAILED', 'SUPERVISOR_TIMEOUT', 'SURFACE_FAILED', 'CLEANUP_FAILED']);
const errors = new WeakMap();
export function fixedError(code = 'BOUNDARY_ERROR') { const error = new Error('RECOVERY_PRIVATE_BOUNDARY_FAILED'); errors.set(error, failures.includes(code) ? code : 'BOUNDARY_ERROR'); return error; }
export function failureOf(value) { return value && typeof value === 'object' ? errors.get(value) ?? 'BOUNDARY_ERROR' : 'BOUNDARY_ERROR'; }
export function requireTrue(value, code = 'PREDICATE_FAILED') { if (value !== true) throw fixedError(code); }
export async function bounded(work, milliseconds, code = 'BOUNDARY_ERROR') {
  let timer;
  try { return await Promise.race([work, new Promise((_, reject) => { timer = setTimeout(() => reject(fixedError(code)), milliseconds); })]); }
  finally { clearTimeout(timer); }
}
export const exactKeys = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const boolMap = (value, ids) => value !== null && typeof value === 'object' && !Array.isArray(value) && Object.entries(value).every(([key, flag]) => ids.includes(key) && typeof flag === 'boolean');
const complete = (value, ids) => boolMap(value, ids) && exactKeys(value, ids) && ids.every(id => value[id] === true);
const common = ['BFF_IDENTITY', 'ACTUAL_PROOF_METHOD', 'EXACT_OWNER_AND_STEP_UP_BINDING', 'CANONICAL_FIRST_DELIVERY', 'DUPLICATE_NO_REDISCLOSURE', 'NO_EARLY_DELIVERY', 'COOKIE_FLAGS', 'NO_SECRET_URL_OR_STORAGE', 'VOLATILE_CLEAR', 'REQUESTED_NOT_SAVED', 'STRICT_OWNED_SURFACE'];
export const journeys = Object.freeze({
  RECOVERY_GENERATE_PASSWORD_V1: Object.freeze([...common, 'ONE_REQUESTED_DOWNLOAD', 'CLOSE_NO_REVEAL']),
  RECOVERY_REPLACE_PASSWORD_V1: Object.freeze([...common, 'EXACT_SUBSET_REPLACEMENT', 'BODY_LOSS_NO_REDISCLOSURE']),
  RECOVERY_GENERATE_PASSKEY_V1: Object.freeze([...common, 'NATIVE_PASSKEY_PROOF', 'OWNER_FAILURE_SAME_PROOF']),
  RECOVERY_REPLACE_PASSKEY_V1: Object.freeze([...common, 'NATIVE_PASSKEY_PROOF', 'SOURCE_FRESHNESS', 'NO_SESSION_MUTATION']),
  RECOVERY_GENERATE_TOTP_V1: Object.freeze([...common, 'NATIVE_TOTP_PROOF', 'OWNER_FAILURE_SAME_PROOF', 'PAGEHIDE_NO_REVEAL']),
  RECOVERY_REPLACE_TOTP_V1: Object.freeze([...common, 'NATIVE_TOTP_PROOF', 'CURRENT_PUBLICATION', 'NO_SESSION_MUTATION']),
});
export const childChecks = Object.freeze(['BROWSER_ENGINE', 'PUBLIC_ENTRY', 'ERROR_CONTAINED', 'DOWNLOAD_ADMITTED', 'DOWNLOAD_REMOVED', 'SURFACE_CHECKED', ...new Set(Object.values(journeys).flat())]);
export const supervisorChecks = Object.freeze(['SOURCE_CURRENT', 'PROTOCOL_VALID', 'OUTPUT_CONTAINED', 'CHILD_CLOSED', 'ORACLE_CLOSED', 'CHILD_SURFACES_CLOSED', 'SURFACE_REMOVED']);
export const coordinatorChecks = Object.freeze(['SOURCE_BUILD_CURRENT', 'OWNED_DATABASE', 'OWNED_MAILPIT', 'EPHEMERAL_DP', 'CURRENT_MIGRATIONS', 'SAFE_PERSISTENCE', 'SAFE_OUTBOX', 'HOST_STOPPED', 'RESOURCES_REMOVED', 'BORROWED_POSTGRES_PRESERVED']);
export const publicFaults = Object.freeze(['happy', 'unarmed', 'duplicate', 'wrong-page', 'wrong-bytes', 'oversize', 'outside-identity', 'stream-error', 'delete-error', 'unexpected-output', 'invalid-result', 'action-timeout', 'child-timeout', 'unexpected-artifact', 'fatal-rejection', 'journey-entry', 'missing-predicate', 'cross-component', 'cleanup', 'oracle-roundtrip', 'oracle-input', 'oracle-exit', 'oracle-timeout', 'oracle-stderr']);
export const oracleFaults = Object.freeze(publicFaults.filter(value => value.startsWith('oracle-')));
export const registeredJourney = value => typeof value === 'string' && Object.hasOwn(journeys, value);
const origin = (value, protocol, hostname) => {
  if (typeof value !== 'string') return false;
  try { const parsed = new URL(value); return parsed.protocol === protocol && parsed.hostname === hostname && parsed.origin === value && /^\d{4,5}$/.test(parsed.port) && Number(parsed.port) >= 1024 && Number(parsed.port) <= 65535; } catch { return false; }
};
export function validControl(value) {
  return exactKeys(value, ['version', 'type', 'mode', 'journeyId', 'publicFault', 'sourceCommit', 'sourceCommitted', 'origin', 'mailpitOrigin', 'runId']) &&
    value.version === version && value.type === 'runRecoveryPrivate' && ['public', 'live'].includes(value.mode) && registeredJourney(value.journeyId) &&
    typeof value.sourceCommit === 'string' && /^[a-f0-9]{40}$/.test(value.sourceCommit) && typeof value.sourceCommitted === 'boolean' &&
    typeof value.runId === 'string' && /^[a-f0-9]{32}$/.test(value.runId) &&
    (value.mode === 'public' ? publicFaults.includes(value.publicFault) && value.origin === null && value.mailpitOrigin === null : value.publicFault === null && value.sourceCommitted && origin(value.origin, 'https:', 'localhost') && origin(value.mailpitOrigin, 'http:', '127.0.0.1'));
}
export function validInitialize(value) { return exactKeys(value, ['version', 'type', 'control', 'surface']) && value.version === version && value.type === 'initializeRecoveryPrivate' && validControl(value.control) && typeof value.surface === 'string' && isAbsolute(value.surface); }
export function validChildResult(value, control) {
  return exactKeys(value, ['version', 'type', 'journeyId', 'mode', 'publicFault', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'publicFaultObserved', 'contextClosed', 'browserClosed', 'oracleExited', 'oracleGraceful', 'surfaceClean', 'cleanupCompleted']) &&
    value.version === version && value.type === 'recoveryPrivateChildResult' && value.journeyId === control.journeyId && value.mode === control.mode && value.publicFault === control.publicFault &&
    (value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) &&
    ['JOURNEY_OK', 'PUBLIC_BOUNDARY_OK', 'BOUNDARY_FAILED'].includes(value.status) && (value.status !== 'JOURNEY_OK' || control.mode === 'live') && (value.status !== 'PUBLIC_BOUNDARY_OK' || control.mode === 'public') &&
    (value.status === 'BOUNDARY_FAILED' ? value.failureCode !== 'NONE' : value.failureCode === 'NONE') && stages.includes(value.stage) && failures.includes(value.failureCode) && boolMap(value.checks, childChecks) &&
    ['publicFaultObserved', 'contextClosed', 'browserClosed', 'oracleExited', 'oracleGraceful', 'surfaceClean', 'cleanupCompleted'].every(key => typeof value[key] === 'boolean');
}
export function childAccepted(value, control) {
  return validChildResult(value, control) && control.mode === 'live' && value.status === 'JOURNEY_OK' && value.stage === 'COMPLETE' && value.failureCode === 'NONE' && value.browserVersion !== null &&
    !value.publicFaultObserved && complete(value.checks, ['BROWSER_ENGINE', 'SURFACE_CHECKED', ...journeys[control.journeyId]]) && value.contextClosed && value.browserClosed && value.oracleExited && value.oracleGraceful && value.surfaceClean && value.cleanupCompleted;
}
export const validSupervisorChecks = value => boolMap(value, supervisorChecks);
export function realAccepted(child, control, supervisor, coordinator) { return childAccepted(child, control) && complete(supervisor, supervisorChecks) && complete(coordinator, coordinatorChecks); }
export function validReceipt(value) {
  const keys = ['version', 'type', 'journeyId', 'mode', 'publicFault', 'sourceCommit', 'sourceCommitted', 'sourceFingerprint', 'browserVersion', 'status', 'stage', 'failureCode', 'checks', 'supervisor', 'childResultAccepted', 'protocolRejected', 'rawOutputRejected', 'timedOut', 'cleanupForced', 'exitCode', 'contextClosed', 'browserClosed', 'oracleExited', 'oracleGraceful', 'surfaceClean', 'surfaceRemoved', 'cleanupCompleted', 'publicFaultObserved', 'publicOwnership', 'accepted'];
  const flags = ['sourceCommitted', 'childResultAccepted', 'protocolRejected', 'rawOutputRejected', 'timedOut', 'cleanupForced', 'contextClosed', 'browserClosed', 'oracleExited', 'oracleGraceful', 'surfaceClean', 'surfaceRemoved', 'cleanupCompleted', 'publicFaultObserved', 'accepted'];
  if (!exactKeys(value, keys) || value.version !== version || value.type !== 'recoveryPrivateBrowserReceipt' || !registeredJourney(value.journeyId) || !['public', 'live'].includes(value.mode) || !flags.every(key => typeof value[key] === 'boolean') ||
      !boolMap(value.checks, childChecks) || !completeShape(value.supervisor, supervisorChecks) || !['NOT_ACCEPTED', 'JOURNEY_OK', 'PUBLIC_BOUNDARY_OK', 'BOUNDARY_FAILED'].includes(value.status) || !stages.includes(value.stage) || !failures.includes(value.failureCode) ||
      !(value.sourceCommit === null || typeof value.sourceCommit === 'string' && /^[a-f0-9]{40}$/.test(value.sourceCommit)) || !(value.sourceFingerprint === null || typeof value.sourceFingerprint === 'string' && /^[a-f0-9]{64}$/.test(value.sourceFingerprint)) ||
      !(value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) || !(value.exitCode === null || Number.isSafeInteger(value.exitCode) && value.exitCode >= -2147483648 && value.exitCode <= 255)) return false;
  if (value.mode === 'live') return value.publicFault === null && value.publicOwnership === null;
  const owner = value.publicOwnership, pid = value => value === null || Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
  return publicFaults.includes(value.publicFault) && value.accepted === false && exactKeys(owner, ['surface', 'childPid', 'oraclePid']) && (owner.surface === null || typeof owner.surface === 'string' && isAbsolute(owner.surface)) && pid(owner.childPid) && pid(owner.oraclePid);
}
const completeShape = (value, ids) => boolMap(value, ids) && exactKeys(value, ids);
export function validCleanup(value, control) { return exactKeys(value, ['version', 'type', 'journeyId', 'contextClosed', 'browserClosed', 'oracleExited']) && value.version === version && value.type === 'recoveryPrivateCleanup' && value.journeyId === control.journeyId && value.contextClosed === true && value.browserClosed === true && value.oracleExited === true; }
export function observedMap(ids) {
  const values = Object.create(null);
  return Object.freeze({
    observe(id, flag) { if (!ids.includes(id) || typeof flag !== 'boolean') throw fixedError('PREDICATE_FAILED'); values[id] = (values[id] ?? true) && flag; },
    snapshot() { return Object.fromEntries(ids.map(id => [id, values[id] === true])); },
    finish(check) { let failed = false; for (const id of ids) { const flag = values[id] === true; try { check(id, flag); } catch { failed = true; } failed ||= !flag; } if (failed) throw fixedError('PREDICATE_FAILED'); },
  });
}
