import {randomUUID} from 'node:crypto';
import {createServer} from 'node:http';
import {mkdir, realpath, writeFile, lstat, readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {PrivateFrameReader} from '../../web/tests/totp/sensitive/private-frames.mjs';
import {runNative} from './controller.mjs';

const root=await realpath(fileURLToPath(new URL('../../',import.meta.url)));
const exact=(value,keys)=>value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).length===keys.length&&keys.every(key=>Object.hasOwn(value,key));
async function listener(){
  const state={requests:0};const server=createServer((request,response)=>{state.requests++;request.resume();response.writeHead(200,{'content-type':'text/plain'});response.end('PUBLIC_NONFUNCTIONAL_RECOVERY_PROBE');});
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
  return {state,origin:'http://127.0.0.1:'+server.address().port+'/',close:()=>new Promise(resolve=>server.close(resolve))};
}
const flags=['SOURCE_BOUND','FRAMES_STRICT','ACK_REQUIRED','OUTPUT_CONTAINED','PROCESS_CLOSED','SURFACES_REMOVED','PROXY_DIRECT','TELEMETRY_DISABLED'];
let checks=Object.fromEntries(flags.map(key=>[key,false]));const results=[];let binding=null;let local=null;let direct;let proxy;let telemetry;
const physicallyClosed=result=>result.sourceCurrent&&result.processStartVerified&&result.processClosed&&result.helperProcessesClosed&&result.surfaceRemoved&&result.reconciled&&result.borrowedPreserved&&result.owners.every(entry=>entry.owner.role==='config'&&entry.absent);
try{
  if(process.argv.length!==2)throw new Error('NATIVE_PUBLIC_INPUT_INVALID');
  const reader=new PrivateFrameReader(process.stdin,{maximum:2048,total:4096,queue:1});
  try{local=await reader.next(5000);}finally{reader.dispose();}
  if(!exact(local,['version','type','localDockerEndpoint','expectedEngineId'])||local.version!==2||local.type!=='qualifyNativeProtocol')throw new Error('NATIVE_PUBLIC_INPUT_INVALID');
  direct=await listener();proxy=await listener();telemetry=await listener();
  const control=scenario=>({cohort:'PUBLIC_PROTOCOL',publicFault:scenario,localDockerEndpoint:local.localDockerEndpoint,expectedEngineId:local.expectedEngineId,
    publicProbeOrigin:scenario==='PROXY'?direct.origin:null,publicTelemetryOrigin:scenario==='TELEMETRY'?telemetry.origin:null});
  for(const scenario of ['HAPPY','ACK_OWNER','ACK_SEQUENCE','ACK_STAGE','ACK_TIMEOUT','MANAGED_OUTPUT','RAW_STDERR','TIMEOUT','PROXY','TELEMETRY']){
    const result=await runNative(control(scenario),{publicProxyOrigin:scenario==='PROXY'?proxy.origin:null});
    binding??=result.buildBindingSha256;const canary=result.terminal?.canary;let observed=false;
    if(scenario==='HAPPY')observed=result.exitCode===0&&result.protocolValid&&canary?.scenario===scenario&&canary.observed&&canary.configurationAbsent&&!result.forced&&!result.rawOutputRejected&&!result.unexpectedManagedOutput;
    else if(scenario.startsWith('ACK_'))observed=result.exitCode===1&&canary?.scenario===scenario&&canary.observed&&!canary.configurationCreated&&canary.configurationAbsent&&!result.rawOutputRejected&&!result.unexpectedManagedOutput;
    else if(scenario==='MANAGED_OUTPUT')observed=result.exitCode===1&&canary?.observed&&result.unexpectedManagedOutput&&!result.rawOutputRejected;
    else if(scenario==='RAW_STDERR')observed=result.exitCode===1&&canary?.observed&&result.rawOutputRejected;
    else if(scenario==='TIMEOUT')observed=result.publicTimeoutObserved&&result.timedOut&&result.forced;
    else observed=result.exitCode===1&&canary?.scenario===scenario&&canary.observed&&canary.probeSucceeded&&canary.configurationAbsent;
    const passed=physicallyClosed(result)&&result.buildBindingSha256===binding&&observed&&!result.accepted;
    results.push({id:scenario,passed,receipt:result});
  }
  for(const malformedCase of ['ZERO_LENGTH','OVERSIZE','DUPLICATE','BOM','UTF8','TRAILING','LEGACY_V1']){
    const result=await runNative(control('HAPPY'),{malformedCase});
    const passed=physicallyClosed(result)&&result.buildBindingSha256===binding&&result.exitCode===1&&!result.accepted&&!result.rawOutputRejected&&!result.unexpectedManagedOutput&&
      (result.terminal===null||result.terminal.canary===null||result.terminal.canary.configurationCreated===false);
    results.push({id:malformedCase,passed,receipt:result});
  }
  const pass=id=>results.find(row=>row.id===id)?.passed===true;
  checks={SOURCE_BOUND:results.every(row=>row.receipt.sourceCurrent&&row.receipt.buildBindingSha256===binding),
    FRAMES_STRICT:['ZERO_LENGTH','OVERSIZE','DUPLICATE','BOM','UTF8','TRAILING','LEGACY_V1'].every(pass),ACK_REQUIRED:['ACK_OWNER','ACK_SEQUENCE','ACK_STAGE','ACK_TIMEOUT'].every(pass),
    OUTPUT_CONTAINED:pass('MANAGED_OUTPUT')&&pass('RAW_STDERR'),PROCESS_CLOSED:results.every(row=>row.receipt.processClosed&&row.receipt.processStartVerified&&row.receipt.helperProcessesClosed),
    SURFACES_REMOVED:results.every(row=>row.receipt.surfaceRemoved&&row.receipt.reconciled),PROXY_DIRECT:pass('PROXY')&&direct.state.requests===1&&proxy.state.requests===0,
    TELEMETRY_DISABLED:pass('TELEMETRY')&&telemetry.state.requests===0};
}catch{/* Fixed failed qualification, never raw diagnostics. */}
finally{for(const server of [direct,proxy,telemetry])if(server)try{await server.close();}catch{checks.PROCESS_CLOSED=false;}}
const passed=results.length===17&&results.every(row=>row.passed)&&Object.values(checks).every(value=>value===true);
const qualification={version:2,kind:'protocol',buildBindingSha256:binding,passed,checks};
const folder=resolve(root,'artifacts/p2-009-recovery-20261111');await mkdir(folder,{recursive:true});
if((await lstat(folder)).isSymbolicLink()||await realpath(folder)!==folder)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
// Preserve this actual run before updating the fixed fail-closed admission guard.
if((await lstat(folder)).isSymbolicLink()||await realpath(folder)!==folder)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const historyParent=resolve(folder,'native-protocol-qualification');await mkdir(historyParent,{recursive:true});
if((await lstat(historyParent)).isSymbolicLink()||await realpath(historyParent)!==historyParent)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const history=resolve(historyParent,randomUUID().replaceAll('-',''));await mkdir(history,{recursive:false});
if((await lstat(history)).isSymbolicLink()||await realpath(history)!==history)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const guardPath=resolve(folder,'native-protocol-qualification.json');
try{
  const prior=await lstat(guardPath);
  if(!prior.isFile()||prior.isSymbolicLink()||prior.size>65536||await realpath(guardPath)!==guardPath)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
  await writeFile(resolve(history,'previous-canonical.json'),await readFile(guardPath),{flag:'wx'});
}catch(error){if(error?.code!=='ENOENT')throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');}
const guardBytes=JSON.stringify(qualification,null,2)+'\n';
await writeFile(resolve(history,'qualification.json'),guardBytes,{flag:'wx'});
await writeFile(resolve(history,'details.json'),JSON.stringify({version:2,type:'nativeProtocolQualification',passed,results,checks},null,2)+'\n',{flag:'wx'});
await writeFile(guardPath,guardBytes);
process.stdout.write(JSON.stringify({status:passed?'PUBLIC_PROTOCOL_QUALIFIED':'PUBLIC_PROTOCOL_NOT_QUALIFIED',cases:results.length,passed:results.filter(row=>row.passed).length,
  buildBindingSha256:binding,receipt:'artifacts/p2-009-recovery-20261111/native-protocol-qualification.json'})+'\n');
process.exitCode=passed?0:1;
