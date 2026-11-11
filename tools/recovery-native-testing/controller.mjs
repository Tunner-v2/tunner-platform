import {spawn, execFileSync} from 'node:child_process';
import {createHash, randomUUID} from 'node:crypto';
import {lstat, mkdir, readFile, readdir, realpath, rmdir, unlink, writeFile} from 'node:fs/promises';
import {dirname, isAbsolute, join, relative, resolve, sep} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {PrivateFrameReader, writePrivateFrame} from '../../web/tests/totp/sensitive/private-frames.mjs';
import {childEnvironment, stopOwnedChild} from '../../web/tests/totp/sensitive/supervisor.mjs';
import {journeys, publicFaults, requiredCoordinatorChecks} from '../../web/tests/recovery/private/contract.mjs';
delete process.env.TUNNER_CANARY_OS_TEMP_ROOT;
const {ownedSurface, inspectSurface, removeOwnedSurface} = await import('../../web/tests/recovery/private/surface.mjs');

const root = await realpath(fileURLToPath(new URL('../../', import.meta.url)));
const dll = resolve(root, 'tools/recovery-native-testing/bin/Debug/net10.0/Tunner.Recovery.NativeTesting.dll');
const bindingPath = resolve(root, 'artifacts/p2-009-recovery-20261111/native-runtime-build-binding.json');
const sourceRoots = ['src','tools/browser-testing','tests/Shared','tests/Tunner.Identity.Persistence.Tests','web',
  'tools/dev/new-tunner-totp-build-binding.mjs','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','Tunner.Platform.slnx','.editorconfig','.gitattributes','NuGet.Config','tools/recovery-native-testing'];
const images = Object.freeze({'native-postgres':'sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873',
  mailpit:'sha256:0306debe903ec7a44a4d49266ac54c54667183efc5de1586d75093d9b60b823b',openbao:'sha256:71156a1c6623a5fa3f5e61b0c6a8ead0faf0df29a778339188443551995d1315'});
const hex = (value,length) => typeof value === 'string' && value.length === length && /^[a-f0-9]+$/.test(value);
const exact = (value,keys) => value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === keys.length && keys.every(key=>Object.hasOwn(value,key));
const requireTrue = value => { if (!value) throw new Error('NATIVE_BOUNDARY_FAILED'); };
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const git = args => execFileSync('git',args,{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe'],timeout:10000,maxBuffer:16*1024*1024});
const same = (a,b) => process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b;
async function checkedFile(path) {
  requireTrue(typeof path==='string' && !isAbsolute(path) && !path.includes('\\') && !path.includes(':') && path.split('/').every(part=>part && part!=='.' && part!=='..'));
  const absolute=resolve(root,path), stat=await lstat(absolute);
  requireTrue(stat.isFile()&&!stat.isSymbolicLink()&&same(await realpath(absolute),absolute)); return absolute;
}
async function sourceBinding(browser=false) {
  const selectedBinding=browser?resolve(root,'artifacts/p2-009-recovery-20261111/runtime-build-binding.json'):bindingPath;
  const raw=await readFile(selectedBinding); requireTrue(raw.length<2000000); const data=JSON.parse(raw);
  requireTrue(exact(data,['schema_version','source_commit_sha','source_inputs','build_outputs'])&&data.schema_version===1&&hex(data.source_commit_sha,40));
  requireTrue(git(['status','--porcelain','--untracked-files=all','--',...sourceRoots]).trim()==='');
  const paths=git(['ls-files','-z','--',...sourceRoots]).split('\0').filter(Boolean).sort();
  requireTrue(JSON.stringify(paths)===JSON.stringify(data.source_inputs.map(row=>row.path)));
  const tree=commit=>new Map(git(['ls-tree','-rz',commit,'--',...sourceRoots]).split('\0').filter(Boolean).map(line=>{
    const match=/^(100644|100755) blob ([a-f0-9]{40})\t(.+)$/.exec(line);requireTrue(match);return [match[3],match[2]];
  }));
  const previous=tree(data.source_commit_sha), current=tree('HEAD');
  for(const row of data.source_inputs) requireTrue(exact(row,['path','sha256','git_blob'])&&previous.get(row.path)===row.git_blob&&current.get(row.path)===row.git_blob&&sha(await readFile(await checkedFile(row.path)))===row.sha256);
  const output=browser?'tools/browser-testing/bin/Debug/net10.0':'tools/recovery-native-testing/bin/Debug/net10.0'; const actual=[];
  async function walk(path){for(const name of (await readdir(resolve(root,path))).sort()){const child=path+'/'+name,stat=await lstat(resolve(root,child));requireTrue(!stat.isSymbolicLink());if(stat.isDirectory())await walk(child);else{await checkedFile(child);actual.push(child);}}}
  await walk(output);actual.sort();requireTrue(JSON.stringify(actual)===JSON.stringify(data.build_outputs.map(row=>row.path))&&actual.includes(output+(browser?'/Tunner.BrowserTesting.dll':'/Tunner.Recovery.NativeTesting.dll')));
  for(const row of data.build_outputs)requireTrue(exact(row,['path','sha256'])&&sha(await readFile(await checkedFile(row.path)))===row.sha256);
  return {commit:git(['rev-parse','HEAD']).trim(),buildCommit:data.source_commit_sha,hash:sha(raw),sourceFingerprint:sha(Buffer.from(JSON.stringify(data.source_inputs))),outputFingerprint:sha(Buffer.from(JSON.stringify(data.build_outputs)))};
}
function validReady(value,child){return exact(value,['version','type','processId','processStartUtc'])&&value.version===2&&value.type==='processReady'&&value.processId===child.pid&&typeof value.processStartUtc==='string'&&/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$/.test(value.processStartUtc);}
function validTerminal(value,browser=false){return exact(value,['version','type','result','unexpectedManagedOutput','accepted'])&&value.version===2&&value.type===(browser?'recoveryBrowserTerminal':'recoveryNativeTerminal')&&typeof value.unexpectedManagedOutput==='boolean'&&typeof value.accepted==='boolean';}
async function waitClose(state,milliseconds){if(state.closed)return true;let timer;try{await Promise.race([state.promise,new Promise(resolve=>{timer=setTimeout(resolve,milliseconds);})]);return state.closed;}finally{clearTimeout(timer);}}
function browserRegistration(id){
  if(Object.hasOwn(journeys,id))return {id,mode:'browser',selection:id,args:['--finite-recovery-browser-testing',id]};
  if(id.startsWith('resource:')&&['after-db','after-mailpit','after-vault-session','after-app-start','cleanup-reject'].includes(id.slice(9)))return {id,mode:'resource-canary',selection:id.slice(9),args:['--finite-recovery-resource-canary',id.slice(9)]};
  if(id.startsWith('public:')&&publicFaults.includes(id.slice(7)))return {id,mode:'public-fault',selection:id.slice(7),args:['--finite-recovery-public-fault',id.slice(7)]};
  throw new Error('NATIVE_BOUNDARY_FAILED');
}
function startOwned(surface,helper,proxyOrigin=null,browser=null){
  const env={...childEnvironment(surface),ASPNETCORE_ENVIRONMENT:'Development',OTEL_SDK_DISABLED:'true',DOTNET_CLI_TELEMETRY_OPTOUT:'1',DOTNET_NOLOGO:'1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE:'1'};
  if(proxyOrigin!==null){const uri=new URL(proxyOrigin);requireTrue(uri.protocol==='http:'&&uri.hostname==='127.0.0.1'&&uri.port&&uri.pathname==='/'&&!uri.username&&!uri.password&&!uri.search&&!uri.hash);Object.assign(env,{HTTP_PROXY:proxyOrigin,HTTPS_PROXY:proxyOrigin,ALL_PROXY:proxyOrigin,NO_PROXY:''});}
  const target=helper||browser===null?dll:resolve(root,'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll');
  const args=helper?['--recovery-fixture-check']:browser?.args??[];
  const child=spawn('dotnet',[target,...args],{cwd:root,env,stdio:['pipe','pipe','pipe'],windowsHide:true,detached:process.platform!=='win32'});
  const state={closed:false,exitCode:null,stderr:0,spawnFailed:false};state.promise=new Promise(resolve=>{child.once('close',code=>{state.closed=true;state.exitCode=code;resolve();});child.once('error',()=>{state.spawnFailed=true;resolve();});});
  child.stdin.on('error',()=>{state.stdinFailed=true;});
  child.stderr.on('data',bytes=>{state.stderr+=bytes.length;if(state.stderr>16384)void stopOwnedChild(child);});
  const reader=new PrivateFrameReader(child.stdout,{maximum:8192,total:262144,queue:2});return {child,state,reader};
}
const borrowedFailureStages=Object.freeze(['BORROWED_CONTROL','BORROWED_SOURCE_PIN','BORROWED_EMPTY_CONFIG','BORROWED_ENGINE_QUERY','BORROWED_ENGINE_IDENTITY','BORROWED_METADATA','BORROWED_CLIENT_DISPOSE']);
async function helper(context,control){
  const owned=startOwned(context.surface.path,true);let ready;let terminal;let protocol=false;let failureCode='HELPER_READY';
  try{
    ready=await owned.reader.next(10000);requireTrue(validReady(ready,owned.child));
    failureCode='HELPER_CONTROL_WRITE';
    await writePrivateFrame(owned.child.stdin,control,8192,{timeout:2000});owned.child.stdin.end();
    failureCode='HELPER_TERMINAL';
    terminal=await owned.reader.next(65000);requireTrue(validTerminal(terminal));
    failureCode='HELPER_CLOSE_OR_OUTPUT';
    requireTrue(await waitClose(owned.state,3000)&&owned.reader.drained&&!owned.reader.failed&&owned.state.stderr===0&&!terminal.unexpectedManagedOutput);
    protocol=true;
    const rejectedStage=terminal.result?.fixture?.action;
    failureCode=owned.state.exitCode===0&&terminal.accepted===true?'NONE':borrowedFailureStages.includes(rejectedStage)?rejectedStage:'HELPER_REJECTED';
  }catch{/* Never retain or stringify raw diagnostics. */}
  finally{if(!owned.state.closed){await stopOwnedChild(owned.child);await waitClose(owned.state,3000);}owned.reader.dispose();}
  context.helperProcesses.push({processId:owned.child.pid??null,processStartUtc:validReady(ready,owned.child)?ready.processStartUtc:null,closed:owned.state.closed,
    exitCode:owned.state.exitCode,protocolValid:protocol,unexpectedManagedOutput:terminal?.unexpectedManagedOutput===true,rawOutputRejected:owned.state.stderr!==0,failureCode});
  return protocol&&owned.state.exitCode===0&&terminal.accepted===true?terminal.result:null;
}
async function processVerified(context,ready){
  const browser=context.browser;
  const result=await helper(context,{version:2,type:'processCheck',runId:context.runId,processId:ready.processId,processStartUtc:ready.processStartUtc,buildBindingSha256:context.binding.hash,
    registration:browser?'BROWSER':'NATIVE',mode:browser?.args[0]??null,selection:browser?.selection??null,browserBuildBindingSha256:browser?context.workerBinding.hash:null});
  return result?.native===null&&result?.canary===null&&result?.fixture?.action==='VERIFY_PROCESS'&&result.fixture.verified===true;
}
async function fixtureCheck(context,entry,action){
  const result=await helper(context,{version:2,type:'fixtureCheck',action,runId:context.runId,owner:entry.owner,containerId:entry.containerId??null,
    preparationVerified:entry.preparationVerified,localDockerEndpoint:context.control.localDockerEndpoint,expectedEngineId:context.control.expectedEngineId,buildBindingSha256:context.binding.hash});
  return result?.native===null&&result?.canary===null&&result?.fixture?.ownerId===entry.owner.ownerId&&result.fixture.action===action?result.fixture:null;
}
async function borrowedCheck(context){
  const result=await helper(context,{version:2,type:'borrowedCheck',runId:context.runId,localDockerEndpoint:context.control.localDockerEndpoint,
    expectedEngineId:context.control.expectedEngineId,expectedContainerId:context.borrowed??null,buildBindingSha256:context.binding.hash});
  const receipt=result?.fixture;return result?.native===null&&result?.canary===null&&receipt?.action==='VERIFY_BORROWED'&&receipt.verified===true&&hex(receipt.borrowedContainerId,64)?receipt.borrowedContainerId:null;
}
function validOwner(frame){
  if(!exact(frame,['version','type','sequence','runId','ownerId','role','name','imageId'])||!hex(frame.ownerId,32))return false;
  return frame.role==='config'?frame.name==='tunner-p2-009-docker-'+frame.ownerId&&frame.imageId===null:
    Object.hasOwn(images,frame.role)&&frame.imageId===images[frame.role]&&frame.name==='tunner-p2-009-'+frame.role+'-'+frame.ownerId;
}
async function absent(path){try{await lstat(path);return false;}catch(error){if(error?.code==='ENOENT')return true;throw new Error('NATIVE_BOUNDARY_FAILED');}}
async function inspectConfig(context,entry,initialized){
  const folder=join(context.surface.path,entry.owner.name), rel=relative(context.surface.path,folder);requireTrue(rel===entry.owner.name&&!isAbsolute(rel));
  if(!initialized)return absent(folder);
  const stat=await lstat(folder);requireTrue(stat.isDirectory()&&!stat.isSymbolicLink()&&same(await realpath(folder),folder));
  const names=await readdir(folder);requireTrue(names.length===1&&names[0]==='config.json');
  const path=join(folder,'config.json'),file=await lstat(path);requireTrue(file.isFile()&&!file.isSymbolicLink()&&file.nlink===1&&file.size===2&&same(await realpath(path),path)&&String(await readFile(path))==='{}');
  entry.file={dev:file.dev,ino:file.ino};return true;
}
async function removeConfig(context,entry){
  const folder=join(context.surface.path,entry.owner.name);if(await absent(folder))return true;
  const stat=await lstat(folder);requireTrue(stat.isDirectory()&&!stat.isSymbolicLink()&&same(await realpath(folder),folder));
  const names=await readdir(folder);requireTrue(names.length===0||names.length===1&&names[0]==='config.json');
  if(names.length){const path=join(folder,'config.json'),file=await lstat(path);requireTrue(file.isFile()&&!file.isSymbolicLink()&&file.nlink===1&&file.size<=2);
    if(entry.file)requireTrue(file.dev===entry.file.dev&&file.ino===entry.file.ino);requireTrue(['','{','{}'].includes(String(await readFile(path))));await unlink(path);}
  await rmdir(folder);return absent(folder);
}
function validNative(receipt,registry){
  const keys=['version','type','cohort','failureCode','cases','caseExecutionSettled','ownershipVerified','noLogPersistenceVerified','containerAbsentVerified','borrowedPostgresPreserved','cleanupVerified','dockerConfigurationAbsentVerified','containmentVerified','sensitiveAccepted'];
  if(!exact(receipt,keys)||receipt.version!==1||receipt.type!=='nativeReceipt'||receipt.failureCode!=='NONE')return false;
  const names={PublicCanaries:'PUBLIC_CANARIES',PublicSql:'PUBLIC_SQL',PrivateRecovery:'PRIVATE_RECOVERY',PublicCapacity:'PUBLIC_CAPACITY'},id=names[receipt.cohort],ids=registry[id];
  return Array.isArray(ids)&&exact(receipt.cases,ids)&&ids.every(key=>receipt.cases[key]===true)&&keys.slice(5,-1).filter(key=>key!=='cases').every(key=>receipt[key]===true)&&receipt.sensitiveAccepted===(id==='PRIVATE_RECOVERY');
}

// This is a fixed NativeTesting registration. It never receives executable/module/SQL/account/image selectors.
export async function runNative(control,options={}){return runOwned(control,options,null);}
export async function runRecoveryBrowser(registration,local){
  requireTrue(exact(local,['localDockerEndpoint','expectedEngineId']));
  return runOwned({cohort:'BROWSER',publicFault:null,...local,publicProbeOrigin:null,publicTelemetryOrigin:null},{},browserRegistration(registration));
}
async function runOwned(control,{resourceCut=null,publicProxyOrigin=null,malformedCase=null}={},browser=null){
  requireTrue(exact(control,['cohort','publicFault','localDockerEndpoint','expectedEngineId','publicProbeOrigin','publicTelemetryOrigin']));
  requireTrue(browser!==null?control.cohort==='BROWSER':['PUBLIC_PROTOCOL','PUBLIC_CANARIES','PUBLIC_SQL','PRIVATE_RECOVERY','PUBLIC_CAPACITY'].includes(control.cohort));
  requireTrue(resourceCut===null||control.cohort==='PUBLIC_CANARIES'&&['PREPARED','CREATED','AFTER_START','AFTER_MIGRATION'].includes(resourceCut));
  requireTrue(publicProxyOrigin===null||control.cohort==='PUBLIC_PROTOCOL'&&control.publicFault==='PROXY');
  requireTrue(malformedCase===null||control.cohort==='PUBLIC_PROTOCOL'&&['ZERO_LENGTH','OVERSIZE','DUPLICATE','BOM','UTF8','TRAILING','LEGACY_V1'].includes(malformedCase));
  const binding=await sourceBinding(),workerBinding=browser===null?binding:await sourceBinding(true),runId=randomUUID().replaceAll('-','');
  const registry=JSON.parse(await readFile(await checkedFile('tools/recovery-native-testing/cases.json'),'utf8'));
  const planned=join(await realpath(tmpdir()),'tunner-p2-009-private-'+runId);let surface;let rootCreated=false;let owned;let ready;let terminal;let sequence=0;let protocol=true;let timedOut=false;let forced=false;let cutObserved=false;let publicTimeoutObserved=false;let verifiedProcess=false;let clean=false;let removed=false;let reconciled=true;let after=null;
  const context={control,binding,workerBinding,browser,runId,surface:null,owners:new Map(),helperProcesses:[],borrowed:null};let borrowedPreserved=false;let capacity=null;
  const deadline=Date.now()+(control.cohort==='PRIVATE_RECOVERY'?1200000:600000);
  try{
    await mkdir(planned,{recursive:false,mode:0o700});rootCreated=true;surface=await ownedSurface(planned);context.surface=surface;
    // Outside-owned helper config is known before creation, and helpers never create an unreported configuration.
    const helperFolder=join(planned,'tunner-p2-009-helper-'+runId);await mkdir(helperFolder,{recursive:false,mode:0o700});await writeFile(join(helperFolder,'config.json'),'{}',{flag:'wx',mode:0o600});
    context.borrowed=await borrowedCheck(context);requireTrue(context.borrowed!==null);
    owned=startOwned(planned,false,publicProxyOrigin,browser);ready=await owned.reader.next(10000);requireTrue(validReady(ready,owned.child));
    verifiedProcess=await processVerified(context,ready);requireTrue(verifiedProcess);
    const runFrame=browser?{version:2,type:'browserRun',runId,localDockerEndpoint:control.localDockerEndpoint,expectedEngineId:control.expectedEngineId,sourceCommit:workerBinding.commit,buildBindingSha256:workerBinding.hash,nativeBuildBindingSha256:binding.hash}:{version:2,type:'nativeRun',runId,cohort:control.cohort,publicFault:control.publicFault,
      localDockerEndpoint:control.localDockerEndpoint,expectedEngineId:control.expectedEngineId,sourceCommit:binding.commit,buildBindingSha256:binding.hash,
      publicProbeOrigin:control.publicProbeOrigin,publicTelemetryOrigin:control.publicTelemetryOrigin};
    if(malformedCase===null||malformedCase==='TRAILING'){
      await writePrivateFrame(owned.child.stdin,runFrame,8192,{timeout:2000});
      if(malformedCase==='TRAILING')owned.child.stdin.write(Buffer.from([0x7f,0,0,1]));
    }else{
      let bytes=Buffer.from(JSON.stringify(runFrame));
      if(malformedCase==='LEGACY_V1')bytes=Buffer.from(JSON.stringify({version:1,type:'run',cohort:'PRIVATE_RECOVERY',localDockerEndpoint:control.localDockerEndpoint}));
      if(malformedCase==='DUPLICATE')bytes=Buffer.from('{"version":2,'+JSON.stringify(runFrame).slice(1));
      if(malformedCase==='BOM')bytes=Buffer.concat([Buffer.from([0xef,0xbb,0xbf]),bytes]);
      if(malformedCase==='UTF8')bytes=Buffer.from([0xff]);
      const header=Buffer.alloc(4);header.writeUInt32BE(malformedCase==='ZERO_LENGTH'?0:malformedCase==='OVERSIZE'?8193:bytes.length);
      owned.child.stdin.write(header);if(!['ZERO_LENGTH','OVERSIZE'].includes(malformedCase))owned.child.stdin.write(bytes);owned.child.stdin.end();bytes.fill(0);
    }
    for(;;){
      const remaining=deadline-Date.now();if(remaining<=0){timedOut=true;throw new Error('NATIVE_BOUNDARY_FAILED');}
      const frame=await owned.reader.next(control.cohort==='PRIVATE_RECOVERY'?remaining:Math.min(remaining,120000));
      if(validTerminal(frame,browser!==null)){terminal=frame;break;}
      if(frame?.type==='capacityMeasured'){
        requireTrue(control.cohort==='PUBLIC_CAPACITY'&&capacity===null&&exact(frame,['version','type','runId','databases','databaseBytes','walBytes'])&&frame.version===2&&frame.runId===runId&&
          Number.isSafeInteger(frame.databases)&&frame.databases===registry.PRIVATE_RECOVERY.length+1&&Number.isSafeInteger(frame.databaseBytes)&&frame.databaseBytes>0&&Number.isSafeInteger(frame.walBytes)&&frame.walBytes>=0);
        capacity={databases:frame.databases,databaseBytes:frame.databaseBytes,walBytes:frame.walBytes};continue;
      }
      if(frame?.type==='publicArmed'){
        requireTrue(exact(frame,['version','type','runId','scenario'])&&frame.version===2&&frame.runId===runId&&control.cohort==='PUBLIC_PROTOCOL'&&control.publicFault==='TIMEOUT'&&frame.scenario==='TIMEOUT');
        publicTimeoutObserved=true;await new Promise(resolve=>setTimeout(resolve,500));timedOut=true;forced=true;await stopOwnedChild(owned.child);break;
      }
      requireTrue(frame?.version===2&&frame.runId===runId&&Number.isInteger(frame.sequence)&&frame.sequence===++sequence&&sequence<=24);
      let entry;let stage;
      if(frame.type==='ownerPrepared'){
        requireTrue(validOwner(frame)&&context.owners.size<8&&!context.owners.has(frame.ownerId)&&![...context.owners.values()].some(e=>e.owner.name===frame.name));
        requireTrue(frame.role!=='config'||![...context.owners.values()].some(e=>e.owner.role==='config'));
        entry={owner:{ownerId:frame.ownerId,role:frame.role,name:frame.name,imageId:frame.imageId},preparationVerified:false,preparedAckSent:false,createdAckSent:false,containerId:null};context.owners.set(frame.ownerId,entry); // Retained BEFORE fallible independent verification/ACK.
        requireTrue(frame.role==='config'?await inspectConfig(context,entry,false):(await fixtureCheck(context,entry,'VERIFY_PREPARED'))?.verified===true);
        stage='PREPARED';entry.preparationVerified=true;
      }else{
        entry=context.owners.get(frame.ownerId);requireTrue(entry?.preparationVerified===true);
        if(frame.type==='ownerCreated'){
          requireTrue(exact(frame,['version','type','sequence','runId','ownerId','containerId'])&&entry.owner.role!=='config'&&entry.containerId===null&&hex(frame.containerId,64));
          entry.containerId=frame.containerId; // Retained BEFORE inspect/ACK; exact-name reconciliation also survives unknown Create.
          requireTrue((await fixtureCheck(context,entry,'VERIFY_CREATED'))?.verified===true);stage='CREATED';
        }else if(frame.type==='ownerInitialized'){
          requireTrue(exact(frame,['version','type','sequence','runId','ownerId'])&&entry.owner.role==='config'&&!entry.file&&await inspectConfig(context,entry,true));stage='INITIALIZED';
        }else if(frame.type==='publicStage'){
          requireTrue(exact(frame,['version','type','sequence','runId','ownerId','canaryId'])&&control.cohort==='PUBLIC_CANARIES'&&entry.containerId&&registry.PUBLIC_CANARIES.includes(frame.canaryId));
          if(['AFTER_START','AFTER_MIGRATION','CASE_FAILURE','CLEANUP_REJECT'].includes(frame.canaryId))requireTrue((await fixtureCheck(context,entry,'VERIFY_STARTED'))?.verified===true);
          stage='CANARY';entry.canaryId=frame.canaryId;
        }else throw new Error('NATIVE_BOUNDARY_FAILED');
      }
      if(resourceCut&&(stage===resourceCut&&entry.owner.role!=='config'||stage==='CANARY'&&entry.canaryId===resourceCut)){cutObserved=true;forced=true;await stopOwnedChild(owned.child);break;}
      if(control.cohort==='PUBLIC_PROTOCOL'&&control.publicFault==='ACK_TIMEOUT'&&stage==='PREPARED')continue;
      const ack={version:2,type:'ownerAck',sequence,runId,ownerId:frame.ownerId,stage,status:'OK'};
      if(control.cohort==='PUBLIC_PROTOCOL'&&stage==='PREPARED'){
        if(control.publicFault==='ACK_OWNER')ack.ownerId='0'.repeat(32);
        if(control.publicFault==='ACK_SEQUENCE')ack.sequence++;
        if(control.publicFault==='ACK_STAGE')ack.stage='CREATED';
      }
      await writePrivateFrame(owned.child.stdin,ack,8192,{timeout:2000});
      if(ack.ownerId===frame.ownerId&&ack.sequence===sequence&&ack.stage===stage){if(stage==='PREPARED')entry.preparedAckSent=true;if(stage==='CREATED')entry.createdAckSent=true;}
    }
    owned.child.stdin.end();requireTrue(await waitClose(owned.state,3000));
    requireTrue(owned.reader.drained&&!owned.reader.failed&&owned.state.stderr===0);
  }catch{protocol=false;}
  finally{
    if(owned&&!owned.state.closed){forced=true;if(ready&&await processVerified(context,ready))await stopOwnedChild(owned.child);await waitClose(owned.state,3000);}
    if(owned)owned.reader.dispose();
    if(context.surface&&(!owned||owned.state.closed)&&context.helperProcesses.every(p=>p.closed)){
      for(const entry of [...context.owners.values()].reverse()){
        if(!context.helperProcesses.every(p=>p.closed)){reconciled=false;break;}
        try{
          if(entry.owner.role==='config')entry.absent=entry.preparationVerified?await removeConfig(context,entry):await absent(join(context.surface.path,entry.owner.name));
          else if(entry.preparationVerified){const absent=await fixtureCheck(context,entry,'VERIFY_ABSENT');entry.absent=absent?.absent===true;if(!entry.absent){forced=true;const removal=await fixtureCheck(context,entry,'REMOVE_ACKNOWLEDGED');entry.absent=removal?.absent===true;} }
          else entry.absent=(await fixtureCheck(context,entry,'VERIFY_PREPARED'))?.verified===true; // No ACK: verify absence, never remove a preexisting foreign target.
          reconciled&&=entry.absent===true;
        }catch{reconciled=false;}
      }
      if(context.helperProcesses.every(p=>p.closed))try{borrowedPreserved=context.borrowed!==null&&await borrowedCheck(context)===context.borrowed;}catch{borrowedPreserved=false;}
      if(context.helperProcesses.every(p=>p.closed)){
        try{const folder=join(planned,'tunner-p2-009-helper-'+runId);const files=await readdir(folder);requireTrue(files.length===1&&files[0]==='config.json');await unlink(join(folder,'config.json'));await rmdir(folder);}catch{reconciled=false;}
        try{clean=(await inspectSurface(surface,{readContents:false})).clean;removed=await removeOwnedSurface(surface);}catch{clean=false;removed=false;}
      }else reconciled=false;
    }else reconciled=false;
    try{after={native:await sourceBinding(),worker:browser===null?null:await sourceBinding(true)};}catch{/* Fixed source rejection only. */}
  }
  const sourceCurrent=after?.native.hash===binding.hash&&after.native.sourceFingerprint===binding.sourceFingerprint&&after.native.outputFingerprint===binding.outputFingerprint&&
    (browser===null||after.worker?.hash===workerBinding.hash&&after.worker.sourceFingerprint===workerBinding.sourceFingerprint&&after.worker.outputFingerprint===workerBinding.outputFingerprint);
  const normal=protocol&&!timedOut&&!forced&&owned?.state.closed===true&&owned.state.exitCode===0&&owned.state.stderr===0&&verifiedProcess&&terminal?.unexpectedManagedOutput===false&&terminal.accepted===true;
  const nativeAccepted=browser===null&&normal&&validNative(terminal?.result?.native,registry)&&terminal.result.fixture===null&&terminal.result.canary===null;
  const report=browser===null?null:terminal?.result;
  const browserShape=browser!==null&&exact(report,['version','sourceCommit','runId','journeyId','mode','selection','accepted','failureCode','managedOutputRejected','publicBoundaryContained','publicResourceContained','publicCleanupReducerRejected','buildBinding','browserReceipt','coordinatorChecks','cleanup','fixtureObservations','boundary'])&&
    report.version===1&&report.sourceCommit===workerBinding.commit&&report.runId===runId&&report.mode===browser.mode&&report.selection===browser.selection&&typeof report.accepted==='boolean'&&
    exact(report.cleanup,['app','postgres','mailpit','vaultSession','vaultContainer','dockerConfig'])&&Object.values(report.cleanup).every(value=>value===true);
  const browserPrivate=browserShape&&browser.mode==='browser'&&normal&&report.accepted===true&&report.failureCode==='NONE'&&!report.managedOutputRejected&&
    exact(report.coordinatorChecks,requiredCoordinatorChecks(browser.selection))&&Object.values(report.coordinatorChecks).every(value=>value===true)&&report.browserReceipt?.accepted===true;
  const marker=browser?.mode==='public-fault'&&browser.selection==='unexpected-output';
  const browserPublic=browserShape&&browser.mode!=='browser'&&protocol&&!timedOut&&!forced&&report.accepted===false&&owned?.state.closed===true&&owned.state.exitCode===0&&owned.state.stderr===0&&verifiedProcess&&
    terminal?.accepted===true&&terminal.unexpectedManagedOutput===marker&&report.managedOutputRejected===marker&&
    (browser.mode==='resource-canary'?report.publicResourceContained===true:report.publicBoundaryContained===true);

  return {version:2,type:browser===null?'nativeControllerReceipt':'browserControllerReceipt',registration:browser?.id??null,cohort:control.cohort,publicFault:control.publicFault,resourceCut,malformedCase,sourceCommit:binding.commit,buildSourceCommit:binding.buildCommit,
    buildBindingSha256:binding.hash,workerBuildBindingSha256:workerBinding.hash,sourceCurrent,protocolValid:protocol,processId:ready?.processId??owned?.child.pid??null,processStartUtc:ready?.processStartUtc??null,processStartVerified:verifiedProcess,
    processClosed:owned?.state.closed===true,exitCode:owned?.state.exitCode??null,rawOutputRejected:(owned?.state.stderr??0)>0,unexpectedManagedOutput:terminal?.unexpectedManagedOutput??false,
    timedOut,forced,cutObserved,publicTimeoutObserved,reconciled,borrowedPreserved,surfaceClean:clean,surfaceRemoved:removed,helperProcessesClosed:context.helperProcesses.every(p=>p.closed),
    terminal:terminal?.result??null,capacity,heldSurface:removed?null:rootCreated?planned:null,helperProcesses:context.helperProcesses,
    reconciliationMetadata:context.borrowed?{localDockerEndpoint:control.localDockerEndpoint,expectedEngineId:control.expectedEngineId,borrowedContainerId:context.borrowed}:null,
    owners:[...context.owners.values()].map(entry=>({owner:entry.owner,preparationVerified:entry.preparationVerified,preparedAckSent:entry.preparedAckSent,createdAckSent:entry.createdAckSent,containerId:entry.containerId,absent:entry.absent===true})),
    publicContainmentAccepted:browserPublic&&sourceCurrent&&reconciled&&borrowedPreserved&&clean&&removed&&context.helperProcesses.every(p=>p.closed),
    accepted:(browser===null?control.cohort!=='PUBLIC_PROTOCOL'&&nativeAccepted:browserPrivate)&&sourceCurrent&&reconciled&&borrowedPreserved&&clean&&removed&&context.helperProcesses.every(p=>p.closed)};
}


// The existing standalone C# reducer entry is public and resource-free. It has no Ready frame.
// Resource/browser registrations above always require the exact processReady/OS identity handshake.
export async function runRecoveryBrowserProtocol(){
  const binding=await sourceBinding(),workerBinding=await sourceBinding(true),runId=randomUUID().replaceAll('-','');
  const planned=join(await realpath(tmpdir()),'tunner-p2-009-private-'+runId);
  const ids=['REAL_EXACT_WINNING_MAP','REAL_EACH_MISSING_FALSE','REAL_EXTRA_CROSS_COMPONENT','CONDITIONAL_COORDINATOR_OWNER','STRICT_SCALAR_AND_KEY_SHAPE','SERVER_LIFECYCLE','PUBLIC_FAILURE_NEVER_REAL','RELAY_PUBLIC_TOPOLOGY','PRODUCTION_CONTROLLER_FACTS','PUBLIC_CONTAINMENT_NEGATIVES','RAW_MANAGED_OUTPUT','SOURCE_SELECTOR_COMPATIBILITY'];
  let surface;let rootCreated=false;let child;let state;let raw=null;let count=0;let errors=0;let overflow=false;let clean=false;let removed=false;let sourceCurrent=false;
  try{
    await mkdir(planned,{recursive:false,mode:0o700});rootCreated=true;surface=await ownedSurface(planned);
    child=spawn('dotnet',[resolve(root,'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'),'--finite-recovery-protocol-canary'],
      {cwd:root,env:{...childEnvironment(planned),ASPNETCORE_ENVIRONMENT:'Development',OTEL_SDK_DISABLED:'true',DOTNET_CLI_TELEMETRY_OPTOUT:'1',DOTNET_NOLOGO:'1'},stdio:['ignore','pipe','pipe'],windowsHide:true,detached:process.platform!=='win32'});
    state={closed:false,exitCode:null};state.promise=new Promise(resolve=>{child.once('close',code=>{state.closed=true;state.exitCode=code;resolve();});child.once('error',resolve);});
    const chunks=[];
    child.stdout.on('data',bytes=>{count+=bytes.length;if(count<=16384)chunks.push(Buffer.from(bytes));else{overflow=true;void stopOwnedChild(child);}});
    child.stderr.on('data',bytes=>{errors+=bytes.length;if(errors>8192)void stopOwnedChild(child);});
    if(!await waitClose(state,60000))throw new Error('NATIVE_PUBLIC_BOUNDARY_FAILED');
    requireTrue(!overflow&&errors===0);raw=JSON.parse(Buffer.concat(chunks).toString('utf8'));for(const bytes of chunks)bytes.fill(0);
  }catch{/* Public fixed failure only; never retain raw diagnostics. */}
  finally{
    if(child&&!state.closed){await stopOwnedChild(child);await waitClose(state,3000);}
    if(surface&&(!child||state.closed))try{clean=(await inspectSurface(surface,{readContents:false})).clean;removed=await removeOwnedSurface(surface);}catch{clean=false;removed=false;}
    try{const nativeAfter=await sourceBinding(),browserAfter=await sourceBinding(true);sourceCurrent=nativeAfter.hash===binding.hash&&browserAfter.hash===workerBinding.hash&&nativeAfter.sourceFingerprint===binding.sourceFingerprint&&browserAfter.sourceFingerprint===workerBinding.sourceFingerprint&&nativeAfter.outputFingerprint===binding.outputFingerprint&&browserAfter.outputFingerprint===workerBinding.outputFingerprint;}catch{/* Fixed source rejection only. */}
  }
  const shape=exact(raw,['version','type','sourceCommit','sourceBuildCurrent','buildBinding','cases','qualified','accepted','sensitiveRunAccepted','unexpectedManagedOutput','failureCode','scope','relayTopology','recorderCompatibility'])&&
    raw.version===1&&raw.type==='recoveryPublicProtocolQualification'&&raw.sourceCommit===workerBinding.commit&&raw.sourceBuildCurrent===true&&
    raw.buildBinding?.ManifestSha256===workerBinding.hash&&exact(raw.cases,ids)&&ids.every(id=>typeof raw.cases[id]==='boolean')&&
    raw.accepted===false&&raw.sensitiveRunAccepted===false&&typeof raw.unexpectedManagedOutput==='boolean';
  const cases=Object.fromEntries(ids.map(id=>[id,shape&&raw.cases[id]===true]));
  const qualified=shape&&raw.qualified===true&&raw.failureCode==='NONE'&&!raw.unexpectedManagedOutput&&Object.values(cases).every(value=>value===true)&&sourceCurrent&&state?.closed===true&&state.exitCode===0&&clean&&removed;
  return {version:2,type:'browserProtocolControllerReceipt',cases,qualified,sourceCurrent,workerBuildBindingSha256:workerBinding.hash,buildBindingSha256:binding.hash,processClosed:state?.closed===true,exitCode:state?.exitCode??null,rawOutputRejected:overflow||errors!==0,unexpectedManagedOutput:shape?raw.unexpectedManagedOutput:false,surfaceClean:clean,surfaceRemoved:removed,heldSurface:removed?null:rootCreated?planned:null};
}
