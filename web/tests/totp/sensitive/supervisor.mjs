import {fork, spawn} from 'node:child_process';
import {mkdtemp, rm, lstat, realpath} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import {dirname, join, resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {browserEngines, hasMarker, predicatesSatisfied, scanSurface, scenarios, validResult, validSurfacePath, version, realVersion, realBudgets, realExactKeys, realJourneys, realReceiptAccepted, validRealControl, validRealChildResult, validFixtureRequest, validFixtureResult, sameCanonicalPath, validPublicProcess} from './contract.mjs';
import {writePrivateFrame} from './private-frames.mjs';

const childPath = fileURLToPath(new URL('./child.mjs', import.meta.url));
export function childEnvironment(surface) {
  const keep = new Set(['PATH', 'PATHEXT', 'SYSTEMROOT', 'WINDIR', 'SYSTEMDRIVE', 'COMSPEC', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMDATA', 'LOCALAPPDATA', 'APPDATA', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'LANG', 'LC_ALL', 'PLAYWRIGHT_BROWSERS_PATH']);
  const env = {};
  for (const [key, value] of Object.entries(process.env)) if (keep.has(key.toUpperCase())) env[key] = value;
  return {...env, TMP: surface, TEMP: surface, TMPDIR: surface, TUNNER_CANARY_OS_TEMP_ROOT: dirname(surface), NODE_USE_SYSTEM_CA: '1'};
}
export async function stopOwnedChild(child) {
  if (!child.pid || child.exitCode !== null || child.signalCode !== null) return;
  if (process.platform === 'win32') {
    // This PID is the still-running process created immediately above; /T is its tree only.
    await new Promise(resolve => {
      const systemRoot = Object.entries(process.env).find(([key]) => key.toUpperCase() === 'SYSTEMROOT')?.[1];
      const killer = spawn(systemRoot ? join(systemRoot, 'System32', 'taskkill.exe') : 'taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], {stdio: 'ignore', windowsHide: true});
      const limit = setTimeout(() => { killer.kill(); killer.unref(); resolve(); }, 3000);
      killer.once('error', () => { clearTimeout(limit); resolve(); });
      killer.once('exit', () => { clearTimeout(limit); resolve(); });
    });
  } else {
    try { process.kill(-child.pid, 'SIGKILL'); } catch { /* fixed failure is reported below */ }
  }
}
export async function removeOwnedSurface(surface) {
  // Never recursively delete a caller-supplied, linked, or unresolved target.
  if (!validSurfacePath(surface) || (await lstat(surface)).isSymbolicLink() || !sameCanonicalPath(await realpath(surface), surface)) return false;
  await rm(surface, {recursive: true, force: true, maxRetries: 3, retryDelay: 100});
  try { await lstat(surface); return false; } catch (error) { return error?.code === 'ENOENT'; }
}
export async function runPublicScenario(scenario, browserEngine = 'edge') {
  if (!scenarios.includes(scenario) || !browserEngines.includes(browserEngine)) throw new Error('SCENARIO_INVALID');
  const surface = await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-008-public-sensitive-'));
  let child;
  let result;
  let closed = false;
  let outputBytes = 0;
  let outputMarker = false;
  let protocolFailed = false;
  let timedOut = false;
  let exitCode = null;
  let filesScanned = 0;
  let surfaceClean = false;
  let surfaceRemoved = false;
  let timer;
  try {
    child = fork(childPath, [scenario, surface, browserEngine], {execArgv: [], env: childEnvironment(surface), stdio: ['ignore', 'pipe', 'pipe', 'ipc'], windowsHide: true, detached: process.platform !== 'win32'});
    const terminal = new Promise(resolve => {
      const failAfter = milliseconds => {
        clearTimeout(timer);
        timer = setTimeout(() => {
          timedOut = true;
          try { child.send({type: 'cancel', version}, () => {}); } catch { /* no raw error */ }
          timer = setTimeout(() => { void stopOwnedChild(child).then(resolve); }, 2000);
        }, milliseconds);
      };
      failAfter(20000);
      const consume = bytes => {
        // Never tee, retain, stringify, attach, or print child diagnostics.
        outputBytes += bytes.length;
        outputMarker ||= hasMarker(bytes);
        if (outputBytes > 16384) { protocolFailed = true; void stopOwnedChild(child).then(resolve); }
      };
      child.stdout.on('data', consume);
      child.stderr.on('data', consume);
      child.on('message', message => {
        if (message && Object.keys(message).sort().join('|') === 'scenario|type|version' && message.type === 'armed' && message.version === version && message.scenario === 'supervisor-timeout' && scenario === 'supervisor-timeout') { failAfter(500); return; }
        if (!message || Object.keys(message).sort().join('|') !== 'result|type' || message.type !== 'result' || result || !validResult(message.result, scenario, browserEngine)) { protocolFailed = true; return; }
        result = message.result;
      });
      child.once('error', () => { protocolFailed = true; resolve(); });
      child.once('close', code => { closed = true; exitCode = code; resolve(); });
    });
    await terminal;
    clearTimeout(timer);
    if (!closed) {
      await stopOwnedChild(child);
      if (!closed) await Promise.race([new Promise(resolve => child.once('close', resolve)), new Promise(resolve => setTimeout(resolve, 3000))]);
    }
    const scanned = await scanSurface(surface);
    filesScanned = scanned.filesScanned + (result?.filesScanned ?? 0);
    surfaceClean = scanned.clean && (['supervisor-timeout', 'invalid-result'].includes(scenario) || result?.surfaceClean === true);
  } catch { protocolFailed = true; }
  finally {
    clearTimeout(timer);
    if (child) await stopOwnedChild(child);
    try { surfaceRemoved = await removeOwnedSurface(surface); } catch { surfaceRemoved = false; }
    if (child && !closed) {
      // A denied OS termination must remain a fixed failed result, never hang the
      // supervisor or acquire passing cleanup evidence. No remaining output is exposed.
      child.stdout?.destroy();
      child.stderr?.destroy();
      try { if (child.connected) child.disconnect(); } catch { /* no raw error */ }
      child.unref();
    }
  }
  const cleanOutput = outputBytes === 0;
  const predicatesAccepted = predicatesSatisfied(result, scenario, browserEngine);
  const normal = !timedOut && closed && exitCode === 0 && result?.cleanupCompleted === true && predicatesAccepted && surfaceClean && surfaceRemoved;
  let passed = normal && !protocolFailed && cleanOutput;
  if (scenario === 'supervisor-timeout') passed = timedOut && result?.status === 'BOUNDARY_FAILED' && result?.failureCode === 'SUPERVISOR_TIMEOUT' && exitCode === 1 && predicatesAccepted && cleanOutput && !protocolFailed && surfaceClean && surfaceRemoved && closed;
  if (scenario === 'unexpected-output') passed = normal && !protocolFailed && outputBytes > 0 && outputMarker;
  if (scenario === 'invalid-result') passed = closed && exitCode === 0 && protocolFailed && !result && cleanOutput && surfaceClean && surfaceRemoved;
  if (scenario === 'cleanup') passed = closed && exitCode === 1 && result?.status === 'BOUNDARY_FAILED' && result?.failureCode === 'CLEANUP_FAILED' && result?.cleanupCompleted === false && predicatesAccepted && !protocolFailed && cleanOutput && surfaceClean && surfaceRemoved;
  const expectedStatus = scenario === 'happy' ? 'PUBLIC_CANARY_OK' : ['unhandled', 'uncaught'].includes(scenario) ? 'EXPECTED_FATAL_CONTAINED' : 'EXPECTED_ERROR_CONTAINED';
  if (!['cleanup', 'supervisor-timeout', 'unexpected-output', 'invalid-result'].includes(scenario)) passed &&= result?.status === expectedStatus;
  return {scenario, browserEngine, browserVersion: result?.browserVersion ?? null, passed, childStatus: result?.status ?? 'NOT_ACCEPTED', childStage: result?.stage ?? 'NOT_ACCEPTED', childFailureCode: result?.failureCode ?? 'NOT_ACCEPTED', envelopeAccepted: Boolean(result) && !protocolFailed && cleanOutput, predicatesAccepted, sensitiveRunAccepted: normal && !protocolFailed && cleanOutput && result?.status === 'PUBLIC_CANARY_OK', rawOutputRejected: !cleanOutput, invalidResultRejected: protocolFailed && !result, supervisorTimedOut: timedOut, surfaceClean, surfaceRemoved, filesScanned};
}

const repository = fileURLToPath(new URL('../../../../', import.meta.url));
const realSourceRoots = ['src', 'tools/browser-testing', 'tests/Shared', 'tests/Tunner.Identity.Persistence.Tests', 'web/tests/totp/sensitive', 'web/apps/user', 'web/packages/ui', 'web/fixtures/step-up', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'web/package.json', 'web/pnpm-lock.yaml'];
const realRequiredFiles = [...['contract.mjs', 'child.mjs', 'supervisor.mjs', 'private-frames.mjs', 'oracle-bridge.mjs', 'real-child.mjs', 'real-supervisor.mjs', 'real-journey.mjs', 'boundary-tests.mjs', 'public-real-canaries.mjs'].map(name => 'web/tests/totp/sensitive/' + name), 'tests/Shared/NativeTotpCodeFixture.cs'];
export function sensitiveSourceFacts() {
  try {
    const run = args => execFileSync('git', args, {cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim();
    const sourceCommit = run(['rev-parse', 'HEAD']);
    if (!/^[a-f0-9]{40}$/.test(sourceCommit)) throw new Error('SOURCE_INVALID');
    const tracked = run(['ls-files', '--', ...realSourceRoots]).split(/\r?\n/);
    const dirty = run(['status', '--porcelain', '--untracked-files=all', '--', ...realSourceRoots]);
    return {sourceCommit, sourceCommitted: dirty === '' && realRequiredFiles.every(path => tracked.includes(path))};
  } catch { return {sourceCommit: null, sourceCommitted: false}; }
}
export async function runRealScenario(control, privateControlReader = null, privateOutput = process.stdout, {publicProbe = false, signal} = {}) {
  const source = sensitiveSourceFacts();
  const fallback = {version: realVersion, type: 'browserReceipt', journeyId: Object.hasOwn(realJourneys, control?.journeyId ?? '') ? control.journeyId : Object.keys(realJourneys)[0], browserEngine: 'chromium', browserVersion: null, status: 'BOUNDARY_FAILED', stage: 'START', failureCode: 'INPUT_INVALID', checks: {}, oracleRequests: 0, rawOutputRejected: false, processExited: false, oracleExited: false, surfaceClean: false, surfaceRemoved: false, cleanupCompleted: false, publicFaultObserved: false, ...source, accepted: false};
  if (!validRealControl(control)) return fallback;
  if (publicProbe && (control.publicFault === null || privateControlReader !== null) || signal?.aborted) return fallback;
  const expectedAssembly = resolve(repository, 'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll');
  if (!sameCanonicalPath(control.oracleAssemblyPath, expectedAssembly) || source.sourceCommit !== control.sourceCommit || source.sourceCommitted !== control.sourceCommitted || control.publicFault === null && !source.sourceCommitted) return {...fallback, stage: 'ENVIRONMENT', failureCode: 'SOURCE_INVALID'};
  let surface; let child; let result; let closed = false; let outputBytes = 0; let protocolFailed = false; let timedOut = false; let exitCode = null; let surfaceClean = false; let surfaceRemoved = false; let timer; let finished = false; let fixturePending = 0; let fixtureSequence = 0; let invalidResultObserved = false; let oracleProcess = 0; let cancelRequested = false;
  let requestCancellation = () => { cancelRequested = true; timedOut = true; };
  const publicProgress = (kind, processId) => { if (publicProbe) process.stderr.write(JSON.stringify({version: 3, type: 'publicProgress', phase: 'OWNED_PROCESS', publicFault: control.publicFault, kind, processId, passed: null, observedAt: new Date().toISOString()}) + '\n'); };
  try {
    if (!sameCanonicalPath(await realpath(expectedAssembly), expectedAssembly)) throw new Error('INPUT_INVALID');
    surface = await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-008-sensitive-'));
    child = fork(childPath, ['--real-sensitive-v3'], {execArgv: [], env: childEnvironment(surface), stdio: ['ignore', 'pipe', 'pipe', 'ipc'], windowsHide: true, detached: process.platform !== 'win32'});
    publicProgress('RAW_CHILD', child.pid);
    const send = message => { try { child.send(message, () => {}); } catch { protocolFailed = true; } };
    if (privateControlReader) void (async () => {
      try {
        while (!finished) {
          const frame = await privateControlReader.next((control.publicFault === null ? realBudgets.journey : realBudgets.publicFault) + 10000);
          if (finished) break;
          if (realExactKeys(frame, ['version', 'type']) && frame.version === 3 && frame.type === 'cancel') { requestCancellation(); continue; }
          if (!validFixtureResult(frame) || !fixturePending || frame.requestId !== fixturePending) throw new Error('INPUT_INVALID');
          fixturePending = 0; send(frame);
        }
      } catch { if (!finished) { protocolFailed = true; requestCancellation(); } }
    })();
    const terminal = new Promise(resolveTerminal => {
      requestCancellation = () => { cancelRequested = true; timedOut = true; clearTimeout(timer); send({version: 3, type: 'cancel'}); timer = setTimeout(() => { void stopOwnedChild(child).then(resolveTerminal); }, realBudgets.cancelGrace); };
      timer = setTimeout(requestCancellation, control.publicFault === null ? realBudgets.journey : realBudgets.publicFault);
      signal?.addEventListener('abort', requestCancellation, {once: true});
      if (cancelRequested || signal?.aborted) requestCancellation();
      const consume = bytes => { outputBytes += bytes.length; if (outputBytes > 16384) { protocolFailed = true; void stopOwnedChild(child).then(resolveTerminal); } };
      child.stdout.on('data', consume); child.stderr.on('data', consume);
      child.on('message', message => {
        if (validPublicProcess(message)) {
          if (control.publicFault === null || oracleProcess || result) { protocolFailed = true; requestCancellation(); return; }
          oracleProcess = message.processId; publicProgress('ORACLE', oracleProcess); return;
        }
        if (validFixtureRequest(message)) {
          if (result || fixturePending || message.requestId !== ++fixtureSequence || !privateControlReader) { protocolFailed = true; requestCancellation(); return; }
          fixturePending = message.requestId;
          void writePrivateFrame(privateOutput, message, 32768).catch(() => { protocolFailed = true; requestCancellation(); }); return;
        }
        if (!realExactKeys(message, ['type', 'result']) || message.type !== 'result' || result || !validRealChildResult(message.result, control) || fixturePending) {
          if (control.publicFault === 'invalid-result' && realExactKeys(message, ['type', 'result']) && message.type === 'result' && message.result?.extra === 'TUNNER_PUBLIC_NONFUNCTIONAL_CANARY_008' && message.result?.publicFaultObserved === true) {
            const publicEvidence = {...message.result}; delete publicEvidence.extra;
            if (validRealChildResult(publicEvidence, control)) { invalidResultObserved = true; result = publicEvidence; }
          }
          protocolFailed = true; return;
        }
        result = message.result;
      });
      child.once('error', () => { protocolFailed = true; resolveTerminal(); });
      child.once('close', code => { closed = true; exitCode = code; resolveTerminal(); });
      send({version: 3, type: 'initialize', control, surface});
    });
    await terminal; clearTimeout(timer);
    if (!closed) { await stopOwnedChild(child); if (!closed) await Promise.race([new Promise(resolve => child.once('close', resolve)), new Promise(resolve => setTimeout(resolve, 3000))]); }
    const scanned = await scanSurface(surface); surfaceClean = scanned.clean && (result?.surfaceClean === true || invalidResultObserved);
  } catch { protocolFailed = true; }
  finally {
    finished = true; clearTimeout(timer); signal?.removeEventListener('abort', requestCancellation); privateControlReader?.dispose();
    if (child) await stopOwnedChild(child);
    if (surface) { try { surfaceRemoved = await removeOwnedSurface(surface); } catch { surfaceRemoved = false; } }
    if (child && !closed) { child.stdout?.destroy(); child.stderr?.destroy(); try { if (child.connected) child.disconnect(); } catch {} child.unref(); }
  }
  const value = {...fallback, ...(result ?? {}), type: 'browserReceipt', rawOutputRejected: outputBytes > 0, processExited: closed, surfaceClean, surfaceRemoved, ...source, accepted: false};
  if (invalidResultObserved) value.publicFaultObserved = true;
  if (protocolFailed || timedOut || outputBytes || exitCode !== 0 || control.publicFault !== null) { value.status = 'BOUNDARY_FAILED'; if (protocolFailed) value.failureCode = 'INPUT_INVALID'; else if (timedOut) value.failureCode = 'SUPERVISOR_TIMEOUT'; else if (value.failureCode === 'NONE') value.failureCode = 'BOUNDARY_ERROR'; }
  value.cleanupCompleted = result?.cleanupCompleted === true && closed && result?.oracleExited === true && surfaceRemoved;
  value.accepted = control.publicFault === null && !protocolFailed && !timedOut && exitCode === 0 && realReceiptAccepted(value);
  return value;
}
