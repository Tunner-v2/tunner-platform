import {mkdir,realpath,lstat,writeFile,readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {PrivateFrameReader} from '../../web/tests/totp/sensitive/private-frames.mjs';
import {publicFaults} from '../../web/tests/recovery/private/contract.mjs';
import {runRecoveryBrowser,runRecoveryBrowserProtocol} from './controller.mjs';

const root=await realpath(fileURLToPath(new URL('../../',import.meta.url)));
const protocolIds=Object.freeze(['REAL_EXACT_WINNING_MAP','REAL_EACH_MISSING_FALSE','REAL_EXTRA_CROSS_COMPONENT','CONDITIONAL_COORDINATOR_OWNER','STRICT_SCALAR_AND_KEY_SHAPE','SERVER_LIFECYCLE','PUBLIC_FAILURE_NEVER_REAL','RELAY_PUBLIC_TOPOLOGY','PRODUCTION_CONTROLLER_FACTS','PUBLIC_CONTAINMENT_NEGATIVES','RAW_MANAGED_OUTPUT','SOURCE_SELECTOR_COMPATIBILITY']);
const exact=(value,keys)=>value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).length===keys.length&&keys.every(key=>Object.hasOwn(value,key));
const digest=value=>typeof value==='string'&&/^[a-f0-9]{64}$/.test(value);
const checks=Object.fromEntries(publicFaults.map(id=>[id,false])),protocolChecks=Object.fromEntries(protocolIds.map(id=>[id,false])),results=[];
let protocol=null,workerBinding=null,nativeBinding=null,sourceCommit=null,sourceUnchanged=false;
try{
  if(process.argv.length!==2||publicFaults.length!==37||new Set(publicFaults).size!==37)throw new Error();
  sourceCommit=execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe'],timeout:10000}).trim();
  if(!/^[a-f0-9]{40}$/.test(sourceCommit))throw new Error();
  const reader=new PrivateFrameReader(process.stdin,{maximum:2048,total:4096,queue:1});let local;
  try{local=await reader.next(5000);}finally{reader.dispose();}
  if(!exact(local,['version','type','localDockerEndpoint','expectedEngineId'])||local.version!==2||local.type!=='qualifyRecoveryBrowserPublic')throw new Error();
  // The shared controller alone owns process creation, bounded framing, start
  // verification, independent ACK/reconciliation, helper teardown and resources.
  protocol=await runRecoveryBrowserProtocol();
  workerBinding=protocol.workerBuildBindingSha256;nativeBinding=protocol.buildBindingSha256;
  const protocolOk=protocol.version===2&&protocol.type==='browserProtocolControllerReceipt'&&protocol.qualified===true&&protocol.sourceCurrent===true&&protocol.processClosed===true&&protocol.exitCode===0&&protocol.rawOutputRejected===false&&protocol.unexpectedManagedOutput===false&&protocol.surfaceClean===true&&protocol.surfaceRemoved===true&&digest(workerBinding)&&digest(nativeBinding)&&exact(protocol.cases,protocolIds)&&protocolIds.every(id=>protocol.cases[id]===true);
  if(!protocolOk)throw new Error();
  for(const id of protocolIds)protocolChecks[id]=true;
  for(const publicFault of publicFaults){
    process.stdout.write(JSON.stringify({type:'BROWSER_PUBLIC_PROGRESS',publicFault})+'\n');
    const receipt=await runRecoveryBrowser('public:'+publicFault,{localDockerEndpoint:local.localDockerEndpoint,expectedEngineId:local.expectedEngineId});
    const passed=receipt.version===2&&receipt.type==='browserControllerReceipt'&&receipt.registration==='public:'+publicFault&&receipt.publicContainmentAccepted===true&&receipt.accepted===false&&receipt.sourceCurrent===true&&receipt.sourceCommit===sourceCommit&&receipt.protocolValid===true&&receipt.workerBuildBindingSha256===workerBinding&&receipt.buildBindingSha256===nativeBinding&&receipt.processStartVerified===true&&receipt.processClosed===true&&receipt.exitCode===0&&receipt.rawOutputRejected===false&&receipt.unexpectedManagedOutput===(publicFault==='unexpected-output')&&receipt.timedOut===false&&receipt.forced===false&&receipt.reconciled===true&&receipt.borrowedPreserved===true&&receipt.surfaceClean===true&&receipt.surfaceRemoved===true&&receipt.helperProcessesClosed===true&&Array.isArray(receipt.owners)&&receipt.owners.every(entry=>entry.absent===true);
    checks[publicFault]=passed;results.push({id:publicFault,passed,receipt});
    process.stdout.write(JSON.stringify({type:'BROWSER_PUBLIC_RESULT',publicFault,passed})+'\n');
  }
  sourceUnchanged=execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe'],timeout:10000}).trim()===sourceCommit;
}catch{/* Fixed failed qualification; raw exceptions/streams are never exported. */}
const passed=sourceUnchanged&&results.length===37&&Object.values(checks).every(value=>value===true)&&Object.values(protocolChecks).every(value=>value===true);
const qualification={version:2,kind:'browser-public',buildBindingSha256:workerBinding,nativeBuildBindingSha256:nativeBinding,passed,checks,protocolChecks};
const folder=resolve(root,'artifacts/p2-009-recovery-20261111');await mkdir(folder,{recursive:true});
if((await lstat(folder)).isSymbolicLink()||await realpath(folder)!==folder)throw new Error('BROWSER_PUBLIC_ARTIFACT_INVALID');
const runFolder=resolve(folder,'browser-public-qualification',sourceCommit??'source-unverified');await mkdir(runFolder,{recursive:true});
if((await lstat(runFolder)).isSymbolicLink()||await realpath(runFolder)!==runFolder)throw new Error('BROWSER_PUBLIC_ARTIFACT_INVALID');
const guardPath=resolve(folder,'browser-public-qualification.json');
try{
  const prior=await lstat(guardPath);
  if(!prior.isFile()||prior.isSymbolicLink()||prior.size>65536||await realpath(guardPath)!==guardPath)throw new Error('BROWSER_PUBLIC_ARTIFACT_INVALID');
  await writeFile(resolve(runFolder,'previous-canonical.json'),await readFile(guardPath),{flag:'wx'});
}catch(error){if(error?.code!=='ENOENT')throw new Error('BROWSER_PUBLIC_ARTIFACT_INVALID');}
const guardBytes=JSON.stringify(qualification,null,2)+'\n';
await writeFile(resolve(runFolder,'qualification.json'),guardBytes,{flag:'wx'});
await writeFile(resolve(runFolder,'details.json'),JSON.stringify({version:2,type:'browserPublicQualification',sourceCommit,sourceUnchanged,passed,protocol,results},null,2)+'\n',{flag:'wx'});
await writeFile(guardPath,guardBytes);
process.stdout.write(JSON.stringify({status:passed?'BROWSER_PUBLIC_QUALIFIED':'BROWSER_PUBLIC_NOT_QUALIFIED',completed:results.length,passed:results.filter(row=>row.passed).length,protocolCases:protocolIds.length,buildBindingSha256:workerBinding,nativeBuildBindingSha256:nativeBinding,receipt:'artifacts/p2-009-recovery-20261111/browser-public-qualification.json'})+'\n');
process.exitCode=passed?0:1;
