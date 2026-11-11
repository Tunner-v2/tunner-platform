import {fork, execFileSync} from 'node:child_process';
import {mkdtemp, realpath, readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {childEnvironment, stopOwnedChild} from '../../totp/sensitive/supervisor.mjs';
import {parsePrivateJson} from '../../totp/sensitive/private-frames.mjs';
import {version, scenarios, budgets, exactKeys, validChildResult, inspectSurface, removeOwnedSurface, canaryPassed, bounded} from './contract.mjs';

const repository = fileURLToPath(new URL('../../../../', import.meta.url));
const childPath = fileURLToPath(new URL('./child.mjs', import.meta.url));
const sources = Object.freeze(['contract.mjs', 'child.mjs', 'supervisor.mjs', 'public-canaries.mjs', 'boundary-tests.mjs', 'README.md'].map(name => 'web/tests/recovery/sensitive/' + name).concat(['web/tests/totp/sensitive/contract.mjs', 'web/tests/totp/sensitive/supervisor.mjs', 'web/tests/totp/sensitive/private-frames.mjs', 'web/package.json', 'web/pnpm-lock.yaml']));
export async function sourceFacts() {
  const options = {cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], windowsHide: true, timeout: 5000};
  try {
    const sourceCommit = execFileSync('git', ['rev-parse', 'HEAD'], options).trim();
    execFileSync('git', ['ls-files', '--error-unmatch', '--', ...sources], options);
    const sourceCommitted = execFileSync('git', ['status', '--porcelain', '--untracked-files=all', '--', ...sources], options).trim() === '';
    const bindings = [];
    for (const path of sources) bindings.push({path, sha256: createHash('sha256').update(await readFile(join(repository, path))).digest('hex')});
    return {sourceCommit, sourceCommitted, bindings};
  } catch { return {sourceCommit: null, sourceCommitted: false, bindings: []}; }
}
export async function runPublicScenario(scenario, {signal} = {}) {
  if (!scenarios.includes(scenario)) throw new Error('INPUT_INVALID');
  const source = await sourceFacts();
  const base = {version, scenario, browserEngine: 'chromium', ...source, publicCanaryPassed: false, sensitiveRunAccepted: false, ownedSurface: null, ownedChildPid: null, ownedChildStartedAt: null, childClosed: false, browserClosedVerified: false, exitCode: null, timedOut: false, outputRejected: false, protocolRejected: false, surfaceClean: false, surfaceRemoved: false, childResultAccepted: false, childStatus: 'NOT_ACCEPTED', childFailureCode: 'INPUT_INVALID', browserVersion: null};
  if (!source.sourceCommitted || signal?.aborted) return base;
  const surface = await mkdtemp(join(await realpath(tmpdir()), 'tunner-p2-009-public-sensitive-'));
  let child; let result = null; let closed = false; let browserClosedVerified = false; let exitCode = null; let outputBytes = 0; let protocolRejected = false; let timedOut = false; let surfaceClean = false; let surfaceRemoved = false; let timer; let ready = false; let ownedChildStartedAt = null;
  let resolveClosed; const closure = new Promise(resolveClose => { resolveClosed = resolveClose; });
  const cancel = () => { timedOut = true; try { if (child?.connected) child.send({version, type: 'cancel'}); } catch {} };
  try {
    ownedChildStartedAt = new Date().toISOString();
    child = fork(childPath, ['--public-recovery-boundary-v1'], {cwd: repository, execArgv: [], env: childEnvironment(surface), stdio: ['ignore', 'pipe', 'pipe', 'ipc'], windowsHide: true, detached: process.platform !== 'win32'});
    process.stdout.write(JSON.stringify({type: 'publicOwnership', scenario, ownedSurface: surface, ownedChildPid: child.pid, ownedChildStartedAt}) + '\n');
    child.once('close', code => { closed = true; exitCode = code; resolveClosed(); });
    child.once('error', () => { protocolRejected = true; });
    for (const stream of [child.stdout, child.stderr]) stream.on('data', bytes => { outputBytes += bytes.length; if (outputBytes > 65536) cancel(); });
    child.on('message', message => {
      try {
        const bytes = Buffer.from(JSON.stringify(message));
        if (bytes.length > budgets.maximumFrameBytes) { protocolRejected = true; cancel(); return; }
        const value = parsePrivateJson(bytes);
        if (!browserClosedVerified && !result && exactKeys(value, ['version', 'type', 'scenario', 'contextClosed', 'browserClosed']) && value.version === version && value.type === 'publicCleanup' && value.scenario === scenario && value.contextClosed === true && value.browserClosed === true) { browserClosedVerified = true; return; }
        if (scenario === 'child-timeout' && !ready && !result && exactKeys(value, ['version', 'type', 'scenario']) && value.version === version && value.type === 'publicReady' && value.scenario === scenario) {
          ready = true; clearTimeout(timer); timer = setTimeout(cancel, 200); return;
        }
        if (result || !validChildResult(value, scenario)) { protocolRejected = true; return; }
        result = value;
      } catch { protocolRejected = true; }
    });
    timer = setTimeout(cancel, budgets.scenario);
    signal?.addEventListener('abort', cancel, {once: true});
    child.send({version, type: 'initialize', scenario, surface});
    // A hard controller bound remains even if child cancellation or IPC fails.
    try { await bounded(closure, budgets.scenario + budgets.cancel); }
    catch { cancel(); }
    if (!closed) { await stopOwnedChild(child); try { await bounded(closure, budgets.close); } catch {} }
    clearTimeout(timer);
    if (!closed) protocolRejected = true;
    surfaceClean = (await bounded(inspectSurface(surface), budgets.close)).clean;
  } catch { protocolRejected = true; }
  finally {
    clearTimeout(timer); signal?.removeEventListener('abort', cancel);
    if (child && !closed) { try { await stopOwnedChild(child); await bounded(closure, budgets.close); } catch {} }
    // Removal is never attempted while a known child still owns the surface.
    if (!child || closed && browserClosedVerified) { try { surfaceRemoved = await bounded(removeOwnedSurface(surface), budgets.close); } catch {} }
    if (child?.connected) { try { child.disconnect(); } catch {} }
  }
  const after = await sourceFacts();
  const sourceUnchanged = after.sourceCommitted && JSON.stringify(after.bindings) === JSON.stringify(source.bindings);
  const parent = {...base, sourceCommitted: source.sourceCommitted && sourceUnchanged, sourceEndCommit: after.sourceCommit, ownedSurface: surface, ownedChildPid: child?.pid ?? null, ownedChildStartedAt, childClosed: closed, browserClosedVerified, exitCode, timedOut, outputRejected: outputBytes > 0, protocolRejected, surfaceClean, surfaceRemoved};
  parent.childResultAccepted = !protocolRejected && Boolean(result);
  parent.childStatus = parent.childResultAccepted ? result.status : 'NOT_ACCEPTED';
  parent.childFailureCode = parent.childResultAccepted ? result.failureCode : 'INPUT_INVALID';
  parent.browserVersion = parent.childResultAccepted ? result.browserVersion : null;
  parent.publicCanaryPassed = canaryPassed(parent, protocolRejected ? null : result);
  // Fixed receipt only. Invalid child fields and raw stream content never escape.
  return parent;
}
