import {randomUUID} from 'node:crypto';
import {realJourneys, realPublicFaults, validRealReceipt} from './contract.mjs';
import {fixedOracleAssembly} from './oracle-bridge.mjs';
import {runRealScenario, sensitiveSourceFacts} from './supervisor.mjs';

// Fixed public-only topology. No account, BFF journey or live seed is created here.
const results = [];
try {
  if (process.argv.length !== 2) throw new Error('INPUT_INVALID');
  for (const publicFault of realPublicFaults) {
    const source = sensitiveSourceFacts();
    const control = {version: 3, type: 'run', journeyId: Object.keys(realJourneys)[0], browserEngine: 'chromium', origin: 'https://localhost:24443', mailpitOrigin: 'http://127.0.0.1:28025', ...source, runId: randomUUID().replaceAll('-', ''), oracleAssemblyPath: fixedOracleAssembly, oracleNativeVersion: '10.0.13', publicFault};
    const receipt = await runRealScenario(control);
    const passed = validRealReceipt(receipt) && receipt.accepted === false && receipt.status === 'BOUNDARY_FAILED' && receipt.publicFaultObserved && receipt.processExited && receipt.oracleExited && receipt.surfaceClean && receipt.surfaceRemoved && (publicFault !== 'unexpected-output' || receipt.rawOutputRejected) && (publicFault !== 'oracle-roundtrip' || receipt.oracleRequests === 1);
    results.push({publicFault, passed, ...receipt});
  }
  const passed = results.length === realPublicFaults.length && results.every(value => value.passed);
  process.stdout.write(JSON.stringify({version: 3, scope: 'PUBLIC_ORACLE_BROWSER_BOUNDARY_ONLY', passed, realJourney: 'NOT_EXECUTED', coordinatorFixtures: 'NOT_EXECUTED_BY_THIS_DRIVER', results}) + '\n');
  process.exitCode = passed ? 0 : 1;
} catch { process.stdout.write(JSON.stringify({version: 3, scope: 'PUBLIC_ORACLE_BROWSER_BOUNDARY_ONLY', passed: false, realJourney: 'NOT_EXECUTED', results}) + '\n'); process.exitCode = 1; }
