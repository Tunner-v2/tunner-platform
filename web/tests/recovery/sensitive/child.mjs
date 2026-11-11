import {createRequire} from 'node:module';
import {dirname, join, resolve} from 'node:path';
import {lstat, writeFile, readdir} from 'node:fs/promises';
import {version, publicMarker, publicBody, budgets, fixedError, failureOf, bounded, validInitialize, exactKeys, createAdmission, downloadIdentity, sameDownload, inspectSurface} from './contract.mjs';

const abort = new AbortController();
const checks = Object.create(null); const admission = createAdmission(); const downloads = [];
let control; let browser; let context; let page; let browserVersion = null; let faultObserved = false; let fatal = false; let failureCode = 'NONE';
let contextClosed = false; let browserClosed = false; let cleanupCompleted = false; let resultSent = false;
const send = value => bounded(new Promise(resolveSend => { try { if (process.connected) process.send(value, () => resolveSend()); else resolveSend(); } catch { resolveSend(); } }), budgets.ipc).catch(() => {});
const fatalHandler = () => { fatal = true; if (control?.scenario === 'fatal-rejection') faultObserved = true; abort.abort(); };
process.on('uncaughtException', fatalHandler); process.on('unhandledRejection', fatalHandler);
function canceled() { if (abort.signal.aborted) throw fixedError(fatal ? 'FATAL_FAILED' : 'SUPERVISOR_TIMEOUT'); }
const initial = new Promise((resolveInitial, reject) => {
  const timer = setTimeout(() => reject(fixedError('INPUT_INVALID')), 5000);
  process.on('message', value => {
    if (exactKeys(value, ['version', 'type']) && value.version === version && value.type === 'cancel') { abort.abort(); return; }
    if (!control && validInitialize(value)) { control = value; clearTimeout(timer); resolveInitial(); return; }
    fatalHandler();
  });
});
let nextDownload;
function wire(owner) {
  owner.on('download', download => {
    downloads.push(download); checks.DOWNLOAD_EVENT = true;
    const accepted = admission.accept(owner);
    if (accepted) checks.ADMISSION_ACCEPTED = true;
    nextDownload?.({download, accepted}); nextDownload = null;
  });
}
async function request(owner) {
  canceled();
  const event = new Promise(resolveEvent => { nextDownload = resolveEvent; });
  await owner.locator('#download').click({timeout: budgets.action});
  const value = await bounded(event, budgets.action); canceled();
  if (!value.accepted) throw fixedError('ADMISSION_REJECTED');
  return value.download;
}
async function fixture(owner, body) {
  await owner.setContent('<main><h1>Public recovery boundary</h1><p id="public"></p><button id="download">Request public fixture</button></main>');
  await owner.evaluate(({marker, contents}) => {
    document.querySelector('#public').textContent = marker;
    document.querySelector('#download').addEventListener('click', () => {
      const capability = URL.createObjectURL(new Blob([contents], {type: 'text/plain'}));
      const anchor = document.createElement('a'); anchor.href = capability; anchor.download = 'public-nonfunctional-fixture.txt'; anchor.click();
      setTimeout(() => URL.revokeObjectURL(capability), 1000);
    });
  }, {marker: publicMarker, contents: body});
}
async function privateBytes(download, identity) {
  const stream = await bounded(download.createReadStream(), budgets.action);
  if (!stream) throw fixedError('STREAM_FAILED');
  const chunks = []; let size = 0;
  try {
    await bounded((async () => {
      for await (const part of stream) {
        canceled(); size += part.length;
        if (size > budgets.maximumDownloadBytes) throw fixedError('SIZE_LIMIT');
        if (control.scenario === 'stream-error') throw new Error(publicMarker);
        chunks.push(Buffer.from(part));
      }
    })(), budgets.action, 'STREAM_FAILED');
    if (!await sameDownload(control.surface, identity)) throw fixedError('PATH_REJECTED');
    const bytes = Buffer.concat(chunks); const equal = bytes.equals(Buffer.from(publicBody)); bytes.fill(0);
    checks.BYTES_MATCH = equal;
    if (!equal) throw fixedError('BYTES_MISMATCH');
  } catch (error) { throw fixedError(failureOf(error) === 'BOUNDARY_ERROR' ? 'STREAM_FAILED' : failureOf(error)); }
  finally { stream.destroy(); for (const chunk of chunks) chunk.fill(0); }
}
try {
  await initial;
  if (process.argv.length !== 3 || process.argv[2] !== '--public-recovery-boundary-v1') throw fixedError('INPUT_INVALID');
  canceled();
  const require = createRequire(import.meta.url); const from = dirname(require.resolve('@playwright/test/package.json'));
  const packagePath = require.resolve('playwright/package.json', {paths: [from]});
  if (require(packagePath).version !== '1.62.1') throw fixedError('BOUNDARY_ERROR');
  const {chromium} = require(require.resolve('playwright', {paths: [from]}));
  const core = require.resolve('playwright-core/package.json', {paths: [dirname(packagePath)]});
  browser = await chromium.launch({headless: true, timeout: budgets.launch, downloadsPath: join(control.surface, 'downloads'), tracesDir: join(control.surface, 'traces')});
  browserVersion = browser.version();
  checks.BROWSER_ENGINE = browserVersion === require(join(dirname(core), 'browsers.json')).browsers.find(value => value.name === 'chromium-headless-shell')?.browserVersion;
  if (!checks.BROWSER_ENGINE) throw fixedError('BOUNDARY_ERROR');
  context = await browser.newContext({acceptDownloads: true, serviceWorkers: 'block'});
  context.on('page', wire); page = await context.newPage(); page.setDefaultTimeout(budgets.action);
  const body = control.scenario === 'wrong-bytes' ? 'PUBLIC WRONG NONFUNCTIONAL CONTENT' : control.scenario === 'oversize' ? publicBody.repeat(Math.ceil((budgets.maximumDownloadBytes + 1) / publicBody.length)) : publicBody;
  await fixture(page, body);
  checks.PUBLIC_DOM = await page.locator('#public').textContent() === publicMarker;
  if (!checks.PUBLIC_DOM) throw fixedError('BOUNDARY_ERROR');
  if (control.scenario === 'child-timeout') { await send({version, type: 'publicReady', scenario: control.scenario}); await new Promise(resolveAbort => { if (abort.signal.aborted) resolveAbort(); else abort.signal.addEventListener('abort', resolveAbort, {once: true}); }); canceled(); }
  if (control.scenario === 'action-timeout') {
    try { await page.locator('#public-does-not-exist').click({timeout: 200}); } catch { faultObserved = true; throw fixedError('ACTION_FAILED'); }
    throw fixedError('BOUNDARY_ERROR');
  }
  if (control.scenario === 'fatal-rejection') { Promise.reject(new Error(publicMarker)); await new Promise(resolveTurn => setTimeout(resolveTurn, 20)); canceled(); throw fixedError('BOUNDARY_ERROR'); }
  if (control.scenario !== 'unarmed') admission.arm(page);
  let owner = page;
  if (control.scenario === 'wrong-page') { owner = await context.newPage(); await fixture(owner, publicBody); }
  const download = await request(owner);
  const path = await bounded(download.path(), budgets.action);
  if (control.scenario === 'outside-identity') {
    try { await downloadIdentity(control.surface, resolve(control.surface, '..', 'not-an-owned-download')); } catch { faultObserved = true; throw fixedError('PATH_REJECTED'); }
    throw fixedError('BOUNDARY_ERROR');
  }
  const identity = await downloadIdentity(control.surface, path); checks.PATH_OWNED = true;
  const live = await inspectSurface(control.surface, {admitted: identity, readContents: false});
  if (!live.clean) throw fixedError('SURFACE_FAILED');
  await privateBytes(download, identity);
  try { if (control.scenario === 'delete-error') throw new Error(publicMarker); await bounded(download.delete(), budgets.action, 'DELETE_FAILED'); }
  catch { throw fixedError('DELETE_FAILED'); }
  admission.settle();
  try { await lstat(identity.path); throw fixedError('DELETE_FAILED'); } catch (error) { if (error?.code !== 'ENOENT') throw error; }
  checks.DOWNLOAD_REMOVED = true;
  if (control.scenario === 'duplicate') await request(page);
  if (control.scenario === 'unexpected-artifact') await writeFile(join(control.surface, 'unexpected-public-artifact.txt'), publicMarker, {flag: 'wx'});
  if (control.scenario === 'unexpected-output') { process.stderr.write(publicMarker); faultObserved = true; throw fixedError('RAW_OUTPUT_INJECTED'); }
} catch (error) {
  failureCode = failureOf(error);
  const expected = {unarmed: 'ADMISSION_REJECTED', duplicate: 'ADMISSION_REJECTED', 'wrong-page': 'ADMISSION_REJECTED', 'wrong-bytes': 'BYTES_MISMATCH', oversize: 'SIZE_LIMIT', 'stream-error': 'STREAM_FAILED', 'delete-error': 'DELETE_FAILED', 'child-timeout': 'SUPERVISOR_TIMEOUT'};
  if (expected[control?.scenario] === failureCode) faultObserved = true;
} finally {
  checks.CLEANUP_ATTEMPTED = true; admission.close(); nextDownload = null;
  for (const download of downloads) { try { await bounded(download.cancel(), budgets.close); } catch {} try { await bounded(download.delete(), budgets.close); } catch {} }
  try { if (context) { await bounded(context.close(), budgets.close); contextClosed = true; } } catch {}
  try { if (browser) { await bounded(browser.close(), budgets.close); browserClosed = true; } } catch {}
  checks.CONTEXT_CLOSED = contextClosed; checks.BROWSER_CLOSED = browserClosed;
  if (control && downloads.length) {
    try {
      const folder = join(control.surface, 'downloads');
      checks.DOWNLOAD_REMOVED = (await readdir(folder)).length === 0;
    } catch (error) { checks.DOWNLOAD_REMOVED = error?.code === 'ENOENT'; }
  }
  let surfaceClean = false;
  try { surfaceClean = (await inspectSurface(control.surface)).clean; } catch {}
  checks.SURFACE_CLEAN = surfaceClean;
  if (control?.scenario === 'unexpected-artifact' && !surfaceClean) { checks.SURFACE_REJECTED = true; faultObserved = true; failureCode = 'SURFACE_FAILED'; }
  else if (!surfaceClean && failureCode === 'NONE') failureCode = 'SURFACE_FAILED';
  cleanupCompleted = contextClosed && browserClosed && (downloads.length === 0 || checks.DOWNLOAD_REMOVED === true);
  if (!cleanupCompleted && failureCode === 'NONE') failureCode = 'CLEANUP_FAILED';
}
if (faultObserved) checks.FAULT_OBSERVED = true;
if (contextClosed && browserClosed) await send({version, type: 'publicCleanup', scenario: control.scenario, contextClosed: true, browserClosed: true});
const result = {version, type: 'publicRecoveryResult', scenario: control?.scenario ?? 'happy', browserVersion, status: failureCode === 'NONE' ? 'PUBLIC_BOUNDARY_OK' : 'BOUNDARY_FAILED', failureCode, checks, downloadEvents: admission.events, faultObserved, cleanupCompleted};
if (!resultSent) { resultSent = true; await send(control?.scenario === 'invalid-result' ? {...result, unexpected: publicMarker} : result); }
process.exitCode = failureCode === 'NONE' ? 0 : 1;
if (process.connected) process.disconnect();
