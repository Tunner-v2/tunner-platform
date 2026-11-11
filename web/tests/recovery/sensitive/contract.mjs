import {lstat, readdir, realpath, open, rm} from 'node:fs/promises';
import {isAbsolute, join, relative, resolve, sep} from 'node:path';
import {tmpdir} from 'node:os';
import {sameCanonicalPath} from '../../totp/sensitive/contract.mjs';

// Public fixtures only. This module registers no real recovery journey or secret transport.
export const version = 1;
export const publicMarker = 'TUNNER_PUBLIC_NONFUNCTIONAL_RECOVERY_DOWNLOAD_CANARY_009';
export const publicBody = publicMarker + '\nNOT A RECOVERY CODE. PUBLIC FIXTURE ONLY.\n';
export const scenarios = Object.freeze(['happy', 'unarmed', 'duplicate', 'wrong-page', 'wrong-bytes', 'oversize', 'outside-identity', 'stream-error', 'delete-error', 'unexpected-output', 'invalid-result', 'action-timeout', 'child-timeout', 'unexpected-artifact', 'fatal-rejection']);
export const budgets = Object.freeze({launch: 10000, action: 3000, scenario: 25000, cancel: 5000, close: 3000, ipc: 1000, matrix: 180000, maximumDownloadBytes: 65536, maximumFrameBytes: 2048});
export const predicateIds = Object.freeze(['BROWSER_ENGINE', 'PUBLIC_DOM', 'DOWNLOAD_EVENT', 'ADMISSION_ACCEPTED', 'PATH_OWNED', 'BYTES_MATCH', 'DOWNLOAD_REMOVED', 'FAULT_OBSERVED', 'CLEANUP_ATTEMPTED', 'CONTEXT_CLOSED', 'BROWSER_CLOSED', 'SURFACE_CLEAN', 'SURFACE_REJECTED']);
export const failures = Object.freeze(['NONE', 'INPUT_INVALID', 'BOUNDARY_ERROR', 'ADMISSION_REJECTED', 'BYTES_MISMATCH', 'SIZE_LIMIT', 'PATH_REJECTED', 'STREAM_FAILED', 'DELETE_FAILED', 'RAW_OUTPUT_INJECTED', 'ACTION_FAILED', 'SUPERVISOR_TIMEOUT', 'SURFACE_FAILED', 'FATAL_FAILED', 'CLEANUP_FAILED']);
const expectedFailures = Object.freeze({unarmed: 'ADMISSION_REJECTED', duplicate: 'ADMISSION_REJECTED', 'wrong-page': 'ADMISSION_REJECTED', 'wrong-bytes': 'BYTES_MISMATCH', oversize: 'SIZE_LIMIT', 'outside-identity': 'PATH_REJECTED', 'stream-error': 'STREAM_FAILED', 'delete-error': 'DELETE_FAILED', 'unexpected-output': 'RAW_OUTPUT_INJECTED', 'action-timeout': 'ACTION_FAILED', 'child-timeout': 'SUPERVISOR_TIMEOUT', 'unexpected-artifact': 'SURFACE_FAILED', 'fatal-rejection': 'FATAL_FAILED'});
export const exactKeys = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const fixedFailures = new WeakMap();
export function fixedError(code) { const safe = failures.includes(code) ? code : 'BOUNDARY_ERROR'; const error = new Error(safe); fixedFailures.set(error, safe); return error; }
export function failureOf(error) { return error !== null && typeof error === 'object' ? fixedFailures.get(error) ?? 'BOUNDARY_ERROR' : 'BOUNDARY_ERROR'; }
export async function bounded(promise, milliseconds, code = 'BOUNDARY_ERROR') {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(fixedError(code)), milliseconds); })]); }
  finally { clearTimeout(timer); }
}
export function validSurfacePath(surface) {
  if (typeof surface !== 'string' || !isAbsolute(surface)) return false;
  const root = resolve(process.env.TUNNER_CANARY_OS_TEMP_ROOT ?? tmpdir());
  const part = relative(root, resolve(surface));
  return !part.includes(sep) && /^tunner-p2-009-public-sensitive-[A-Za-z0-9]+$/.test(part);
}
export async function requireOwnedSurface(surface) {
  if (!validSurfacePath(surface)) throw fixedError('PATH_REJECTED');
  const stat = await lstat(surface);
  if (!stat.isDirectory() || stat.isSymbolicLink() || !sameCanonicalPath(await realpath(surface), surface)) throw fixedError('PATH_REJECTED');
}
export async function removeOwnedSurface(surface) {
  await requireOwnedSurface(surface);
  await rm(surface, {recursive: true, force: true, maxRetries: 3, retryDelay: 100});
  try { await lstat(surface); return false; } catch (error) { return error?.code === 'ENOENT'; }
}
export async function downloadIdentity(surface, candidate) {
  await requireOwnedSurface(surface);
  const folder = join(surface, 'downloads');
  if (typeof candidate !== 'string' || !isAbsolute(candidate) || relative(folder, candidate).includes(sep) || relative(folder, candidate).startsWith('..') || relative(folder, candidate) === '') throw fixedError('PATH_REJECTED');
  const directory = await lstat(folder);
  const stat = await lstat(candidate);
  if (!directory.isDirectory() || directory.isSymbolicLink() || !sameCanonicalPath(await realpath(folder), folder) || !sameCanonicalPath(await realpath(candidate), candidate) || !stat.isFile() || stat.isSymbolicLink() || stat.nlink !== 1) throw fixedError('PATH_REJECTED');
  if (stat.size > budgets.maximumDownloadBytes) throw fixedError('SIZE_LIMIT');
  return {path: candidate, dev: stat.dev, ino: stat.ino};
}
export async function sameDownload(surface, identity) {
  const actual = await downloadIdentity(surface, identity.path);
  return actual.dev === identity.dev && actual.ino === identity.ino;
}
export function createAdmission() {
  let state = 'DISARMED'; let page; let events = 0;
  return Object.freeze({
    arm(owner) { if (state !== 'DISARMED' || events !== 0 || owner === null || owner === undefined) throw fixedError('ADMISSION_REJECTED'); page = owner; state = 'ARMED'; },
    accept(owner) { events++; if (state !== 'ARMED' || owner !== page || events !== 1) { state = 'REJECTED'; return false; } state = 'ACTIVE'; return true; },
    settle() { if (state !== 'ACTIVE') throw fixedError('ADMISSION_REJECTED'); state = 'SETTLED'; page = null; },
    close() { state = 'CLOSED'; page = null; },
    get events() { return events; },
    get active() { return state === 'ACTIVE'; },
  });
}
export async function inspectSurface(surface, {admitted = null, readContents = true} = {}) {
  await requireOwnedSurface(surface);
  if (admitted && !await sameDownload(surface, admitted)) throw fixedError('PATH_REJECTED');
  let clean = true; let files = 0; let bytes = 0;
  const marker = Buffer.from(publicMarker);
  async function visit(directory, depth) {
    if (depth > 25) throw fixedError('SURFACE_FAILED');
    for (const entry of await readdir(directory, {withFileTypes: true})) {
      const path = join(directory, entry.name); const stat = await lstat(path);
      const first = relative(surface, path).split(sep)[0];
      if (depth === 0 && !/^(downloads|traces|playwright-artifacts-[A-Za-z0-9]+|playwright_chromiumdev_profile-[A-Za-z0-9]+)$/.test(entry.name)) clean = false;
      if (stat.isSymbolicLink()) { clean = false; continue; }
      if (stat.isDirectory()) { await visit(path, depth + 1); continue; }
      if (!stat.isFile() || stat.nlink !== 1) { clean = false; continue; }
      if (++files > 10000 || (bytes += stat.size) > 256 * 1024 * 1024 || stat.size > 64 * 1024 * 1024) throw fixedError('SURFACE_FAILED');
      const allowedFile = admitted && path === admitted.path && stat.dev === admitted.dev && stat.ino === admitted.ino;
      if (!first.startsWith('playwright_chromiumdev_profile-') && !allowedFile) clean = false;
      if (/(?:\.(?:png|jpe?g|webp|gif|zip|har|webm|mp4|dmp|heapsnapshot|cpuprofile)$|^error-context\.md$|^report\..*\.json$)/i.test(entry.name)) clean = false;
      if (!readContents || allowedFile) continue;
      const handle = await open(path, 'r');
      try {
        const chunk = Buffer.alloc(65536); let tail = Buffer.alloc(0);
        for (;;) { const result = await handle.read(chunk); if (!result.bytesRead) break; const part = Buffer.concat([tail, chunk.subarray(0, result.bytesRead)]); if (part.includes(marker)) clean = false; tail = Buffer.from(part.subarray(Math.max(0, part.length - marker.length))); }
      } finally { await handle.close(); }
    }
  }
  await visit(surface, 0);
  return {clean, files};
}
export function validInitialize(value) {
  return exactKeys(value, ['version', 'type', 'scenario', 'surface']) && value.version === version && value.type === 'initialize' && scenarios.includes(value.scenario) && validSurfacePath(value.surface);
}
export function validChildResult(value, scenario) {
  return scenarios.includes(scenario) && exactKeys(value, ['version', 'type', 'scenario', 'browserVersion', 'status', 'failureCode', 'checks', 'downloadEvents', 'faultObserved', 'cleanupCompleted']) && value.version === version && value.type === 'publicRecoveryResult' && value.scenario === scenario && (value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) && ['PUBLIC_BOUNDARY_OK', 'BOUNDARY_FAILED'].includes(value.status) && failures.includes(value.failureCode) && Number.isSafeInteger(value.downloadEvents) && value.downloadEvents >= 0 && value.downloadEvents <= 4 && typeof value.faultObserved === 'boolean' && typeof value.cleanupCompleted === 'boolean' && value.checks !== null && typeof value.checks === 'object' && !Array.isArray(value.checks) && Object.entries(value.checks).every(([key, val]) => predicateIds.includes(key) && typeof val === 'boolean');
}
export function requiredChecks(scenario) {
  const ids = ['BROWSER_ENGINE', 'PUBLIC_DOM', 'CLEANUP_ATTEMPTED', 'CONTEXT_CLOSED', 'BROWSER_CLOSED'];
  ids.push(scenario === 'unexpected-artifact' ? 'SURFACE_REJECTED' : 'SURFACE_CLEAN');
  if (scenario === 'happy' || scenario === 'invalid-result') ids.push('DOWNLOAD_EVENT', 'ADMISSION_ACCEPTED', 'PATH_OWNED', 'BYTES_MATCH', 'DOWNLOAD_REMOVED');
  else ids.push('FAULT_OBSERVED');
  if (['unarmed', 'duplicate', 'wrong-page', 'wrong-bytes', 'oversize', 'outside-identity', 'stream-error', 'delete-error'].includes(scenario)) ids.push('DOWNLOAD_EVENT', 'DOWNLOAD_REMOVED');
  if (['duplicate', 'wrong-bytes', 'oversize', 'outside-identity', 'stream-error', 'delete-error', 'unexpected-output', 'unexpected-artifact'].includes(scenario)) ids.push('DOWNLOAD_EVENT', 'ADMISSION_ACCEPTED', 'DOWNLOAD_REMOVED');
  if (['duplicate', 'wrong-bytes', 'stream-error', 'delete-error', 'unexpected-output', 'unexpected-artifact'].includes(scenario)) ids.push('PATH_OWNED');
  if (['duplicate', 'delete-error', 'unexpected-output', 'unexpected-artifact'].includes(scenario)) ids.push('BYTES_MATCH');
  return [...new Set(ids)];
}
export function canaryPassed(parent, result) {
  if (!parent.childClosed || !parent.browserClosedVerified || !parent.surfaceRemoved || !parent.sourceCommitted) return false;
  if (parent.scenario === 'invalid-result') return parent.protocolRejected && !result && parent.exitCode === 0 && !parent.outputRejected && parent.surfaceClean;
  if (parent.protocolRejected || !validChildResult(result, parent.scenario) || result.browserVersion === null || !result.cleanupCompleted || requiredChecks(parent.scenario).some(id => result.checks[id] !== true)) return false;
  const expectedEvents = ['action-timeout', 'child-timeout', 'fatal-rejection'].includes(parent.scenario) ? 0 : parent.scenario === 'duplicate' ? 2 : 1;
  if (result.downloadEvents !== expectedEvents) return false;
  if (parent.scenario === 'wrong-bytes' && result.checks.BYTES_MATCH !== false) return false;
  if (Object.entries(result.checks).some(([id, value]) => !value && !(parent.scenario === 'wrong-bytes' && id === 'BYTES_MATCH') && !(parent.scenario === 'unexpected-artifact' && id === 'SURFACE_CLEAN'))) return false;
  if (parent.scenario === 'unexpected-artifact') { if (parent.surfaceClean) return false; }
  else if (!parent.surfaceClean) return false;
  if (parent.scenario === 'unexpected-output' ? !parent.outputRejected : parent.outputRejected) return false;
  if (parent.scenario === 'happy') return !parent.timedOut && parent.exitCode === 0 && result.status === 'PUBLIC_BOUNDARY_OK' && result.failureCode === 'NONE' && !result.faultObserved && Object.values(result.checks).every(value => value === true);
  return parent.exitCode === 1 && (parent.scenario === 'child-timeout' ? parent.timedOut : !parent.timedOut) && result.status === 'BOUNDARY_FAILED' && result.failureCode === expectedFailures[parent.scenario] && result.faultObserved;
}
