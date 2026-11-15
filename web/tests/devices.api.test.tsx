import {test,expect} from '@playwright/test';
import {checkCurrentSession,SessionHttpError} from '../apps/user/sessions.api';
import {deviceContentKeys,loadDeviceOptions,parseDeviceOptions,parseDevicePage,parseDeviceTrustRemovalContext,parseDeviceTrustRemovalResult,prepareDeviceTrustRemoval,readDeviceDetails,readDevicePage,readDeviceTrustRemovalContext,readDeviceTrustRemovalResult} from '../apps/user/devices.api';

const requestId='11111111-1111-4111-8111-111111111111',reference='22222222-2222-4222-8222-222222222222',other='33333333-3333-4333-8333-333333333333';
const signal=()=>new AbortController().signal;
const item=()=>({reference,version:'9007199254740993',trustVersion:'9223372036854775806',observedForCurrentSession:true,firstObservedAt:'2026-11-14T02:00:00Z',lastObservedAt:'2026-11-15T04:00:00Z',characteristics:{BrowserFamily:'Chrome'},recordedTrustState:'TRUSTED',effectiveTrustState:'TRUSTED',evaluatedAt:'2026-11-15T04:01:00Z'});
const page=()=>({devices:[item()],hasMore:false,continuation:null});
const published=()=>({requestId,locale:'en',content:Object.fromEntries(deviceContentKeys.map(key=>[key,{key,text:'Synthetic test copy',locale:'en',state:'PUBLISHED',variantRef:'synthetic-variant',versionRef:'synthetic-version',publicationRef:'test-only-not-production'}]))});
const confirmed=()=>({requestId,reference,expectedVersion:item().version,expectedTrustVersion:item().trustVersion,confirmed:true as const});
const original=()=>({requestId,operationReference:'synthetic-device-original-operation',status:'PENDING' as const,target:{reference,version:item().version,trustVersion:item().trustVersion},sessionEffect:'PRESERVE_EXISTING_SESSIONS' as const,expiresAt:'2026-11-15T04:05:00Z'});
const revoked=()=>({requestId,operationReference:original().operationReference,status:'REVOKED',target:{reference,version:item().version,priorTrustVersion:item().trustVersion,trustVersion:'9223372036854775807'},revokedAt:'2026-11-15T04:02:00Z',sessionEffect:'PRESERVE_EXISTING_SESSIONS'});

test('Device draft options require their independent78 published keys and safe provenance',()=>{
 expect(deviceContentKeys).toHaveLength(78);expect(new Set(deviceContentKeys).size).toBe(78);expect(deviceContentKeys.every(key=>key.startsWith('account.devices.')||key==='brand.tunner')).toBe(true);expect(parseDeviceOptions(published()).requestId).toBe(requestId);
 for(const key of deviceContentKeys){const missing=published();delete (missing.content as Record<string,unknown>)[key];expect(()=>parseDeviceOptions(missing)).toThrow(SessionHttpError);}
 for(const patch of [{key:'other'},{locale:'fr'},{state:'DRAFT'},{text:' '},{publicationRef:''},{variantRef:''},{versionRef:''},{rawMetadata:'private'}]){const invalid=published();Object.assign(invalid.content[deviceContentKeys[0]]!,patch);expect(()=>parseDeviceOptions(invalid)).toThrow(SessionHttpError);}
 for(const value of [{...published(),requestId:undefined},{...published(),requestId:'00000000-0000-0000-0000-000000000000'},{...published(),installationToken:'private'}])expect(()=>parseDeviceOptions(value)).toThrow(SessionHttpError);
});

test('Strict Device projections preserve Int64 versions and expose only qualified safe characteristics',()=>{
 expect(parseDevicePage(page()).devices[0]?.version).toBe('9007199254740993');expect(parseDevicePage({...page(),devices:[{...item(),version:'9223372036854775807',trustVersion:'9223372036854775807'}]}).devices).toHaveLength(1);
 for(const patch of [{version:9007199254740992},{version:'01'},{trustVersion:'0'},{trustVersion:'9223372036854775808'},{effectiveTrustState:'UNKNOWN'},{reference:'00000000-0000-0000-0000-000000000000'},{characteristics:{BrowserFamily:123}},{characteristics:{BrowserFamily:' '}},{rawMetadata:'private'},{installationToken:'private'},{accountId:'private'},{firstObservedAt:'invalid'}])expect(()=>parseDevicePage({...page(),devices:[{...item(),...patch}]})).toThrow(SessionHttpError);
});

test('Duplicate observations and inconsistent pagination fail closed without a device selection',()=>{
 for(const value of [null,{devices:[],hasMore:true,continuation:'opaque'},{...page(),hasMore:true},{...page(),continuation:'opaque'},{...page(),devices:[item(),item()]},{...page(),devices:[item(),{...item(),reference:other}]}])expect(()=>parseDevicePage(value)).toThrow(SessionHttpError);
 expect(parseDevicePage({...page(),devices:[item(),{...item(),reference:other,observedForCurrentSession:false}],hasMore:true,continuation:'protected-body-only'}).hasMore).toBe(true);
});

test('Prepared original binds the explicit confirmed device/trust versions and preserves the session effect',()=>{
 const expected=confirmed();expect(parseDeviceTrustRemovalContext(original(),expected).target.trustVersion).toBe('9223372036854775806');
 for(const patch of [{requestId:other},{status:'DRAFT'},{sessionEffect:'SIGN_OUT'},{proofKey:'private'},{target:{...original().target,reference:other}},{target:{...original().target,version:'9007199254740994'}},{target:{...original().target,trustVersion:'9223372036854775807'}}])expect(()=>parseDeviceTrustRemovalContext({...original(),...patch},expected)).toThrow(SessionHttpError);
 const frozen=parseDeviceTrustRemovalContext(original());for(const patch of [{operationReference:'replacement'},{expiresAt:'2026-11-15T05:05:00Z'}])expect(()=>parseDeviceTrustRemovalContext({...original(),...patch},frozen)).toThrow(SessionHttpError);
});

test('Only a causal same-device result with unchanged device version and next trust version confirms removal',()=>{
 const expected=parseDeviceTrustRemovalContext(original());expect(parseDeviceTrustRemovalResult(revoked(),expected).target.trustVersion).toBe('9223372036854775807');
 const maximumDeviceVersion='9223372036854775807';const unchangedDevice=parseDeviceTrustRemovalContext({...original(),target:{...original().target,version:maximumDeviceVersion}},{...confirmed(),expectedVersion:maximumDeviceVersion});expect(parseDeviceTrustRemovalResult({...revoked(),target:{...revoked().target,version:maximumDeviceVersion}},unchangedDevice).target.version).toBe(maximumDeviceVersion);
 for(const value of [{...revoked(),requestId:other},{...revoked(),operationReference:'replacement'},{...revoked(),status:'COMPLETED'},{...revoked(),sessionEffect:'REVOKE_SESSIONS'},{...revoked(),target:{...revoked().target,reference:other}},{...revoked(),target:{...revoked().target,version:'9007199254740994'}},{...revoked(),target:{...revoked().target,priorTrustVersion:'1'}},{...revoked(),target:{...revoked().target,trustVersion:item().trustVersion}},{...revoked(),revokedAt:'tomorrow'},{...revoked(),target:{...revoked().target,rawMetadata:'private'}}])expect(()=>parseDeviceTrustRemovalResult(value,expected)).toThrow(SessionHttpError);
});

test('Existing session CSRF protects exact body-only Device commands; options alone never select an owner',async()=>{
 const before=globalThis.fetch,calls:{path:string;method:string;body:unknown;csrf:string|null;credentials:unknown;cache:unknown;redirect:unknown}[]=[];
 globalThis.fetch=async(input,init)=>{
  const path=String(input);calls.push({path,method:init?.method??'GET',body:init?.body===undefined?undefined:JSON.parse(String(init.body)),csrf:new Headers(init?.headers).get('X-Tunner-CSRF'),credentials:init?.credentials,cache:init?.cache,redirect:init?.redirect});
  return Response.json(path==='/authentication/session'?{authenticated:true,csrfToken:'fresh-existing-session-csrf'}:path.endsWith('/options')?published():path.endsWith('/list')?page():path.endsWith('/details')?{device:item()}:path.endsWith('/result')?revoked():original());
 };
 try{
  const csrf=await checkCurrentSession(signal()),options=await loadDeviceOptions(signal());expect(options?.requestId).toBe(requestId);expect(calls.map(call=>call.path)).toEqual(['/authentication/session','/account/devices/options']);
  await readDevicePage(csrf,undefined,signal());await readDevicePage(csrf,'protected-continuation-only-in-body',signal());await readDeviceDetails(csrf,reference,signal());const prepared=await prepareDeviceTrustRemoval(csrf,confirmed(),signal());await readDeviceTrustRemovalContext(signal());await readDeviceTrustRemovalResult(csrf,prepared,signal());
  expect(calls.slice(2).map(({path,method,body,csrf})=>({path,method,body,csrf}))).toEqual([
   {path:'/account/devices/list',method:'POST',body:{},csrf:'fresh-existing-session-csrf'},
   {path:'/account/devices/list',method:'POST',body:{continuation:'protected-continuation-only-in-body'},csrf:'fresh-existing-session-csrf'},
   {path:'/account/devices/details',method:'POST',body:{reference},csrf:'fresh-existing-session-csrf'},
   {path:'/account/devices/prepare',method:'POST',body:confirmed(),csrf:'fresh-existing-session-csrf'},
   {path:'/account/devices/context',method:'GET',body:undefined,csrf:null},
   {path:'/account/devices/result',method:'POST',body:{requestId},csrf:'fresh-existing-session-csrf'},
  ]);
  for(const call of calls){expect(call.path).not.toContain('?');expect(call.credentials).toBe('same-origin');expect(call.cache).toBe('no-store');expect(call.redirect).toBe('error');}
 }finally{globalThis.fetch=before;}
});

test('Unconfirmed, malformed and extra commands stop before transport; selected details must match',async()=>{
 const before=globalThis.fetch;let calls=0;globalThis.fetch=async()=>{calls++;return Response.json({device:{...item(),reference:other}});};
 try{
  for(const patch of [{confirmed:false},{confirmed:undefined},{expectedTrustVersion:'9223372036854775807'},{expectedVersion:'01'},{requestId:'invalid'},{accountId:'private'},{proofKey:'private'}])await expect(prepareDeviceTrustRemoval('csrf',{...confirmed(),...patch} as never,signal())).rejects.toBeInstanceOf(SessionHttpError);
  await expect(readDevicePage('csrf',null as never,signal())).rejects.toBeInstanceOf(SessionHttpError);await expect(readDevicePage('csrf','',signal())).rejects.toBeInstanceOf(SessionHttpError);expect(calls).toBe(0);
  await expect(readDeviceDetails('csrf',reference,signal())).rejects.toBeInstanceOf(SessionHttpError);expect(calls).toBe(1);
 }finally{globalThis.fetch=before;}
});

test('Context/options absence stays distinct from authentication, changed and unavailable failures',async()=>{
 const before=globalThis.fetch,paths:string[]=[];
 try{
  globalThis.fetch=async input=>{paths.push(String(input));return Response.json({privateDetail:'not-for-presentation'},{status:404});};
  expect(await loadDeviceOptions(signal())).toBeUndefined();expect(await readDeviceTrustRemovalContext(signal())).toBeUndefined();expect(paths).toEqual(['/account/devices/options','/account/devices/context']);
  for(const [status,kind] of [[401,'authentication'],[403,'authentication'],[404,'changed'],[409,'changed'],[503,'unavailable']] as const){
   paths.length=0;globalThis.fetch=async input=>{paths.push(String(input));return Response.json({error_code:'RESULT_UNCONFIRMED',privateDetail:'not-for-presentation'},{status});};
   await expect(readDeviceTrustRemovalResult('csrf',parseDeviceTrustRemovalContext(original()),signal())).rejects.toMatchObject({kind});expect(paths).toEqual(['/account/devices/result']);
   if(status!==404){await expect(loadDeviceOptions(signal())).rejects.toMatchObject({kind});await expect(readDeviceTrustRemovalContext(signal())).rejects.toMatchObject({kind});}
   try{await readDeviceDetails('csrf',reference,signal());throw new Error('Expected rejection');}catch(error){expect(error).toBeInstanceOf(SessionHttpError);expect(JSON.stringify(error)).not.toContain('not-for-presentation');expect((error as Error).message).not.toContain('RESULT_UNCONFIRMED');}
  }
 }finally{globalThis.fetch=before;}
});

test('Lost prepare/result responses and malformed context never retry, renew options or resnapshot',async()=>{
 const before=globalThis.fetch,paths:string[]=[];
 try{
  globalThis.fetch=async input=>{paths.push(String(input));throw new Error('synthetic response loss');};
  await expect(prepareDeviceTrustRemoval('csrf',confirmed(),signal())).rejects.toMatchObject({kind:'unavailable'});await expect(readDeviceTrustRemovalResult('csrf',parseDeviceTrustRemovalContext(original()),signal())).rejects.toMatchObject({kind:'unavailable'});expect(paths).toEqual(['/account/devices/prepare','/account/devices/result']);
  paths.length=0;globalThis.fetch=async input=>{paths.push(String(input));return Response.json({...original(),sessionEffect:'UNKNOWN'});};await expect(readDeviceTrustRemovalContext(signal())).rejects.toMatchObject({kind:'unavailable'});expect(paths).toEqual(['/account/devices/context']);
  globalThis.fetch=async()=>Response.json({...published(),content:{}});await expect(loadDeviceOptions(signal())).rejects.toBeInstanceOf(SessionHttpError);
 }finally{globalThis.fetch=before;}
});
