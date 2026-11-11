import {createRequire} from 'node:module';
import {dirname, join} from 'node:path';
import {realBudgets, realExactKeys, realFailureCodes, realJourneys, realPredicatesSatisfied, realVersion, validFixtureRequest, validFixtureResult, validRealControl, validSurfacePath, scanSurface, publicMarker, publicUri} from './contract.mjs';
import {createNativeOracle} from './oracle-bridge.mjs';

const abort = new AbortController();
let control; let surface; let stage = 'START'; let failureStage; let failureCode = 'NONE';
let status = 'BOUNDARY_FAILED'; let browser; let context; let oracle; let browserVersion = null;
let oracleExited = false; let cleanupCompleted = false; let surfaceClean = false; let fatal = false; let publicFaultObserved = false;
let fixtureSequence = 0; let fixturePending;
const checks = Object.create(null);
const send = value => new Promise(resolve => { try { if (process.connected) process.send(value, () => resolve()); else resolve(); } catch { resolve(); } });
const cancelled = () => new Error('SUPERVISOR_TIMEOUT');
const onFatal = () => { fatal = true; abort.abort(); };
process.on('uncaughtException', onFatal); process.on('unhandledRejection', onFatal);
const check = (id, passed) => {
  if (!control || !realJourneys[control.journeyId].includes(id) || Object.hasOwn(checks, id) || typeof passed !== 'boolean') throw new Error('PREDICATE_FAILED');
  checks[id] = passed; if (!passed) throw new Error('PREDICATE_FAILED');
};
const initial = new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('INPUT_INVALID')), 10000);
  process.on('message', message => {
    if (realExactKeys(message, ['version', 'type']) && message.version === 3 && message.type === 'cancel') { abort.abort(); fixturePending?.reject(cancelled()); return; }
    if (validFixtureResult(message)) {
      if (!fixturePending || message.requestId !== fixturePending.id) { onFatal(); return; }
      const pending = fixturePending; fixturePending = null; pending.resolve(message); return;
    }
    if (!control && realExactKeys(message, ['version', 'type', 'control', 'surface']) && message.version === 3 && message.type === 'initialize' && validRealControl(message.control) && validSurfacePath(message.surface)) {
      control = message.control; surface = message.surface; clearTimeout(timer); resolve(); return;
    }
    onFatal();
  });
});
async function fixtureControl(action) {
  const request = {version: 3, type: 'fixtureRequest', requestId: ++fixtureSequence, action};
  if (!validFixtureRequest(request) || fixturePending || abort.signal.aborted) throw new Error('BOUNDARY_ERROR');
  let timer;
  try {
    const reply = new Promise((resolve, reject) => { fixturePending = {id: request.requestId, resolve, reject}; timer = setTimeout(() => { fixturePending = null; reject(new Error('BOUNDARY_ERROR')); }, 5000); });
    await send(request); const value = await reply;
    if (value.status !== 'OK') throw new Error('BOUNDARY_ERROR');
    return {status: value.status, counts: value.counts};
  } finally { clearTimeout(timer); fixturePending = null; }
}
try {
  await initial; stage = 'ENVIRONMENT';
  if (process.argv.length !== 3 || abort.signal.aborted) throw new Error('INPUT_INVALID');
  for (const key of Object.keys(process.env)) if (/^(DEBUG|PWDEBUG|NODE_OPTIONS|NODE_V8_COVERAGE|NODE_INSPECT_RESUME_ON_START)$/i.test(key) || /^(PLAYWRIGHT|PW_TEST).*(DEBUG|LOG|TRACE|REPORT|SCREENSHOT|VIDEO|HAR)/i.test(key)) delete process.env[key];
  process.env.TMP = surface; process.env.TEMP = surface; process.env.TMPDIR = surface;
  stage = 'LIBRARY'; const require = createRequire(import.meta.url);
  const from = dirname(require.resolve('@playwright/test/package.json'));
  const packagePath = require.resolve('playwright/package.json', {paths: [from]});
  if (require(packagePath).version !== '1.62.1') throw new Error('BOUNDARY_ERROR');
  const {chromium} = require(require.resolve('playwright', {paths: [from]}));
  stage = 'BROWSER';
  browser = await chromium.launch({headless: true, timeout: realBudgets.browserLaunch, downloadsPath: join(surface, 'downloads'), tracesDir: join(surface, 'traces')});
  browserVersion = browser.version();
  const core = require.resolve('playwright-core/package.json', {paths: [dirname(packagePath)]});
  const expected = require(join(dirname(core), 'browsers.json')).browsers.find(item => item.name === 'chromium-headless-shell')?.browserVersion;
  check('BROWSER_ENGINE', /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(browserVersion) && browserVersion === expected);
  context = await browser.newContext({acceptDownloads: false, serviceWorkers: 'block'});
  const page = await context.newPage(); page.setDefaultTimeout(realBudgets.action);
  stage = 'ORACLE'; oracle = await createNativeOracle(control, surface, abort.signal); check('NATIVE_ORACLE_IDENTITY', true);
  stage = 'JOURNEY';
  if (control.publicFault !== null) {
    await page.setContent(`<main><h1>${publicMarker}</h1><p>${publicUri}</p></main>`);
    if (await page.locator('h1').textContent() !== publicMarker) throw new Error('BOUNDARY_ERROR');
    if (control.publicFault === 'oracle-roundtrip') { const code = await oracle.nativeCode('JBSWY3DPEHPK3PXP'); publicFaultObserved = /^\d{6}$/.test(code); }
    else if (control.publicFault.startsWith('oracle-')) publicFaultObserved = await oracle.publicFault(control.publicFault.slice(7));
    else if (control.publicFault === 'journey-entry') {
      const {runPublicJourneyCanary} = await import('./real-journey.mjs');
      const evidence = await runPublicJourneyCanary({page, context, browser, nativeCode: (seed, offset = 0) => oracle.nativeCode(seed, offset), signal: abort.signal});
      publicFaultObserved = realExactKeys(evidence, ['publicDom', 'helperErrorContained']) && evidence.publicDom === true && evidence.helperErrorContained === true;
    }
    else if (control.publicFault === 'browser-error') { try { await page.getByRole('button', {name: publicMarker}).click({timeout: 250}); } catch (error) { publicFaultObserved = error?.name === 'TimeoutError' && error.message.includes(publicMarker); } }
    else if (control.publicFault === 'browser-unhandled') { void Promise.reject(new Error(publicMarker)); await new Promise(resolve => setTimeout(resolve, 50)); publicFaultObserved = fatal; }
    else if (control.publicFault === 'browser-timeout') { try { await page.waitForFunction(() => false, undefined, {timeout: 250}); } catch (error) { publicFaultObserved = error?.name === 'TimeoutError'; } }
    else if (['unexpected-output', 'invalid-result'].includes(control.publicFault)) publicFaultObserved = true;
    // All public runs remain incomplete real journeys, including successful oracle rehearsal.
    throw new Error('BOUNDARY_ERROR');
  }
  const {runRealJourney} = await import('./real-journey.mjs');
  const interruption = new Promise((_, reject) => { if (abort.signal.aborted) reject(cancelled()); else abort.signal.addEventListener('abort', () => reject(cancelled()), {once: true}); });
  await Promise.race([runRealJourney({page, context, browser, origin: control.origin, mailpitOrigin: control.mailpitOrigin, runId: control.runId, journeyId: control.journeyId, check, nativeCode: (seed, offset = 0) => oracle.nativeCode(seed, offset), fixtureControl, signal: abort.signal}), interruption]);
  stage = 'SURFACE_SCAN'; surfaceClean = (await scanSurface(surface, {readContents: false})).clean;
  if (!surfaceClean) throw new Error('SURFACE_FAILED');
  status = 'REAL_JOURNEY_OK';
} catch (error) {
  failureStage = stage; failureCode = realFailureCodes.includes(error?.message) ? error.message : 'BOUNDARY_ERROR';
} finally {
  stage = 'CLEANUP'; checks.CLEANUP_ATTEMPTED = true; let clean = true;
  try { if (context) await context.close(); } catch { clean = false; }
  try { if (browser) await browser.close(); } catch { clean = false; }
  try { if (oracle) { const result = await oracle.close(); oracleExited = result.exited; clean &&= result.graceful; } } catch { clean = false; }
  try { if (surface) surfaceClean = (await scanSurface(surface)).clean; } catch { surfaceClean = false; clean = false; }
  if (control?.publicFault === 'cleanup') { publicFaultObserved = true; clean = false; }
  cleanupCompleted = clean;
  if (!clean) { status = 'BOUNDARY_FAILED'; failureCode = 'CLEANUP_FAILED'; failureStage ??= 'CLEANUP'; }
}
if (fatal || abort.signal.aborted || !control || control.publicFault !== null || !realPredicatesSatisfied(checks, control.journeyId)) { status = 'BOUNDARY_FAILED'; if (failureCode === 'NONE') failureCode = 'PREDICATE_FAILED'; }
const result = {version: realVersion, journeyId: control?.journeyId ?? Object.keys(realJourneys)[0], browserEngine: 'chromium', browserVersion, status, stage: failureStage ?? 'COMPLETE', failureCode, checks, oracleRequests: oracle?.requests ?? 0, oracleExited, surfaceClean, cleanupCompleted, publicFaultObserved};
if (control?.publicFault === 'unexpected-output') process.stderr.write(publicMarker);
if (control?.publicFault === 'invalid-result') result.extra = publicMarker;
await send({type: 'result', result}); process.disconnect?.();
process.exitCode = status === 'REAL_JOURNEY_OK' ? 0 : 1;
