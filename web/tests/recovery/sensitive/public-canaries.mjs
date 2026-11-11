import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
import {version, scenarios, budgets} from './contract.mjs';
import {runPublicScenario, sourceFacts} from './supervisor.mjs';

export async function runPublicMatrix(selected = scenarios) {
  if (!Array.isArray(selected) || selected.length === 0 || selected.length > scenarios.length || new Set(selected).size !== selected.length || selected.some(value => !scenarios.includes(value))) throw new Error('INPUT_INVALID');
  const source = await sourceFacts(); const controller = new AbortController();
  process.stdout.write(JSON.stringify({type: 'publicController', pid: process.pid, startedAt: new Date().toISOString(), sourceCommit: source.sourceCommit, sourceCommitted: source.sourceCommitted}) + '\n');
  const timer = setTimeout(() => controller.abort(), budgets.matrix); const results = [];
  try {
    for (const scenario of selected) {
      if (controller.signal.aborted) break;
      process.stdout.write(JSON.stringify({type: 'publicProgress', scenario, stage: 'START'}) + '\n');
      const result = await runPublicScenario(scenario, {signal: controller.signal}); results.push(result);
      process.stdout.write(JSON.stringify({type: 'publicProgress', scenario, stage: 'COMPLETE', passed: result.publicCanaryPassed}) + '\n');
    }
  } finally { clearTimeout(timer); }
  return {version, type: 'publicRecoveryMatrixReceipt', ...source, publicOnly: true, realRecoveryValues: false, coordinatorFixtures: 'NOT_EXECUTED_BY_THIS_DRIVER', sensitiveRunAccepted: false, scenariosRequested: selected.length, scenariosCompleted: results.length, matrixTimedOut: controller.signal.aborted, passed: source.sourceCommitted && !controller.signal.aborted && results.length === selected.length && results.every(result => result.publicCanaryPassed), results};
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2); let selected = scenarios;
    if (args.length === 1 && args[0].startsWith('--scenario=') && scenarios.includes(args[0].slice(11))) selected = [args[0].slice(11)];
    else if (args.length) throw new Error('INPUT_INVALID');
    const receipt = await runPublicMatrix(selected);
    process.stdout.write(JSON.stringify(receipt) + '\n'); process.exitCode = receipt.passed ? 0 : 1;
  } catch { process.stdout.write(JSON.stringify({version, type: 'publicRecoveryMatrixReceipt', passed: false, failureCode: 'INPUT_INVALID', publicOnly: true, sensitiveRunAccepted: false}) + '\n'); process.exitCode = 1; }
}
