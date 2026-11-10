import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {predicatesSatisfied, scenarios, validResult, version} from './contract.mjs';
import {runPublicScenario} from './supervisor.mjs';

const repository = fileURLToPath(new URL('../../../../', import.meta.url));
let sourceCommit = null;
let sourceCommitted = false;
try {
  const sha = execFileSync('git', ['rev-parse', 'HEAD'], {cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim();
  if (/^[0-9a-f]{40}$/.test(sha)) sourceCommit = sha;
  const status = execFileSync('git', ['status', '--porcelain', '--', 'web/tests/totp/sensitive'], {cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
  const tracked = execFileSync('git', ['ls-files', '--', 'web/tests/totp/sensitive'], {cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
  const required = ['README.md', 'child.mjs', 'contract.mjs', 'public-canaries.mjs', 'supervisor.mjs'].map(name => 'web/tests/totp/sensitive/' + name);
  const trackedPaths = tracked.trim().split(/\r?\n/);
  sourceCommitted = status.trim() === '' && required.every(path => trackedPaths.includes(path));
} catch { /* Source provenance remains explicitly unknown. */ }
try {
  const commonChecks = {PUBLIC_DOM: true, CLEANUP_ATTEMPTED: true};
  const probe = (id, scenario, checks, shouldAccept = false) => {
    const candidate = {version, scenario, status: 'PUBLIC_CANARY_OK', stage: 'COMPLETE', failureCode: 'NONE', checks, filesScanned: 0, surfaceClean: true, cleanupCompleted: true};
    const envelopeValid = validResult(candidate, scenario);
    return {id, passed: envelopeValid && predicatesSatisfied(candidate, scenario) === shouldAccept, envelopeValid};
  };
  const predicateProbes = [
    probe('EMPTY_CHECKS_REJECTED', 'happy', {}),
    probe('FALSE_REQUIRED_CHECK_REJECTED', 'happy', {...commonChecks, PUBLIC_DOM: false}),
    probe('FALSE_ADDITIONAL_CHECK_REJECTED', 'happy', {...commonChecks, SOURCE_FRESHNESS: false}),
    probe('MISSING_EXPECTED_ERROR_REJECTED', 'locator', {...commonChecks}),
    probe('MISSING_FATAL_HANDLER_REJECTED', 'unhandled', {...commonChecks}),
    probe('CLEANUP_MISSING_CHECKS_REJECTED', 'cleanup', {}),
    probe('SUPERVISOR_TIMEOUT_MISSING_CHECKS_REJECTED', 'supervisor-timeout', {}),
    probe('REQUIRED_TRUE_CHECKS_ACCEPTED', 'happy', {...commonChecks}, true),
  ];
  const results = [];
  for (const scenario of scenarios) results.push(await runPublicScenario(scenario));
  const passed = predicateProbes.every(probe => probe.passed) && results.every(result => result.passed);
  process.stdout.write(JSON.stringify({version, scope: 'PUBLIC_CANARIES_ONLY', status: passed ? 'PUBLIC_CANARIES_PASS' : 'PUBLIC_CANARIES_FAIL', sourceCommit, sourceCommitted, realJourney: 'NOT_EXECUTED', predicateProbes, results}) + '\n');
  process.exitCode = passed ? 0 : 1;
} catch {
  process.stdout.write(JSON.stringify({version, scope: 'PUBLIC_CANARIES_ONLY', status: 'PUBLIC_CANARIES_FAIL', sourceCommit, sourceCommitted, realJourney: 'NOT_EXECUTED'}) + '\n');
  process.exitCode = 1;
}
