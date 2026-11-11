import {randomUUID} from 'node:crypto';
import {mkdir, realpath, writeFile, lstat, readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {PrivateFrameReader} from '../../web/tests/totp/sensitive/private-frames.mjs';
import {runNative} from './controller.mjs';

const root=await realpath(fileURLToPath(new URL('../../',import.meta.url)));
const ids=['SOURCE_BOUND','OWNERS_RETAINED','CREATED_BEFORE_START','FORCED_CLOSE_VERIFIED','RECONCILED_EXACT','CONTAINERS_ABSENT','VOLUMES_ABSENT','CONFIGURATIONS_ABSENT','BORROWED_PRESERVED','CAPACITY_VERIFIED'];
const results=[];let binding=null;let checks=Object.fromEntries(ids.map(id=>[id,false]));
try{
  if(process.argv.length!==2)throw new Error('NATIVE_PUBLIC_INPUT_INVALID');
  const reader=new PrivateFrameReader(process.stdin,{maximum:2048,total:4096,queue:1});let local;
  try{local=await reader.next(5000);}finally{reader.dispose();}
  if(!local||Object.keys(local).sort().join('|')!=='expectedEngineId|localDockerEndpoint|type|version'||local.version!==2||local.type!=='qualifyNativeResources')throw new Error('NATIVE_PUBLIC_INPUT_INVALID');
  const control={cohort:'PUBLIC_CANARIES',publicFault:null,localDockerEndpoint:local.localDockerEndpoint,expectedEngineId:local.expectedEngineId,publicProbeOrigin:null,publicTelemetryOrigin:null};
  for(const cut of [null,'PREPARED','CREATED','AFTER_START','AFTER_MIGRATION']){
    const receipt=await runNative(control,{resourceCut:cut});binding??=receipt.buildBindingSha256;
    const physicallyClosed=receipt.sourceCurrent&&receipt.buildBindingSha256===binding&&receipt.processStartVerified&&receipt.processClosed&&receipt.helperProcessesClosed&&receipt.reconciled&&receipt.borrowedPreserved&&receipt.surfaceRemoved&&receipt.owners.every(owner=>owner.absent);
    const expected=cut===null?receipt.accepted&&receipt.terminal?.native?.sensitiveAccepted===false:receipt.cutObserved&&receipt.forced&&!receipt.accepted;
    results.push({cut,passed:physicallyClosed&&expected,receipt});
  }
  const capacity=await runNative({...control,cohort:'PUBLIC_CAPACITY'});
  results.push({cut:'CAPACITY',passed:capacity.accepted&&capacity.sourceCurrent&&capacity.buildBindingSha256===binding&&capacity.capacity!==null&&capacity.processClosed&&capacity.surfaceRemoved&&capacity.borrowedPreserved&&capacity.reconciled,receipt:capacity});
  const all=results.length===6&&results.every(result=>result.passed);
  checks={SOURCE_BOUND:all&&results.every(result=>result.receipt.sourceCurrent),OWNERS_RETAINED:all&&results.every(result=>result.receipt.owners.length>0),
    CREATED_BEFORE_START:all&&results.find(result=>result.cut==='CREATED')?.receipt.owners.some(owner=>owner.containerId&&owner.preparationVerified)===true,
    FORCED_CLOSE_VERIFIED:all&&results.filter(result=>result.cut!==null&&result.cut!=='CAPACITY').every(result=>result.receipt.cutObserved&&result.receipt.processClosed),
    RECONCILED_EXACT:all&&results.every(result=>result.receipt.reconciled),CONTAINERS_ABSENT:all&&results.every(result=>result.receipt.owners.filter(owner=>owner.owner.role!=='config').every(owner=>owner.absent)),
    VOLUMES_ABSENT:all&&results.every(result=>result.receipt.reconciled),CONFIGURATIONS_ABSENT:all&&results.every(result=>result.receipt.owners.filter(owner=>owner.owner.role==='config').every(owner=>owner.absent)),
    BORROWED_PRESERVED:all&&results.every(result=>result.receipt.borrowedPreserved),CAPACITY_VERIFIED:all&&capacity.accepted&&capacity.capacity!==null};
}catch{/* No raw diagnostics or private values. */}
const passed=results.length===6&&results.every(result=>result.passed)&&Object.values(checks).every(value=>value===true);
const folder=resolve(root,'artifacts/p2-009-recovery-20261111');await mkdir(folder,{recursive:true});
// Preserve this actual run before updating the fixed fail-closed admission guard.
if((await lstat(folder)).isSymbolicLink()||await realpath(folder)!==folder)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const historyParent=resolve(folder,'native-resource-qualification');await mkdir(historyParent,{recursive:true});
if((await lstat(historyParent)).isSymbolicLink()||await realpath(historyParent)!==historyParent)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const history=resolve(historyParent,randomUUID().replaceAll('-',''));await mkdir(history,{recursive:false});
if((await lstat(history)).isSymbolicLink()||await realpath(history)!==history)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
const guardPath=resolve(folder,'native-resource-qualification.json');
try{
  const prior=await lstat(guardPath);
  if(!prior.isFile()||prior.isSymbolicLink()||prior.size>65536||await realpath(guardPath)!==guardPath)throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');
  await writeFile(resolve(history,'previous-canonical.json'),await readFile(guardPath),{flag:'wx'});
}catch(error){if(error?.code!=='ENOENT')throw new Error('NATIVE_PUBLIC_ARTIFACT_INVALID');}
const guardBytes=JSON.stringify({version:2,kind:'resources',buildBindingSha256:binding,passed,checks},null,2)+'\n';
await writeFile(resolve(history,'qualification.json'),guardBytes,{flag:'wx'});
await writeFile(resolve(history,'details.json'),JSON.stringify({version:2,type:'nativeResourceQualification',passed,results,checks},null,2)+'\n',{flag:'wx'});
await writeFile(guardPath,guardBytes);
process.stdout.write(JSON.stringify({status:passed?'PUBLIC_RESOURCE_QUALIFIED':'PUBLIC_RESOURCE_NOT_QUALIFIED',cases:results.length,passed:results.filter(result=>result.passed).length,
  buildBindingSha256:binding,receipt:'artifacts/p2-009-recovery-20261111/native-resource-qualification.json'})+'\n');
process.exitCode=passed?0:1;
