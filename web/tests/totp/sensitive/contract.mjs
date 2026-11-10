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
  return isAbsolute(surface) && path.startsWith(root + sep) && !rel.includes(sep) && /^tunner-p2-008-public-sensitive-[A-Za-z0-9]+$/.test(rel);
}
export async function scanSurface(surface, {readContents = true} = {}) {
  if (!validSurfacePath(surface) || (await lstat(surface)).isSymbolicLink() || resolve(await realpath(surface)).toLowerCase() !== resolve(surface).toLowerCase()) throw new Error('SURFACE_INVALID');
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
