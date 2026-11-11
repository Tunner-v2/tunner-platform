import {PrivateFrameReader, writePrivateFrame} from './private-frames.mjs';
import {runRealScenario} from './supervisor.mjs';
// One fixed entry. No module, command, callback, argument payload or secret environment.
const reader = new PrivateFrameReader(process.stdin, {maximum: 32768, total: 262144, queue: 2});
let exitCode = 1;
try {
  if (process.argv.length !== 2) throw new Error('INPUT_INVALID');
  const control = await reader.next(10000);
  const receipt = await runRealScenario(control, reader);
  await writePrivateFrame(process.stdout, receipt, 32768);
  exitCode = receipt.accepted ? 0 : 1;
} catch { /* Raw parser/process/error diagnostics are forbidden. The coordinator rejects missing receipt. */ }
finally { reader.dispose(); process.stdin.destroy(); }
process.exitCode = exitCode;
