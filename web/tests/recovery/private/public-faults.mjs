import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {version, budgets, publicFaults, oracleFaults, validReceipt, fixtureCountKeys, publicCancelFaults, publicStageFaults, publicExtraFaults} from './contract.mjs';
import {sourceFacts, runScenario} from './supervisor.mjs';

// Public values only. This reducer never grants live acceptance or coordinator facts.
export function publicQualified(receipt) {
  if (!validReceipt(receipt) || receipt.mode !== 'public' || receipt.accepted !== false || !receipt.sourceCommitted) return false;
  const fault = receipt.publicFault, supervisor = receipt.supervisor;
  if (!supervisor || !['SOURCE_CURRENT', 'CHILD_CLOSED', 'ORACLE_CLOSED', 'CHILD_SURFACES_CLOSED', 'SURFACE_REMOVED'].every(id => supervisor[id] === true) || !receipt.contextClosed || !receipt.browserClosed || !receipt.serverExited || !receipt.oracleExited || !receipt.surfaceRemoved || receipt.cleanupForced) return false;
  // Only the nine added cases advertise these additional exact facts.
  // Historical28 expected-fault branches retain their existing interpretation.
  if((publicExtraFaults.includes(fault)||publicStageFaults.includes(fault))&&(receipt.stage!=='PUBLIC_ENTRY'||receipt.oracleGraceful!==true||supervisor.PROTOCOL_VALID!==true||supervisor.OUTPUT_CONTAINED!==true))return false;
  const malformed = ['invalid-result', 'cross-component'].includes(fault);
  if (malformed) return receipt.protocolRejected && !receipt.childResultAccepted && !receipt.rawOutputRejected && receipt.surfaceClean && receipt.exitCode === 0;
  if(fault==='relay-malformed'||fault==='relay-timeout')return receipt.protocolRejected&&receipt.failureCode==='PROTOCOL_FAILED'&&receipt.accepted===false&&!receipt.rawOutputRejected&&receipt.surfaceClean&&receipt.cleanupCompleted&&receipt.exitCode===1;
  if (!receipt.childResultAccepted || receipt.protocolRejected || !receipt.checks?.BROWSER_ENGINE || !receipt.checks?.SURFACE_CHECKED) return false;
  if (fault === 'missing-predicate') return receipt.checks.PUBLIC_ENTRY === undefined && !receipt.rawOutputRejected && receipt.surfaceClean && receipt.cleanupCompleted && receipt.publicFaultObserved;
  if (receipt.checks.PUBLIC_ENTRY !== true) return false;
  if (fault === 'unexpected-artifact') return receipt.status === 'BOUNDARY_FAILED' && receipt.failureCode === 'SURFACE_FAILED' && !receipt.surfaceClean && receipt.publicFaultObserved && !receipt.rawOutputRejected && receipt.cleanupCompleted;
  if (!receipt.surfaceClean) return false;
  if (fault === 'unexpected-output') return receipt.rawOutputRejected && receipt.publicFaultObserved && receipt.cleanupCompleted && receipt.exitCode === 0;
  if (receipt.rawOutputRejected) return false;
  if (fault === 'cleanup') return receipt.status === 'BOUNDARY_FAILED' && receipt.failureCode === 'CLEANUP_FAILED' && !receipt.cleanupCompleted && receipt.publicFaultObserved;
  if (!receipt.cleanupCompleted) return false;
  if(publicExtraFaults.includes(fault)&&(!receipt.checks.DOWNLOAD_ADMITTED||!receipt.checks.DOWNLOAD_REMOVED))return false;
  if(publicStageFaults.includes(fault)&&(!receipt.checks.STAGE_SETTLED_PUBLIC||fault!=='cancel-initiation'&&(!receipt.checks.DOWNLOAD_ADMITTED||!receipt.checks.DOWNLOAD_REMOVED)))return false;
  if (publicCancelFaults.includes(fault)) return receipt.timedOut && receipt.status === 'BOUNDARY_FAILED' && receipt.failureCode === 'SUPERVISOR_TIMEOUT' && receipt.publicFaultObserved && receipt.exitCode === 1;
  if (receipt.timedOut) return false;
  if (oracleFaults.includes(fault)) return receipt.publicFaultObserved && (fault !== 'oracle-roundtrip' || receipt.oracleGraceful) && receipt.exitCode === 0;
  if(fault==='production-controller')return receipt.checks.PRODUCTION_CONTROLLER_PUBLIC===true&&receipt.checks.DOWNLOAD_ADMITTED===true&&receipt.checks.DOWNLOAD_REMOVED===true&&receipt.publicOwnership.serverPid!==null&&receipt.serverExited&&receipt.publicFaultObserved&&receipt.exitCode===0;
  if(fault==='relay-roundtrip')return receipt.checks.FIXTURE_RELAY_PUBLIC===true&&receipt.publicFaultObserved&&receipt.exitCode===0;
  if (fault === 'journey-entry') return receipt.checks.ERROR_CONTAINED === true && receipt.publicFaultObserved && receipt.exitCode === 0;
  if (fault === 'happy') return receipt.status === 'PUBLIC_BOUNDARY_OK' && receipt.failureCode === 'NONE' && receipt.checks.DOWNLOAD_ADMITTED === true && receipt.checks.DOWNLOAD_REMOVED === true && receipt.exitCode === 0;
  const expected = {unarmed: 'ADMISSION_REJECTED', duplicate: 'ADMISSION_REJECTED', 'wrong-page': 'ADMISSION_REJECTED', 'wrong-bytes': 'BYTES_MISMATCH', oversize: 'SIZE_LIMIT', 'outside-identity': 'PATH_REJECTED', 'stream-error': 'STREAM_FAILED', 'delete-error': 'DELETE_FAILED', 'action-timeout': 'ACTION_FAILED', 'fatal-rejection': 'SUPERVISOR_TIMEOUT','popup-extra':'ADMISSION_REJECTED','iframe-extra':'ADMISSION_REJECTED','simultaneous-extra':'ADMISSION_REJECTED','secondary-context-extra':'ADMISSION_REJECTED','stalled-partial-read':'STREAM_FAILED','stalled-delete':'DELETE_FAILED'};
  return receipt.status === 'BOUNDARY_FAILED' && receipt.failureCode === expected[fault] && receipt.publicFaultObserved && receipt.exitCode === 1;
}
export async function runPublicMatrix() {
  const facts = await sourceFacts(), abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), budgets.publicMatrix); const results = [];
  try {
    for (const publicFault of publicFaults) {
      if (abort.signal.aborted) break;
      // The only origin-free registration creates no account, BFF or fixture.
      const control = {version, type: 'runRecoveryPrivate', mode: 'public', journeyId: 'RECOVERY_GENERATE_PASSWORD_V1', publicFault, sourceCommit: facts.sourceCommit, sourceCommitted: facts.sourceCommitted, origin: null, mailpitOrigin: null, runId: '00000000000000000000000000000000'};
      process.stdout.write(JSON.stringify({type: 'PUBLIC_PROGRESS', publicFault}) + '\n');
      const receipt = await runScenario(control, {signal: abort.signal, publicProgress: true, fixtureRelay:publicFault.startsWith('relay-')?async request=>{if(publicFault==='relay-timeout')return new Promise(()=>{});return{version,type:'recoveryFixtureResult',requestId:request.requestId,status:'OK',counts:Object.fromEntries(fixtureCountKeys.map(k=>[k,0])),...(publicFault==='relay-malformed'?{unexpected:true}:{})};}:undefined});
      results.push({publicFault, qualified: publicQualified(receipt), receipt});
      process.stdout.write(JSON.stringify({type: 'PUBLIC_RESULT', publicFault, qualified: results.at(-1).qualified, receipt}) + '\n');
    }
  } finally { clearTimeout(timer); }
  const after = await sourceFacts();
  return {version, type: 'PUBLIC_MATRIX_SUMMARY', sourceCommit: facts.sourceCommit, sourceInputsUnchanged: facts.sourceCommitted && after.sourceCommitted && facts.sourceFingerprint === after.sourceFingerprint && facts.sourceCommit === after.sourceCommit, expected: publicFaults.length, completed: results.length, qualified: results.filter(value => value.qualified).length, passed: !abort.signal.aborted && results.length === publicFaults.length && results.every(value => value.qualified) && facts.sourceCommitted && after.sourceCommitted && facts.sourceCommit === after.sourceCommit && facts.sourceFingerprint === after.sourceFingerprint, sensitiveRunAccepted: false, coordinatorFixtures: 'NOT_EXECUTED_BY_THIS_DRIVER'};
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { if (process.argv.length !== 3 || process.argv[2] !== '--run-public') throw new Error(); const receipt = await runPublicMatrix(); process.stdout.write(JSON.stringify(receipt) + '\n'); process.exitCode = receipt.passed ? 0 : 1; }
  catch { process.stdout.write(JSON.stringify({type: 'PUBLIC_MATRIX_FAILED', sensitiveRunAccepted: false, failureCode: 'BOUNDARY_ERROR'}) + '\n'); process.exitCode = 1; }
}
