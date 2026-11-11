import {createRequire} from 'node:module';
import {dirname, join} from 'node:path';
import {existsSync} from 'node:fs';
import assert from 'node:assert/strict';
import {browserEngines, failureCodes, predicateIds, publicMarker, publicUri, scanSurface, scenarios, validSurfacePath, version} from './contract.mjs';

if (process.argv[2] === '--real-sensitive-v3') {
  await import('./real-child.mjs');
} else {
// No Playwright code is imported before the environment and fatal handlers exist.
const scenario = process.argv[2];
const surface = process.argv[3];
const browserEngine = process.argv[4];
let browserVersion = null;
let stage = 'START';
let failedStage;
let failureCode = 'NONE';
let status = 'BOUNDARY_FAILED';
let browser;
let context;
let filesScanned = 0;
let surfaceClean = false;
let cleanupCompleted = false;
let fatalSeen = false;
let expectedFatal;
let unexpectedFatal = false;
let acknowledgeFatal;
let cancel;
const checks = {};
const fatal = error => { fatalSeen = true; unexpectedFatal ||= error !== expectedFatal; acknowledgeFatal?.(); };
process.on('uncaughtException', fatal);
process.on('unhandledRejection', fatal);
process.on('message', message => {
  if (message && Object.keys(message).sort().join('|') === 'type|version' && message.type === 'cancel' && message.version === version) cancel?.();
});
const send = value => new Promise(resolve => {
  if (!process.connected) { resolve(); return; }
  try { process.send(value, () => resolve()); } catch { resolve(); }
});
const check = (id, passed) => {
  if (!predicateIds.includes(id) || typeof passed !== 'boolean') throw new Error('PREDICATE_INVALID');
  checks[id] = passed;
  if (!passed) throw new Error('PREDICATE_FAILED');
};
try {
  stage = 'ENVIRONMENT';
  if (!scenarios.includes(scenario) || !browserEngines.includes(browserEngine) || !validSurfacePath(surface)) throw new Error('INPUT_INVALID');
  for (const key of Object.keys(process.env)) {
    if (/^(?:DEBUG|PWDEBUG|NODE_OPTIONS|NODE_V8_COVERAGE|NODE_INSPECT_RESUME_ON_START)$/i.test(key) || /^(?:PLAYWRIGHT|PW_TEST).*(?:DEBUG|LOG|TRACE|REPORT|SCREENSHOT|VIDEO|HAR)/i.test(key)) delete process.env[key];
  }
  process.env.TMP = surface;
  process.env.TEMP = surface;
  process.env.TMPDIR = surface;
  stage = 'LIBRARY';
  const require = createRequire(import.meta.url);
  // Resolve installed metadata only; never execute @playwright/test or its runner.
  const from = dirname(require.resolve('@playwright/test/package.json'));
  const packagePath = require.resolve('playwright/package.json', {paths: [from]});
  const metadata = require(packagePath);
  if (metadata.version !== '1.62.1') throw new Error('LIBRARY_VERSION');
  const raw = require.resolve('playwright', {paths: [from]});
  const {chromium} = require(raw);
  stage = 'BROWSER';
  let executable;
  if (browserEngine === 'edge') {
    const programFiles = Object.entries(process.env).filter(([key]) => ['PROGRAMFILES(X86)', 'PROGRAMFILES'].includes(key.toUpperCase())).map(([, value]) => value);
    executable = programFiles.map(root => join(root, 'Microsoft', 'Edge', 'Application', 'msedge.exe')).find(existsSync);
    if (!executable) throw new Error('BROWSER_NOT_INSTALLED');
  }
  // Each engine is explicit. Missing binaries fail; no engine fallback or installation.
  browser = await chromium.launch({headless: true, ...(executable ? {executablePath: executable} : {}), timeout: 10000, downloadsPath: surface + '/downloads', tracesDir: surface + '/traces'});
  browserVersion = browser.version();
  let expectedVersion = null;
  if (browserEngine === 'chromium') {
    const core = require.resolve('playwright-core/package.json', {paths: [dirname(packagePath)]});
    const registered = require(join(dirname(core), 'browsers.json'));
    expectedVersion = registered.browsers.find(item => item.name === 'chromium-headless-shell')?.browserVersion;
  }
  check('BROWSER_ENGINE', /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(browserVersion) && (browserEngine === 'edge' || browserVersion === expectedVersion));
  context = await browser.newContext({acceptDownloads: false, serviceWorkers: 'block'});
  const page = await context.newPage();
  page.setDefaultTimeout(250);
  stage = 'PUBLIC_DOM';
  // Deliberately invalid provisioning seed and a nonfunctional public SVG QR mock.
  // This is solely a diagnostic-containment fixture, never a native TOTP response.
  await page.setContent(`<main><h1>${publicMarker}</h1><p>${publicUri}</p><label>Canary<input aria-label="Canary" disabled></label><svg role="img" aria-label="Public nonfunctional QR canary" viewBox="0 0 7 7"><path d="M0 0h3v3H0zM4 0h3v3H4zM0 4h3v3H0zM4 4h1v1H4zM6 6h1v1H6z"/></svg></main>`);
  check('PUBLIC_DOM', await page.getByRole('heading').textContent() === publicMarker);
  stage = 'PUBLIC_ACTION';
  if (scenario === 'supervisor-timeout') {
    const cancelled = new Promise((_, reject) => { cancel = () => reject(new Error('SUPERVISOR_TIMEOUT')); });
    // Independent child deadline also releases its browser if supervisor signalling fails.
    const fallback = setTimeout(() => cancel(), 10000);
    await send({type: 'armed', version, scenario});
    try { await cancelled; } finally { clearTimeout(fallback); }
  }
  if (scenario === 'unhandled' || scenario === 'uncaught') {
    const observed = new Promise(resolve => { acknowledgeFatal = resolve; });
    expectedFatal = new Error(publicMarker + publicUri);
    if (scenario === 'unhandled') void Promise.reject(expectedFatal);
    else setTimeout(() => { throw expectedFatal; }, 0);
    await Promise.race([observed, new Promise((_, reject) => setTimeout(() => reject(new Error('FATAL_NOT_OBSERVED')), 2000))]);
    check('FATAL_HANDLER', fatalSeen && !unexpectedFatal);
    status = 'EXPECTED_FATAL_CONTAINED';
  } else if (['locator', 'fill', 'assertion', 'timeout'].includes(scenario)) {
    let observed = false;
    try {
      if (scenario === 'locator') await page.getByRole('button', {name: publicMarker}).click();
      if (scenario === 'fill') await page.getByRole('textbox', {name: 'Canary'}).fill(publicMarker);
      if (scenario === 'assertion') assert.equal(await page.getByRole('heading').textContent(), 'PUBLIC_EXPECTED_OTHER_VALUE');
      if (scenario === 'timeout') await page.waitForFunction(() => false, undefined, {timeout: 250});
    } catch (error) {
      // Inspect only inside the child. Never return a message, stack or compared value.
      observed = scenario === 'timeout' ? error?.name === 'TimeoutError' : typeof error?.message === 'string' && error.message.includes(publicMarker);
    }
    check('EXPECTED_ERROR', observed);
    status = 'EXPECTED_ERROR_CONTAINED';
  } else {
    status = 'PUBLIC_CANARY_OK';
  }
  stage = 'SURFACE_SCAN';
  // Windows may lock normal profile databases/metrics. Inventory live files only;
  // the supervisor strictly reads every surviving file after the process tree exits.
  const scanned = await scanSurface(surface, {readContents: false});
  filesScanned += scanned.filesScanned;
  surfaceClean = scanned.clean;
  if (!surfaceClean) throw new Error('SURFACE_FAILED');
} catch (error) {
  failedStage = stage;
  failureCode = failureCodes.includes(error?.message) ? error.message : 'BOUNDARY_ERROR';
  status = 'BOUNDARY_FAILED';
} finally {
  stage = 'CLEANUP';
  checks.CLEANUP_ATTEMPTED = true;
  let cleanupFailed = false;
  try {
    if (scenario === 'cleanup' && !failedStage) {
      try { throw new Error(publicMarker + publicUri); } catch { cleanupFailed = true; }
    }
    if (context) await context.close();
    if (browser) await browser.close();
    cleanupCompleted = !cleanupFailed;
    if (cleanupFailed) { status = 'BOUNDARY_FAILED'; failureCode = 'CLEANUP_FAILED'; }
  } catch { status = 'BOUNDARY_FAILED'; failureCode = 'CLEANUP_FAILED'; }
}
stage = 'COMPLETE';
if (unexpectedFatal) status = 'BOUNDARY_FAILED';
const result = {version, scenario, browserEngine, browserVersion, status, stage: failedStage ?? stage, failureCode, checks, filesScanned, surfaceClean, cleanupCompleted};
if (scenario === 'unexpected-output') process.stderr.write(publicMarker + publicUri);
if (scenario === 'invalid-result') result.extra = publicMarker;
await send({type: 'result', result});
process.disconnect?.();
// The supervisor bounds teardown even if a native handle remains open.
process.exitCode = status === 'BOUNDARY_FAILED' ? 1 : 0;
}
