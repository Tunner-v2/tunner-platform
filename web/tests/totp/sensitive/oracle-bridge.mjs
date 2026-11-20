import {spawn} from 'node:child_process';
import {realpath} from 'node:fs/promises';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {PrivateFrameReader, writePrivateFrame} from './private-frames.mjs';
import {realBudgets, realExactKeys, sameCanonicalPath} from './contract.mjs';
import {childEnvironment} from './supervisor.mjs';

const repository = fileURLToPath(new URL('../../../../', import.meta.url));
export const fixedOracleAssembly = resolve(repository, 'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll');
const nativeVersion = '10.0.13';
const error = () => new Error('ORACLE_FAILED');
export const waitForOwnedProcessClose = (child, state, milliseconds) => {
  if (state.closed) return Promise.resolve(true);
  return new Promise(resolve => {
    const closed = () => { state.closed = true; clearTimeout(timer); resolve(true); };
    const timer = setTimeout(() => { child.off('close', closed); resolve(false); }, milliseconds);
    child.once('close', closed);
  });
};
export async function createNativeOracle(control, surface, signal) {
  if (!sameCanonicalPath(control.oracleAssemblyPath, fixedOracleAssembly) ||
      !sameCanonicalPath(await realpath(fixedOracleAssembly), fixedOracleAssembly) || control.oracleNativeVersion !== nativeVersion) throw error();
  const environment = childEnvironment(surface);
  Object.assign(environment, {ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_EnableDiagnostics: '0', DOTNET_DbgEnableMiniDump: '0', COMPlus_DbgEnableMiniDump: '0', DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1'});
  // On Unix inherit the raw child's group so its supervisor can also reap this oracle.
  const child = spawn('dotnet', [fixedOracleAssembly, '--sensitive-native-totp-oracle'], {shell: false, windowsHide: true, cwd: repository, env: environment, stdio: ['pipe', 'pipe', 'pipe']});
  if (control.publicFault !== null && child.pid) { try { process.send?.({version: 3, type: 'publicProcess', kind: 'ORACLE', processId: child.pid}, () => {}); } catch { /* private supervisor observes missing progress as no ownership record */ } }
  const reader = new PrivateFrameReader(child.stdout);
  let failed = false; let requests = 0; let closing = false; let outstanding = false; let stderrSeen = false; let closeResult;
  const processState = {closed: false};
  child.stderr.on('data', () => { stderrSeen = true; failed = true; reader.fail(); });
  child.on('error', () => { failed = true; reader.fail(); });
  child.stdin.on('error', () => { failed = true; reader.fail(); });
  child.once('close', () => { processState.closed = true; });
  const bridge = {
    get requests() { return requests; },
    get exited() { return processState.closed; },
    async nativeCode(seed, counterOffset = 0) {
      if (closing || failed || reader.failed || signal?.aborted || outstanding || requests >= 64 || typeof seed !== 'string' || !/^[A-Z2-7]{1,128}$/.test(seed) || ![-2, -1, 0, 1, 2].includes(counterOffset)) throw error();
      outstanding = true; requests++;
      try {
        const started = performance.now();
        await writePrivateFrame(child.stdin, {version: 1, type: 'compute', requestId: requests, counterOffset, seed}, 2048, {timeout: realBudgets.oracleCompute, signal});
        const response = await reader.next(Math.max(1, realBudgets.oracleCompute - (performance.now() - started)), signal);
        if (failed || reader.failed || !realExactKeys(response, ['version', 'type', 'requestId', 'nativeVersion', 'code']) || response.version !== 1 || response.type !== 'code' || response.requestId !== requests || response.nativeVersion !== nativeVersion || typeof response.code !== 'string' || !/^\d{6}$/.test(response.code)) throw error();
        return response.code;
      } catch { failed = true; throw error(); } finally { outstanding = false; }
    },
    async publicFault(fault) {
      if (!['input', 'exit', 'timeout', 'stderr'].includes(fault) || requests !== 0 || closing || failed) throw error();
      let observed = false;
      try {
        if (fault === 'input') await writePrivateFrame(child.stdin, {version: 1, type: 'compute', requestId: 1, counterOffset: 0, seed: 'PUBLIC_INVALID_008'});
        else await writePrivateFrame(child.stdin, {version: 1, type: 'publicFault', fault});
        try {
          const response = await reader.next(fault === 'timeout' ? 250 : realBudgets.oracleCompute, signal);
          observed = fault === 'input' && realExactKeys(response, ['version', 'type', 'requestId', 'failureCode']) && response.version === 1 && response.type === 'error' && response.requestId === 1 && response.failureCode === 'INPUT_INVALID';
        } catch {
          if (fault === 'exit') observed = reader.failureReason === 'EOF' && await waitForOwnedProcessClose(child, processState, realBudgets.oracleClose);
          if (fault === 'timeout') observed = reader.failureReason === 'TIMEOUT' && !processState.closed && !stderrSeen;
          if (fault === 'stderr') observed = stderrSeen;
        }
      } finally { failed = true; }
      return observed;
    },
    async close() {
      if (closing) return closeResult ?? {exited: processState.closed, graceful: false};
      closing = true; let graceful = false; const started = performance.now();
      try {
        if (!processState.closed && !child.stdin.destroyed) { await writePrivateFrame(child.stdin, {version: 1, type: 'stop'}, 2048, {timeout: 1000}); child.stdin.end(); }
        graceful = await waitForOwnedProcessClose(child, processState, Math.max(1, realBudgets.oracleClose - (performance.now() - started))) && child.exitCode === 0 && reader.drained;
      } catch { graceful = false; }
      if (!processState.closed) {
        // The fixed native oracle has no children. Kill its captured process handle only.
        try { child.kill('SIGKILL'); } catch { /* actual close remains mandatory below */ }
        await waitForOwnedProcessClose(child, processState, realBudgets.oracleClose);
      }
      if (!processState.closed) { child.stdin.destroy(); child.stdout.destroy(); child.stderr.destroy(); child.unref(); }
      reader.dispose(); closeResult = {exited: processState.closed, graceful}; return closeResult;
    },
  };
  try {
    const hello = await reader.next(realBudgets.oracleReady, signal);
    if (failed || reader.failed || !realExactKeys(hello, ['version', 'type', 'nativeVersion']) || hello.version !== 1 || hello.type !== 'ready' || hello.nativeVersion !== nativeVersion) throw error();
    return bridge;
  } catch { failed = true; await bridge.close(); throw error(); }
}
