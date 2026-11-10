import {fork, spawn} from 'node:child_process';
import {mkdtemp, rm, lstat, realpath} from 'node:fs/promises';
import {dirname, join, resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {browserEngines, hasMarker, predicatesSatisfied, scanSurface, scenarios, validResult, validSurfacePath, version} from './contract.mjs';

const childPath = fileURLToPath(new URL('./child.mjs', import.meta.url));
function childEnvironment(surface) {
  const keep = new Set(['PATH', 'PATHEXT', 'SYSTEMROOT', 'WINDIR', 'SYSTEMDRIVE', 'COMSPEC', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMDATA', 'LOCALAPPDATA', 'APPDATA', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'LANG', 'LC_ALL', 'PLAYWRIGHT_BROWSERS_PATH']);
  const env = {};
  for (const [key, value] of Object.entries(process.env)) if (keep.has(key.toUpperCase())) env[key] = value;
  return {...env, TMP: surface, TEMP: surface, TMPDIR: surface, TUNNER_CANARY_OS_TEMP_ROOT: dirname(surface)};
}
async function stopOwnedChild(child) {
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
async function removeOwnedSurface(surface) {
  // Never recursively delete a caller-supplied, linked, or unresolved target.
  if (!validSurfacePath(surface) || (await lstat(surface)).isSymbolicLink() || resolve(await realpath(surface)).toLowerCase() !== resolve(surface).toLowerCase()) return false;
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
