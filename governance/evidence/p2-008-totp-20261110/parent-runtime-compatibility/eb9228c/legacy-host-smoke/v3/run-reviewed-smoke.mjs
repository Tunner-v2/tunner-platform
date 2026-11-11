// ARTIFACT ONLY: no build, image acquisition, source edit or ordinary starter invocation.
import {execFile, fork, spawn} from 'node:child_process';
import {promisify} from 'node:util';
import {createHash, randomUUID} from 'node:crypto';
import {readFile, readdir, lstat, realpath, mkdtemp, mkdir, writeFile, rm} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {dirname, join, relative, resolve, sep, isAbsolute} from 'node:path';
import {tmpdir} from 'node:os';
import net from 'node:net';
import https from 'node:https';
import {childEnvironment, stopOwnedChild, removeOwnedSurface} from '../../../../web/tests/totp/sensitive/supervisor.mjs';
import {scanSurface, sameCanonicalPath} from '../../../../web/tests/totp/sensitive/contract.mjs';

const root = await realpath(fileURLToPath(new URL('../../../../',import.meta.url)));
const here = dirname(fileURLToPath(import.meta.url));
const run = promisify(execFile);
const ids = ['BROWSER_ENGINE','NORMAL_HOST_ISOLATED','REGISTRATION_CREATED','EMAIL_VERIFIED','PROFILE_COMPLETED','PASSWORD_SIGN_IN','SIGNED_IN_RELOAD','GENERIC_STEP_UP_BOUND','GENERIC_STEP_UP_COMPLETED','COMPLETION_RECOVERY','COOKIE_FLAGS','PRIVATE_BROWSER_STATE','LOGOUT_GUARDS'];
const sourceRoots = ['src','tools/browser-testing','tests/Shared','tests/Tunner.Identity.Persistence.Tests','web','tools/dev/new-tunner-totp-build-binding.mjs','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','Tunner.Platform.slnx','.editorconfig','.gitattributes','NuGet.Config'];
const pgImage = 'sha256:c6a5b95f7348a70e743a9b5e2a3fd78cf22767a039ae81b36e1e561f138cb09c';
const mailImage = 'sha256:0306debe903ec7a44a4d49266ac54c54667183efc5de1586d75093d9b60b823b';
const label = 'tunner.test.legacy-host-smoke';
const digest = value => createHash('sha256').update(value).digest('hex');
const exact = (value, keys) => value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).sort().join('|') === [...keys].sort().join('|');
const need = value => { if (value !== true) throw new Error('SMOKE_FAILED'); };
const delay = ms => new Promise(resolve => setTimeout(resolve,ms));
const bounded = (promise, ms) => { let timer; return Promise.race([promise,new Promise((_,reject) => { timer = setTimeout(() => reject(new Error('SMOKE_FAILED')),ms); })]).finally(() => clearTimeout(timer)); };
let stage = 'INPUT'; let failureStage; let sourceCommit = null; let buildSource = null; let bindingHash = null;
let controlSurface; let browserSurface; let dockerEnv; let host; let hostClosed = false; let hostForced = false; let browserResult; let browserClosed = false; let browserExit = null; let browserRaw = 0;
let borrowedBefore; let pg; let mail; let timer; let cancelled = false;
const checks = Object.create(null); const runId = randomUUID().replaceAll('-','');
const readiness = {attempts:0,responseObserved:false,pinMatched:false,http200Observed:false,jsonParsed:false,normalModeObserved:false,expectedInstanceObserved:false,readyObserved:false,metadataMatched:false,requestErrorObserved:false,responseErrorObserved:false,timeoutObserved:false,oversizeRejected:false};
const instanceId = randomUUID(); const startedAt = new Date().toISOString();
const output = process.stdout; // Only the final safe receipt is emitted.
const git = async args => (await run('git',args,{cwd:root,encoding:'utf8',maxBuffer:16*1024*1024,windowsHide:true,timeout:15000})).stdout;
async function regular(path, directory = false) {
  need(typeof path === 'string' && !isAbsolute(path) && !path.includes('\\') && !path.includes(':') && path.split('/').every(part => part && part !== '.' && part !== '..'));
  const full = resolve(root,path); need(sameCanonicalPath(await realpath(full),full));
  const stat = await lstat(full); need(!stat.isSymbolicLink() && (directory ? stat.isDirectory() : stat.isFile())); return full;
}
async function verifyBuild() {
  const raw = await readFile(await regular('artifacts/p2-008-totp-20261110/runtime-build-binding.json'));
  const receipt = JSON.parse(raw); need(exact(receipt,['schema_version','source_commit_sha','source_inputs','build_outputs']) && receipt.schema_version === 1 && /^[a-f0-9]{40}$/.test(receipt.source_commit_sha));
  need((await git(['merge-base',receipt.source_commit_sha,'HEAD'])).trim() === receipt.source_commit_sha && !(await git(['status','--porcelain','--untracked-files=all','--',...sourceRoots])).trim());
  const selected = (await git(['ls-files','-z','--',...sourceRoots])).split('\0').filter(Boolean).sort();
  need(JSON.stringify(receipt.source_inputs.map(value => value.path)) === JSON.stringify(selected));
  const tree = async commit => new Map((await git(['ls-tree','-rz',commit,'--',...sourceRoots])).split('\0').filter(Boolean).map(line => { const match = /^(100644|100755) blob ([a-f0-9]{40})\t(.+)$/.exec(line); need(Boolean(match)); return [match[3],match[2]]; }));
  const old = await tree(receipt.source_commit_sha), current = await tree('HEAD');
  for (const item of receipt.source_inputs) need(exact(item,['path','sha256','git_blob']) && old.get(item.path) === item.git_blob && current.get(item.path) === item.git_blob && digest(await readFile(await regular(item.path))) === item.sha256);
  const outputs = [];
  async function walk(path) { for (const name of await readdir(await regular(path,true))) { const child = path+'/'+name, stat = await lstat(resolve(root,child)); need(!stat.isSymbolicLink()); if (stat.isDirectory()) await walk(child); else { need(stat.isFile()); outputs.push(child); } } }
  await walk('tools/browser-testing/bin/Debug/net10.0'); outputs.sort();
  need(JSON.stringify(receipt.build_outputs.map(value => value.path)) === JSON.stringify(outputs) && outputs.includes('tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'));
  for (const item of receipt.build_outputs) need(exact(item,['path','sha256']) && digest(await readFile(await regular(item.path))) === item.sha256);
  return {source:receipt.source_commit_sha,hash:digest(raw)};
}
async function docker(args, timeout = 15000) {
  const result = await run('docker',['--host','npipe:////./pipe/docker_engine',...args],{cwd:root,env:dockerEnv,encoding:'utf8',maxBuffer:256*1024,windowsHide:true,timeout,killSignal:'SIGKILL'}); return result.stdout.trim();
}
const inspectFormat = '{{json .Id}}|{{json .Image}}|{{json .State.Running}}|{{json .State.StartedAt}}|{{json .Config.Labels}}|{{json .HostConfig.LogConfig}}|{{json .Mounts}}|{{json .NetworkSettings.Ports}}';
async function inspect(id) {
  const parts = (await docker(['container','inspect','--format',inspectFormat,id])).split('|').map(value => JSON.parse(value)); need(parts.length === 8);
  return {id:parts[0],image:parts[1],running:parts[2],startedAt:parts[3],labels:parts[4],log:parts[5],mounts:parts[6],ports:parts[7]};
}
async function byName(name) { const values = (await docker(['container','ls','--all','--no-trunc','--filter','name=^/'+name+'$','--format','{{.ID}}'])).split(/\r?\n/).filter(Boolean); need(values.length <= 1); return values[0] ?? null; }
async function borrowed(project) {
  need(typeof project === 'string' && /^tunner-browser-[a-f0-9]{10}$/.test(project));
  const expected = 'tunner-browser-'+digest(Buffer.from(root.toLowerCase())).slice(0,10); need(project === expected);
  const values = (await docker(['container','ls','--all','--no-trunc','--filter','label=com.docker.compose.project='+project,'--filter','label=com.docker.compose.service=postgres','--format','{{.ID}}'])).split(/\r?\n/).filter(Boolean); need(values.length === 1);
  const value = await inspect(values[0]); need(value.image === pgImage && value.running === true);
  return {id:value.id,image:value.image,running:value.running,startedAt:value.startedAt,ports:value.ports};
}
function port(value, key) { const row = value.ports?.[key]; need(Array.isArray(row) && row.length === 1 && row[0].HostIp === '127.0.0.1' && /^\d{4,5}$/.test(row[0].HostPort)); return Number(row[0].HostPort); }
async function createOwned(kind, image, args) {
  const name = 'tunner-legacy-smoke-'+kind+'-'+runId; need(await byName(name) === null);
  const owned = {name,image,id:null}; if (kind === 'pg') pg = owned; else mail = owned;
  const id = await docker(['container','create','--pull=never','--name',name,'--label',label+'='+runId,'--label','tunner.test.legacy-host-kind='+kind,'--restart=no','--log-driver=none',...args,image],30000);
  need(/^[a-f0-9]{64}$/.test(id)); owned.id = id; await docker(['container','start',id],20000);
  const value = await inspect(id); need(value.id === id && value.image === image && value.labels?.[label] === runId && value.log?.Type === 'none' && value.running === true);
  need(kind === 'pg' ? value.mounts?.length === 1 && value.mounts[0].Type === 'tmpfs' && value.mounts[0].Destination === '/var/lib/postgresql' : value.mounts?.length === 0);
  return value;
}
async function removeContainer(owned) {
  if (!owned) return true;
  const id = await byName(owned.name); if (!id) return true;
  const value = await inspect(id); need(value.id === (owned.id ?? id) && value.image === owned.image && value.labels?.[label] === runId);
  need(value.mounts.every(mount => mount.Type === 'tmpfs'));
  await docker(['container','rm','--force','--volumes',id],30000); return await byName(owned.name) === null;
}
async function emptyPort() { const listener = net.createServer(); await new Promise((resolve,reject) => listener.once('error',reject).listen(0,'127.0.0.1',resolve)); const value = listener.address().port; await new Promise(resolve => listener.close(resolve)); return value; }
async function hostReady(origin, expectedPin) {
  readiness.attempts++;
  return new Promise(resolveDone => {
    let finished = false;
    const finish = value => { if (!finished) { finished = true; resolveDone(value); } };
    // Fresh sockets prevent TLS listener accumulation; pin is captured while the response owns its socket.
    // Browser TLS trust remains independently required. No global TLS settings are changed.
    const request = https.get(origin+'/browser-testing/status',{timeout:1500,agent:false,rejectUnauthorized:false},response => {
      readiness.responseObserved = true;
      let pinned = false;
      try { const peer = response.socket?.getPeerCertificate(true); pinned = Buffer.isBuffer(peer?.raw) && digest(peer.raw).toUpperCase() === expectedPin; } catch {}
      readiness.pinMatched ||= pinned;
      readiness.http200Observed ||= response.statusCode === 200;
      if (!pinned || response.statusCode !== 200) { response.destroy(); finish(false); return; }
      let data = ''; let size = 0;
      response.on('data',bytes => {
        size += bytes.length;
        if (size > 4096) { readiness.oversizeRejected = true; response.destroy(); finish(false); }
        else data += bytes;
      });
      response.on('end',() => {
        if (finished) return;
        try {
          const value = JSON.parse(data); readiness.jsonParsed = true;
          const mode = value?.mode === 'ISOLATED_LOCAL_BROWSER_TESTING';
          const instance = value?.instanceId === instanceId; const ready = value?.ready === true;
          readiness.normalModeObserved ||= mode; readiness.expectedInstanceObserved ||= instance; readiness.readyObserved ||= ready;
          const matched = mode && instance && ready && !Object.hasOwn(value,'journeyId'); readiness.metadataMatched ||= matched;
          finish(matched);
        } catch { finish(false); }
      });
      response.on('error',() => { readiness.responseErrorObserved = true; finish(false); });
      response.on('aborted',() => { readiness.responseErrorObserved = true; finish(false); });
    });
    request.on('timeout',() => { readiness.timeoutObserved = true; request.destroy(); finish(false); });
    request.on('error',() => { readiness.requestErrorObserved = true; finish(false); });
  });
}
async function stopHost() {
  if (!host) return true;
  // The unchanged ordinary host exposes no shutdown API. Stop only this captured process and require its close event.
  // This is process-exit evidence, not a claim of graceful ASP.NET shutdown on Windows.
  if (!hostClosed) { try { host.kill('SIGTERM'); } catch {} await bounded(new Promise(resolve => host.once('close',resolve)),3000).catch(() => {}); }
  if (!hostClosed) { hostForced = true; try { host.kill('SIGKILL'); } catch {} await bounded(new Promise(resolve => host.once('close',resolve)),3000).catch(() => {}); }
  return hostClosed;
}
async function runBrowser(control) {
  const child = fork(join(here,'legacy-child.mjs'),[],{cwd:root,execArgv:[],env:childEnvironment(browserSurface),stdio:['ignore','pipe','pipe','ipc'],windowsHide:true,detached:process.platform !== 'win32'});
  let timer; let protocolBad = false;
  const consume = bytes => { browserRaw += bytes.length; if (browserRaw > 16384) { protocolBad = true; void stopOwnedChild(child); } };
  child.stdout.on('data',consume); child.stderr.on('data',consume);
  try {
    await new Promise(resolveDone => {
      timer = setTimeout(() => { cancelled = true; try { child.send({version:1,type:'cancel'},() => {}); } catch {} timer = setTimeout(() => { void stopOwnedChild(child).then(resolveDone); },2000); },120000);
      child.on('message',value => {
        if (browserResult || !exact(value,['version','type','passed','stage','checks','browserVersion','surfaceClean','cleanupCompleted']) || value.version !== 1 || value.type !== 'legacySmokeResult' ||
            !['START','BROWSER','REGISTRATION','AUTHENTICATION','STEP_UP','LOGOUT','COMPLETE','BOUNDARY'].includes(value.stage) || typeof value.passed !== 'boolean' || typeof value.surfaceClean !== 'boolean' || typeof value.cleanupCompleted !== 'boolean' ||
            !(value.browserVersion === null || typeof value.browserVersion === 'string' && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value.browserVersion)) || !value.checks || Array.isArray(value.checks) || Object.entries(value.checks).some(([key,passed]) => !ids.includes(key) || typeof passed !== 'boolean')) { protocolBad = true; return; }
        browserResult = value;
      });
      child.once('error',() => { protocolBad = true; resolveDone(); }); child.once('close',code => { browserClosed = true; browserExit = code; resolveDone(); });
      child.send({...control,version:1,type:'initialize',surface:browserSurface},() => {});
    });
  } finally {
    clearTimeout(timer); if (!browserClosed) { await stopOwnedChild(child); if (!browserClosed) await bounded(new Promise(resolve => child.once('close',resolve)),3000).catch(() => {}); }
    if (!browserClosed) { child.stdout.destroy(); child.stderr.destroy(); try { child.disconnect(); } catch {} child.unref(); }
  }
  checks.BROWSER_RECEIPT = !protocolBad && !cancelled && browserClosed && browserExit === 0 && browserRaw === 0 && browserResult?.passed === true && browserResult.stage === 'COMPLETE' && browserResult.cleanupCompleted === true && browserResult.surfaceClean === true && exact(browserResult.checks,ids) && ids.every(id => browserResult.checks[id] === true);
}
try {
  need(process.argv.length === 3 && process.argv[2] === '--execute-reviewed');
  const review = JSON.parse(await readFile(join(here,'review-manifest.json'),'utf8')); need(review.state === 'PREPARED_REVIEW_CANDIDATE' && Array.isArray(review.inputs));
  const requiredInputs = ['artifacts/p2-008-totp-20261110/legacy-host-smoke/v3/legacy-child.mjs','artifacts/p2-008-totp-20261110/legacy-host-smoke/v3/run-reviewed-smoke.mjs','web/tests/totp/sensitive/supervisor.mjs','web/tests/totp/sensitive/contract.mjs'];
  need(JSON.stringify(review.inputs.map(value => value.path).sort()) === JSON.stringify(requiredInputs.sort()));
  for (const input of review.inputs) need(digest(await readFile(await regular(input.path))) === input.sha256);
  sourceCommit = (await git(['rev-parse','HEAD'])).trim(); need(/^[a-f0-9]{40}$/.test(sourceCommit));
  const binding = await verifyBuild(); buildSource = binding.source; bindingHash = binding.hash; checks.SOURCE_BUILD_BOUND = true;
  timer = setTimeout(() => { cancelled = true; void stopHost(); },240000);
  stage = 'CONFIG';
  const runtime = JSON.parse((await readFile(await regular('artifacts/browser-testing/runtime.json'),'utf8')).replace(/^\uFEFF/,''));
  need(runtime.Mode === 'ISOLATED_LOCAL_BROWSER_TESTING' && /^[A-Fa-f0-9]{40}$/.test(runtime.CertificateThumbprint) && /^[A-Fa-f0-9]{64}$/.test(runtime.CertificateSha256));
  controlSurface = await mkdtemp(join(await realpath(tmpdir()),'tunner-p2-008-legacy-host-')); await mkdir(join(controlSurface,'docker')); await writeFile(join(controlSurface,'docker','config.json'),'{}\n',{flag:'wx'});
  await writeFile(join(controlSurface,'pgpass.empty'),'',{flag:'wx'});
  dockerEnv = {...childEnvironment(controlSurface),DOCKER_CONFIG:join(controlSurface,'docker'),DOCKER_HOST:'npipe:////./pipe/docker_engine'};
  // No registry credentials, context, passfile, inherited Node flags or operator password variables are inherited.
  borrowedBefore = await borrowed(runtime.ProjectName);
  need(await docker(['image','inspect','--format','{{.Id}}',pgImage]) === pgImage && await docker(['image','inspect','--format','{{.Id}}',mailImage]) === mailImage); checks.CACHED_IMAGES_EXACT = true;
  stage = 'RESOURCES';
  const pgValue = await createOwned('pg',pgImage,['--publish','127.0.0.1::5432','--mount','type=tmpfs,destination=/var/lib/postgresql','--env','POSTGRES_USER=tunner','--env','POSTGRES_DB=tunner_browser_testing','--env','POSTGRES_HOST_AUTH_METHOD=trust']);
  const pgPort = port(pgValue,'5432/tcp'); checks.OWNED_PG_IDENTITY = true;
  let pgReady = false; for (let attempt=0;attempt<100;attempt++) { need(!cancelled); try { await docker(['exec',pg.id,'pg_isready','-q','-U','tunner','-d','tunner_browser_testing'],2000); pgReady = true; break; } catch {} await delay(300); } need(pgReady);
  const mailValue = await createOwned('mailpit',mailImage,['--publish','127.0.0.1::1025','--publish','127.0.0.1::8025']);
  const smtpPort = port(mailValue,'1025/tcp'), mailPort = port(mailValue,'8025/tcp'); checks.OWNED_MAILPIT_IDENTITY = true;
  let mailReady = false; for (let attempt=0;attempt<30;attempt++) { need(!cancelled); try { const response = await fetch('http://127.0.0.1:'+mailPort+'/',{signal:AbortSignal.timeout(1500)}); mailReady = response.ok; await response.body?.cancel(); if (mailReady) break; } catch {} await delay(300); } need(mailReady);
  const appPort = await emptyPort(); need(new Set([pgPort,smtpPort,mailPort,appPort]).size === 4);
  const config = {Mode:'ISOLATED_LOCAL_BROWSER_TESTING',InstanceId:instanceId,ApplicationPort:appPort,PostgresPort:pgPort,SmtpPort:smtpPort,CertificateThumbprint:runtime.CertificateThumbprint};
  const configPath = join(controlSurface,'runtime.json'); await writeFile(configPath,JSON.stringify(config),{flag:'wx'});
  const assembly = await regular('tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll');
  stage = 'HOST';
  const env = {...childEnvironment(controlSurface),ASPNETCORE_ENVIRONMENT:'Development',Logging__LogLevel__Default:'None',Logging__LogLevel__Microsoft:'None',Logging__LogLevel__System:'None',DOTNET_NOLOGO:'1',PGPASSWORD:'',PGPASSFILE:join(controlSurface,'pgpass.empty')};
  host = spawn('dotnet',[assembly,'--local-browser-testing',configPath],{cwd:root,env,stdio:['ignore','pipe','pipe'],windowsHide:true});
  let hostBytes = 0; const discard = bytes => { hostBytes += bytes.length; if (hostBytes > 1048576) { cancelled = true; void stopHost(); } }; host.stdout.on('data',discard); host.stderr.on('data',discard); host.on('error',() => { cancelled = true; }); host.once('close',() => { hostClosed = true; });
  const appOrigin = 'https://localhost:'+appPort; let ready = false;
  for (let attempt=0;attempt<120;attempt++) { need(!hostClosed && !cancelled); if (await hostReady(appOrigin,runtime.CertificateSha256.toUpperCase())) { ready = true; break; } await delay(500); } need(ready); checks.NORMAL_HOST_READY = true;
  stage = 'BROWSER'; browserSurface = await mkdtemp(join(await realpath(tmpdir()),'tunner-p2-008-sensitive-'));
  await runBrowser({origin:appOrigin,mailpitOrigin:'http://127.0.0.1:'+mailPort,instanceId}); need(checks.BROWSER_RECEIPT === true);
  stage = 'DATABASE_CHECK';
  const sql = "SELECT json_build_object('oneAccount',(SELECT count(*) FROM accounts.accounts)=1,'oneOwner',(SELECT count(*) FROM browser_testing.step_up_results)=1,'oneConsumedProof',(SELECT count(*) FROM identity.step_up_proofs WHERE state='CONSUMED')=1,'noActiveSession',(SELECT count(*) FROM identity.tunner_sessions WHERE state='ACTIVE')=0)";
  const persistence = JSON.parse(await docker(['exec',pg.id,'psql','--no-psqlrc','-U','tunner','-d','tunner_browser_testing','-Atqc',sql]));
  checks.ACTUAL_COMMIT_COUNTS = exact(persistence,['oneAccount','oneOwner','oneConsumedProof','noActiveSession']) && Object.values(persistence).every(value => value === true); need(checks.ACTUAL_COMMIT_COUNTS);
  const after = await verifyBuild(); need(after.source === buildSource && after.hash === bindingHash && (await git(['rev-parse','HEAD'])).trim() === sourceCommit); checks.SOURCE_BUILD_STABLE = true;
  stage = 'COMPLETE';
} catch { failureStage = stage; }
finally {
  clearTimeout(timer);
  try { checks.HOST_EXITED = await stopHost(); } catch { checks.HOST_EXITED = false; }
  try { checks.BROWSER_SURFACE_CLEAN = Boolean(browserSurface) && (await scanSurface(browserSurface)).clean; } catch { checks.BROWSER_SURFACE_CLEAN = false; }
  try { checks.BROWSER_SURFACE_REMOVED = browserSurface ? await removeOwnedSurface(browserSurface) : true; } catch { checks.BROWSER_SURFACE_REMOVED = false; }
  try { checks.OWNED_MAILPIT_ABSENT = await removeContainer(mail); } catch { checks.OWNED_MAILPIT_ABSENT = false; }
  try { checks.OWNED_PG_ABSENT = await removeContainer(pg); } catch { checks.OWNED_PG_ABSENT = false; }
  try { checks.BORROWED_PG_PRESERVED = Boolean(borrowedBefore) && JSON.stringify(await borrowed(JSON.parse((await readFile(resolve(root,'artifacts/browser-testing/runtime.json'),'utf8')).replace(/^\uFEFF/,'' )).ProjectName)) === JSON.stringify(borrowedBefore); } catch { checks.BORROWED_PG_PRESERVED = false; }
  try {
    if (!controlSurface) checks.OWNED_CONFIG_REMOVED = true;
    else { const actual = await realpath(controlSurface), parent = await realpath(tmpdir()); need(sameCanonicalPath(actual,controlSurface) && sameCanonicalPath(dirname(actual),parent) && /^tunner-p2-008-legacy-host-[A-Za-z0-9]+$/.test(relative(parent,actual)) && !(await lstat(actual)).isSymbolicLink()); await rm(actual,{recursive:true,force:false,maxRetries:2,retryDelay:100}); checks.OWNED_CONFIG_REMOVED = true; }
  } catch { checks.OWNED_CONFIG_REMOVED = false; }
  checks.CLEANUP_ALL_STAGES_ATTEMPTED = true;
}
const required = ['SOURCE_BUILD_BOUND','CACHED_IMAGES_EXACT','OWNED_PG_IDENTITY','OWNED_MAILPIT_IDENTITY','NORMAL_HOST_READY','BROWSER_RECEIPT','ACTUAL_COMMIT_COUNTS','SOURCE_BUILD_STABLE','HOST_EXITED','BROWSER_SURFACE_CLEAN','BROWSER_SURFACE_REMOVED','OWNED_MAILPIT_ABSENT','OWNED_PG_ABSENT','BORROWED_PG_PRESERVED','OWNED_CONFIG_REMOVED','CLEANUP_ALL_STAGES_ATTEMPTED'];
const passed = stage === 'COMPLETE' && !failureStage && !cancelled && required.every(id => checks[id] === true);
output.write(JSON.stringify({schemaVersion:1,type:'NORMAL_HOST_LEGACY_SMOKE',sourceCommit,buildSource,bindingHash,runId,startedAt,finishedAt:new Date().toISOString(),passed,stage:failureStage ?? stage,checks,browserChecks:browserResult?.checks ?? {},browserVersion:browserResult?.browserVersion ?? null,rawChildOutputRejected:browserRaw > 0,browserExited:browserClosed,hostExited:hostClosed,hostForced,requiredSmokeOnly:true,readinessDiagnostics:readiness,boundary:'Actual ordinary local host compatibility only. No TOTP integrated journey or Product/release acceptance.'})+'\n');
process.exitCode = passed ? 0 : 1;
