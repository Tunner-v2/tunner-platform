import {lstat, readdir, open, realpath, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {isAbsolute, join, relative, resolve, sep} from 'node:path';
import {sameCanonicalPath} from '../../totp/sensitive/contract.mjs';
import {budgets, fixedError, validControl} from './contract.mjs';

const parent = resolve(await realpath(process.env.TUNNER_CANARY_OS_TEMP_ROOT ?? tmpdir()));
const prefix = /^tunner-p2-009-private-[A-Za-z0-9]+$/;
export function validSurfacePath(path) { const rel = typeof path === 'string' ? relative(parent, resolve(path)) : ''; return typeof path === 'string' && isAbsolute(path) && prefix.test(rel) && !rel.includes(sep); }
export async function ownedSurface(path) {
  if (!validSurfacePath(path)) throw fixedError('PATH_REJECTED');
  const stat = await lstat(path);
  if (!stat.isDirectory() || stat.isSymbolicLink() || !sameCanonicalPath(await realpath(path), path)) throw fixedError('PATH_REJECTED');
  return Object.freeze({path, dev: stat.dev, ino: stat.ino});
}
async function sameRoot(identity) { const current = await ownedSurface(identity.path); if (current.dev !== identity.dev || current.ino !== identity.ino) throw fixedError('PATH_REJECTED'); }
export async function removeOwnedSurface(identity) { await sameRoot(identity); await rm(identity.path, {recursive: true, force: true, maxRetries: 3, retryDelay: 100}); try { await lstat(identity.path); return false; } catch (error) { return error?.code === 'ENOENT'; } }
export async function downloadIdentity(root, path) {
  await sameRoot(root); const folder = join(root.path, 'downloads');
  if (typeof path !== 'string' || !isAbsolute(path)) throw fixedError('PATH_REJECTED');
  const rel = relative(folder, path); if (!rel || rel.includes(sep) || rel.startsWith('..') || isAbsolute(rel)) throw fixedError('PATH_REJECTED');
  const directory = await lstat(folder), stat = await lstat(path);
  if (!directory.isDirectory() || directory.isSymbolicLink() || !sameCanonicalPath(await realpath(folder), folder) || !stat.isFile() || stat.isSymbolicLink() || stat.nlink !== 1 || !sameCanonicalPath(await realpath(path), path)) throw fixedError('PATH_REJECTED');
  if (stat.size > budgets.maximumDownloadBytes) throw fixedError('SIZE_LIMIT');
  return Object.freeze({path, dev: stat.dev, ino: stat.ino});
}
export async function sameDownload(root, identity) { const current = await downloadIdentity(root, identity.path); return current.dev === identity.dev && current.ino === identity.ino; }
export function privateMarkers() {
  const values = []; let bytes = 0;
  return Object.freeze({
    remember(...strings) { for (const value of strings) { if (typeof value !== 'string' || !value.length || value.length > 4096) throw fixedError(); const item = Buffer.from(value); if (values.some(prior => prior.equals(item))) { item.fill(0); continue; } if (values.length >= 256 || bytes + item.length > 65536) { item.fill(0); throw fixedError('SIZE_LIMIT'); } bytes += item.length; values.push(item); } },
    contains(chunk) { return values.some(value => chunk.includes(value)); },
    get overlap() { return Math.max(1, ...values.map(value => value.length)); },
    clear() { for (const value of values) value.fill(0); values.length = 0; bytes = 0; },
  });
}
export async function inspectSurface(root, {admitted = null, markers = null, readContents = true, publicDiagnostic = null} = {}) {
  if (publicDiagnostic !== null && (!validControl(publicDiagnostic.control) || publicDiagnostic.control.mode !== 'public' || publicDiagnostic.control.publicFault !== 'simultaneous-extra' || publicDiagnostic.control.journeyId !== 'RECOVERY_GENERATE_TOTP_V1' || typeof publicDiagnostic.onFailure !== 'function')) throw fixedError('INPUT_INVALID');
  let clean = true, files = 0, bytes = 0;
  let reported = false;
  function report(category, path = root.path) {
    if (publicDiagnostic === null || reported) return;
    reported = true;
    let relativePath = relative(root.path, path).split(sep).join('/');
    if (!relativePath || relativePath === '..' || relativePath.startsWith('../') || isAbsolute(relativePath)) relativePath = '.';
    try { Promise.resolve(publicDiagnostic.onFailure(Object.freeze({category, relativePath: relativePath.replace(/[\u0000-\u001f\u007f]/g, '?').slice(0, 256)}))).catch(() => {}); } catch { /* Observation cannot change the strict scanner outcome. */ }
  }
  function dirty(category, path) { clean = false; report(category, path); }
  try { await sameRoot(root); } catch (error) { report('ROOT_IDENTITY'); throw error; }
  if (admitted && !await sameDownload(root, admitted)) { report('ADMITTED_IDENTITY', admitted.path); throw fixedError('PATH_REJECTED'); }
  const forbidden = /(?:\.(?:png|jpe?g|webp|gif|zip|har|webm|mp4|dmp|heapsnapshot|cpuprofile)$|^error-context\.md$|^report\..*\.json$)/i;
  async function visit(directory, depth) {
    if (depth > 25) { report('DEPTH_LIMIT', directory); throw fixedError('SURFACE_FAILED'); }
    let entries; try { entries = await readdir(directory, {withFileTypes: true}); } catch (error) { report('DIRECTORY_READ', directory); throw error; }
    for (const entry of entries) {
      const path = join(directory, entry.name); let stat; try { stat = await lstat(path); } catch (error) { if (error?.code === 'ENOENT') continue; report('STAT_FAILED', path); throw fixedError('SURFACE_FAILED'); }
      if (depth === 0 && !/^(downloads|traces|playwright-artifacts-[A-Za-z0-9]+|playwright_chromiumdev_profile-[A-Za-z0-9]+)$/.test(entry.name)) dirty('UNEXPECTED_TOP_ENTRY', path);
      if (forbidden.test(entry.name) || stat.isSymbolicLink()) { dirty(stat.isSymbolicLink() ? 'SYMLINK' : 'FORBIDDEN_NAME', path); continue; }
      if (stat.isDirectory()) { await visit(path, depth + 1); continue; }
      if (!stat.isFile() || stat.nlink !== 1) { dirty('FILE_TYPE_OR_LINK_COUNT', path); continue; }
      if (++files > 10000 || stat.size > 64 * 1024 * 1024 || (bytes += stat.size) > 256 * 1024 * 1024) { report('SCAN_LIMIT', path); throw fixedError('SURFACE_FAILED'); }
      const allowed = admitted && sameCanonicalPath(path, admitted.path) && stat.dev === admitted.dev && stat.ino === admitted.ino;
      if (!relative(root.path, path).split(sep)[0].startsWith('playwright_chromiumdev_profile-') && !allowed) dirty('UNADMITTED_FILE', path);
      if (!readContents || allowed || !markers || stat.size === 0) continue;
      let handle;
      try {
        handle = await open(path, 'r'); const opened = await handle.stat();
        if (!opened.isFile() || opened.nlink !== 1 || opened.dev !== stat.dev || opened.ino !== stat.ino || !sameCanonicalPath(await realpath(path), path)) { report('FILE_IDENTITY', path); throw fixedError('PATH_REJECTED'); }
        const chunk = Buffer.alloc(65536); let tail = Buffer.alloc(0);
        try { for (;;) { const {bytesRead} = await handle.read(chunk); if (!bytesRead) break; const part = Buffer.concat([tail, chunk.subarray(0, bytesRead)]); if (markers.contains(part)) dirty('MARKER_MATCH', path); tail.fill(0); tail = Buffer.from(part.subarray(Math.max(0, part.length - markers.overlap))); part.fill(0); } }
        finally { chunk.fill(0); tail.fill(0); }
      } catch (error) { if (error?.code !== 'ENOENT') { report('FILE_READ', path); throw fixedError('SURFACE_FAILED'); } }
      finally { try { await handle?.close(); } catch (error) { report('FILE_CLOSE', path); throw error; } }
    }
  }
  await visit(root.path, 0); try { await sameRoot(root); } catch (error) { report('ROOT_IDENTITY'); throw error; } return {clean, files};
}
