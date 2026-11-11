import {fork, execFileSync} from 'node:child_process';
import {mkdtemp, realpath, readFile, lstat} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {join, resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {childEnvironment, stopOwnedChild} from '../../totp/sensitive/supervisor.mjs';
import {PrivateFrameReader, writePrivateFrame} from '../../totp/sensitive/private-frames.mjs';
import {sameCanonicalPath} from '../../totp/sensitive/contract.mjs';
import {version, budgets, publicMarker, validControl, validChildResult, validCleanup, childAccepted, exactKeys, oracleFaults, bounded} from './contract.mjs';
import {ownedSurface, removeOwnedSurface, inspectSurface, privateMarkers} from './surface.mjs';

const repository = await realpath(fileURLToPath(new URL('../../../../', import.meta.url)));
const childPath = fileURLToPath(new URL('./child.mjs', import.meta.url));
const ownFiles = ['contract.mjs', 'child.mjs', 'supervisor.mjs', 'journeys.mjs', 'surface.mjs', 'public-faults.mjs', 'boundary-tests.mjs', 'README.md'].map(value => 'web/tests/recovery/private/' + value);
const sourceRoots = ['src', 'tools/browser-testing', 'tests/Shared', 'tests/Tunner.Identity.Persistence.Tests', 'web/apps/user', 'web/packages/ui', 'web/fixtures/step-up', 'web/tests/totp/sensitive', 'web/tests/recovery/sensitive', 'web/tests/recovery/private', 'web/package.json', 'web/pnpm-lock.yaml', 'Directory.Build.props', 'Directory.Packages.props', 'global.json'];
export async function sourceFacts() {
  const git = args => execFileSync('git', args, {cwd: repository, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']});
  try {
    const sourceCommit = git(['rev-parse', 'HEAD']).trim();
    const paths = git(['ls-files', '-z', '--', ...sourceRoots]).split('\0').filter(Boolean).sort();
    const hash = createHash('sha256');
    for (const path of paths) { const absolute = join(repository, path), stat = await lstat(absolute); if (!stat.isFile() || stat.isSymbolicLink() || !sameCanonicalPath(await realpath(absolute), absolute)) throw new Error(); hash.update(path); hash.update('\0'); hash.update(await readFile(absolute)); hash.update('\0'); }
    return {sourceCommit, sourceCommitted: /^[a-f0-9]{40}$/.test(sourceCommit) && ownFiles.every(value => paths.includes(value)) && git(['status', '--porcelain', '--untracked-files=all', '--', ...sourceRoots]).trim() === '', sourceFingerprint: hash.digest('hex'), sourceInputs: paths.length};
  } catch { return {sourceCommit: null, sourceCommitted: false, sourceFingerprint: null, sourceInputs: 0}; }
}
const waitClose = (child, state, milliseconds) => state.closed ? Promise.resolve(true) : new Promise(resolveClose => { const done = () => { clearTimeout(timer); resolveClose(true); }; const timer = setTimeout(() => { child.off('close', done); resolveClose(false); }, milliseconds); child.once('close', done); });
const ipc = (child, message) => bounded(new Promise(resolveSend => { try { child.send(message, () => resolveSend()); } catch { resolveSend(); } }), budgets.ipc).catch(() => {});
export async function runScenario(control, {signal, publicProgress = false} = {}) {
  if (!validControl(control)) throw new Error('RECOVERY_PRIVATE_INPUT_INVALID');
  const before = await sourceFacts(); let after; let root; let child; let result; let cleanupProof = false;
  const state = {closed: false}; let exitCode = null, protocolFailed = false, outputBytes = 0, timedOut = false, timer, stopTimer;
  let surfaceClean = false, surfaceRemoved = false, cleanupForced = false, oracleProcessRecorded = false, oraclePid = null;
  const ownership = kind => { if (control.mode === 'public' && publicProgress === true) process.stdout.write(JSON.stringify({type: 'PUBLIC_OWNERSHIP', publicFault: control.publicFault, kind, surface: root?.path ?? null, childPid: child?.pid ?? null, oraclePid, observedUtc: new Date().toISOString()}) + '\n'); };
  const cancel = () => { timedOut = true; if (!child) return; void ipc(child, {version, type: 'cancelRecoveryPrivate'}); clearTimeout(stopTimer); stopTimer = setTimeout(() => { cleanupForced = true; void stopOwnedChild(child); }, budgets.cancel); };
  try {
    root = await ownedSurface(await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-009-private-')));
    ownership('SURFACE');
    child = fork(childPath, ['--recovery-private-child-v1'], {cwd: repository, execArgv: [], env: childEnvironment(root.path), stdio: ['ignore', 'pipe', 'pipe', 'ipc'], windowsHide: true, detached: process.platform !== 'win32'});
    ownership('CHILD');
    child.once('close', code => { state.closed = true; exitCode = code; });
    const consume = bytes => { outputBytes += bytes.length; if (outputBytes > 16384) { protocolFailed = true; cancel(); } };
    child.stdout.on('data', consume); child.stderr.on('data', consume);
    child.on('error', () => { protocolFailed = true; cancel(); });
    child.on('message', message => {
      if (control.mode === 'public' && exactKeys(message, ['version', 'type', 'publicFault']) && message.version === version && message.type === 'recoveryPrivateArmed' && message.publicFault === 'child-timeout' && control.publicFault === 'child-timeout') { clearTimeout(timer); timer = setTimeout(cancel, 250); return; }
      // Existing fixed oracle bridge publishes only its positively spawned PID.
      if (control.mode === 'public' && oracleFaults.includes(control.publicFault) && exactKeys(message, ['version', 'type', 'kind', 'processId']) && message.version === 3 && message.type === 'publicProcess' && message.kind === 'ORACLE' && Number.isSafeInteger(message.processId) && message.processId > 0 && !oracleProcessRecorded) { oracleProcessRecorded = true; oraclePid = message.processId; ownership('ORACLE'); return; }
      if (validCleanup(message, control) && !cleanupProof) { cleanupProof = true; return; }
      if (!result && validChildResult(message, control)) { result = message; return; }
      protocolFailed = true;
    });
    timer = setTimeout(cancel, control.mode === 'public' ? budgets.publicScenario : budgets.journey);
    signal?.addEventListener('abort', cancel, {once: true}); if (signal?.aborted) cancel();
    await ipc(child, {version, type: 'initializeRecoveryPrivate', control, surface: root.path});
    await waitClose(child, state, (control.mode === 'public' ? budgets.publicScenario : budgets.journey) + budgets.cancel + budgets.close);
    if (!state.closed) { cleanupForced = true; await stopOwnedChild(child); await waitClose(child, state, budgets.close); }
  } catch { protocolFailed = true; }
  finally {
    clearTimeout(timer); clearTimeout(stopTimer); signal?.removeEventListener('abort', cancel);
    if (child && !state.closed) { cleanupForced = true; await stopOwnedChild(child); await waitClose(child, state, budgets.close); }
    if (root && state.closed && cleanupProof) {
      const markers = privateMarkers(); markers.remember(publicMarker);
      try { surfaceClean = (await inspectSurface(root, {markers, readContents: control.mode === 'public'})).clean; } catch {}
      finally { markers.clear(); }
      try { surfaceRemoved = await removeOwnedSurface(root); } catch {}
    }
    if (child && !state.closed) { child.stdout?.destroy(); child.stderr?.destroy(); try { if (child.connected) child.disconnect(); } catch {} child.unref(); }
    after = await sourceFacts();
  }
  const sourceCurrent = before.sourceCommit === control.sourceCommit && before.sourceCommitted && after.sourceCommitted && after.sourceCommit === before.sourceCommit && after.sourceFingerprint === before.sourceFingerprint;
  const supervisor = {SOURCE_CURRENT: sourceCurrent, PROTOCOL_VALID: !protocolFailed && Boolean(result), OUTPUT_CONTAINED: outputBytes === 0, CHILD_CLOSED: state.closed, ORACLE_CLOSED: oracleFaults.includes(control.publicFault) ? oracleProcessRecorded && result?.oracleExited === true : cleanupProof, CHILD_SURFACES_CLOSED: cleanupProof, SURFACE_REMOVED: surfaceRemoved};
  const accepted = sourceCurrent && control.mode === 'live' && childAccepted(result, control) && Object.values(supervisor).every(value => value === true) && !timedOut && !cleanupForced && exitCode === 0;
  // Only generated public ownership metadata is reviewable outside the private child.
  const publicOwnership = control.mode === 'public' ? {surface: root?.path ?? null, childPid: child?.pid ?? null, oraclePid} : null;
  return {version, type: 'recoveryPrivateBrowserReceipt', journeyId: control.journeyId, mode: control.mode, publicFault: control.publicFault, sourceCommit: before.sourceCommit, sourceCommitted: sourceCurrent, sourceFingerprint: before.sourceFingerprint, browserVersion: result?.browserVersion ?? null, status: result?.status ?? 'NOT_ACCEPTED', stage: result?.stage ?? 'START', failureCode: result?.failureCode ?? 'PROTOCOL_FAILED', checks: result?.checks ?? {}, supervisor, childResultAccepted: Boolean(result) && !protocolFailed, protocolRejected: protocolFailed, rawOutputRejected: outputBytes > 0, timedOut, cleanupForced, exitCode, contextClosed: result?.contextClosed ?? cleanupProof, browserClosed: result?.browserClosed ?? cleanupProof, oracleExited: result?.oracleExited ?? !oracleFaults.includes(control.publicFault), oracleGraceful: result?.oracleGraceful ?? !oracleFaults.includes(control.publicFault), surfaceClean, surfaceRemoved, cleanupCompleted: result?.cleanupCompleted ?? false, publicFaultObserved: result?.publicFaultObserved ?? false, publicOwnership, accepted};
}

// Fixed framed executable entry for a future reviewed .NET coordinator.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const reader = new PrivateFrameReader(process.stdin, {maximum: budgets.maximumFrame, total: budgets.maximumFrame * 2, queue: 1});
  let code = 1;
  try { if (process.argv.length !== 2) throw new Error(); const control = await reader.next(budgets.initialize); const receipt = await runScenario(control); await writePrivateFrame(process.stdout, receipt, budgets.maximumFrame); code = receipt.accepted ? 0 : 1; }
  catch { /* Missing fixed receipt is failure. No raw parser/runtime diagnostics. */ }
  finally { reader.dispose(); process.stdin.destroy(); }
  process.exitCode = code;
}
