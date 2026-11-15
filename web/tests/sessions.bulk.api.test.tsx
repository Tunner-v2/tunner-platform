import {test,expect} from '@playwright/test';
import proposal from '../apps/user/sessions.content.proposal.json' with {type:'json'};
import {sessionContentKeys,sessionRevocationContentKeys} from '../apps/user/sessions.content-keys';
import {checkCurrentSession,parseSessionOptions,SessionHttpError} from '../apps/user/sessions.api';
import {activateSessionBulkRevocation,cancelSessionBulkRevocation,loadSessionBulkRevocationOptions,parseSessionBulkRevocationContext,parseSessionBulkRevocationOptions,parseSessionBulkRevocationResult,previewSessionBulkRevocation,readSessionBulkRevocationContext,readSessionBulkRevocationResult,replaySessionBulkRevocationPreview,sessionBulkRevocationContentKeys} from '../apps/user/sessions.bulk.api';

const requestId='11111111-1111-4111-8111-111111111111';
const otherRequest='22222222-2222-4222-8222-222222222222';
const first='33333333-3333-4333-8333-333333333333',second='44444444-4444-4444-8444-444444444444';
const root='/account/sessions/bulk-revocation';
const signal=()=>new AbortController().signal;
const original=()=>({requestId,operationReference:'synthetic-bulk-original-operation',status:'DRAFT' as const,activated:false,targets:[{reference:first,version:'9007199254740993'},{reference:second,version:'9223372036854775806'}],expiresAt:'2026-11-15T04:05:00Z'});
const revoked=()=>({requestId,operationReference:original().operationReference,status:'REVOKED',targets:[{reference:first,version:'9007199254740994',revokedAt:'2026-11-15T04:02:00Z'},{reference:second,version:'9223372036854775807',revokedAt:'2026-11-15T04:02:00Z'}]});
const published=()=>({locale:'en',content:Object.fromEntries(sessionBulkRevocationContentKeys.map(key=>[key,{key,text:'Synthetic test publication',locale:'en',state:'PUBLISHED',variantRef:'test-variant',versionRef:'test-version',publicationRef:'test-only-not-production'}]))});

test('Bulk publication is a separate complete12 capability with matching publication provenance',()=>{
 expect(sessionBulkRevocationContentKeys).toHaveLength(12);expect(sessionContentKeys).toHaveLength(74);expect(sessionRevocationContentKeys).toHaveLength(18);
 const legacy={locale:'en',content:Object.fromEntries(Object.entries(proposal.content).filter(([key])=>sessionContentKeys.includes(key as typeof sessionContentKeys[number])).map(([key,text])=>[key,{...published().content[sessionBulkRevocationContentKeys[0]]!,key,text}]))};
 expect(parseSessionOptions(legacy).locale).toBe('en');expect(()=>parseSessionBulkRevocationOptions(legacy,'en')).toThrow(SessionHttpError);
 expect(Object.keys(parseSessionBulkRevocationOptions(published(),'en').content)).toEqual([...sessionBulkRevocationContentKeys]);
 const additional=published();Object.assign(additional.content,{'account.sessions.bulk.description':{...additional.content[sessionBulkRevocationContentKeys[0]]!,key:'account.sessions.bulk.description',text:'Synthetic optional UI copy'}});expect(parseSessionBulkRevocationOptions(additional,'en').content['account.sessions.bulk.description']?.text).toBe('Synthetic optional UI copy');
 for(const key of sessionBulkRevocationContentKeys){const incomplete=published();delete (incomplete.content as Record<string,unknown>)[key];expect(()=>parseSessionBulkRevocationOptions(incomplete,'en')).toThrow(SessionHttpError);}
 for(const patch of [{key:'different'},{locale:'fr'},{state:'DRAFT'},{publicationRef:' '},{variantRef:''},{versionRef:''},{text:' '},{proofKey:'private'}]){
  const invalid=published();Object.assign(invalid.content[sessionBulkRevocationContentKeys[0]]!,patch);expect(()=>parseSessionBulkRevocationOptions(invalid,'en')).toThrow(SessionHttpError);
 }
 expect(()=>parseSessionBulkRevocationOptions(published(),'fr')).toThrow(SessionHttpError);
 expect(()=>parseSessionBulkRevocationOptions({...published(),csrfToken:'private'},'en')).toThrow(SessionHttpError);
});

test('Original projections retain canonical Int64 strings and reject private, partial or inconsistent sets',()=>{
 expect(parseSessionBulkRevocationContext(original()).targets[0]?.version).toBe('9007199254740993');
 for(const value of [null,{...original(),targets:[]},{...original(),targets:[original().targets[0],original().targets[0]]},{...original(),proofKey:'private'},{...original(),status:'REVOKED'},{...original(),status:'DRAFT',activated:true},{...original(),status:'PENDING',activated:false},{...original(),expiresAt:'tomorrow'},{...original(),requestId:'00000000-0000-0000-0000-000000000000'}])expect(()=>parseSessionBulkRevocationContext(value)).toThrow(SessionHttpError);
 for(const version of [9007199254740992,'0','01','+1','1e2','9223372036854775807','9223372036854775808'])expect(()=>parseSessionBulkRevocationContext({...original(),targets:[{reference:first,version}]})).toThrow(SessionHttpError);
 expect(()=>parseSessionBulkRevocationContext({...original(),targets:[{...original().targets[0],accountId:'private'}]})).toThrow(SessionHttpError);
 expect(parseSessionBulkRevocationContext({...original(),preparedAt:'2026-11-15T04:00:00Z'}).preparedAt).toBe('2026-11-15T04:00:00Z');expect(()=>parseSessionBulkRevocationContext({...original(),preparedAt:'invalid'})).toThrow(SessionHttpError);
});

test('Activation response cannot replace the preview request, correlation, expiry or manifest',()=>{
 const expected=parseSessionBulkRevocationContext(original());
 expect(parseSessionBulkRevocationContext({...original(),status:'PENDING',activated:true,targets:[...original().targets].reverse()},expected).activated).toBe(true);
 for(const patch of [{requestId:otherRequest},{operationReference:'replacement'},{expiresAt:'2026-11-15T05:05:00Z'},{targets:[original().targets[0]]},{targets:[{reference:first,version:'9007199254740994'},original().targets[1]]}])expect(()=>parseSessionBulkRevocationContext({...original(),...patch},expected)).toThrow(SessionHttpError);
 const timed=parseSessionBulkRevocationContext({...original(),preparedAt:'2026-11-15T04:00:00Z'});expect(()=>parseSessionBulkRevocationContext({...timed,preparedAt:'2026-11-15T04:01:00Z'},timed)).toThrow(SessionHttpError);
});

test('A result requires every exact original target with next version and a revocation time',()=>{
 const expected=parseSessionBulkRevocationContext(original());expect(parseSessionBulkRevocationResult({...revoked(),targets:[...revoked().targets].reverse()},expected).status).toBe('REVOKED');
 for(const value of [{...revoked(),requestId:otherRequest},{...revoked(),operationReference:'different'},{...revoked(),status:'COMPLETED'},{...revoked(),targets:revoked().targets.slice(0,1)},{...revoked(),targets:[revoked().targets[0],revoked().targets[0]]},{...revoked(),targets:[{...revoked().targets[0],version:'9007199254740993'},revoked().targets[1]]},{...revoked(),targets:[{...revoked().targets[0],reference:otherRequest},revoked().targets[1]]},{...revoked(),targets:[{reference:first,version:'9007199254740994'},revoked().targets[1]]},{...revoked(),targets:[{...revoked().targets[0],revokedAt:'invalid'},revoked().targets[1]]},{...revoked(),proofKey:'private'}])expect(()=>parseSessionBulkRevocationResult(value,expected)).toThrow(SessionHttpError);
});

test('New preview uses empty command, replay uses original requestId and existing session CSRF; no query selectors',async()=>{
 const before=globalThis.fetch,calls:{path:string;method:string;body:unknown;csrf:string|null;credentials:unknown;cache:unknown;redirect:unknown}[]=[];
 globalThis.fetch=async(input,init)=>{
  const path=String(input);calls.push({path,method:init?.method??'GET',body:init?.body===undefined?undefined:JSON.parse(String(init.body)),csrf:new Headers(init?.headers).get('X-Tunner-CSRF'),credentials:init?.credentials,cache:init?.cache,redirect:init?.redirect});
  if(path==='/authentication/session')return Response.json({authenticated:true,csrfToken:'fresh-existing-session-csrf'});
  if(path.endsWith('/options'))return Response.json(published());if(path.endsWith('/cancel'))return Response.json({requestId,status:'CANCELLED'});if(path.endsWith('/result'))return Response.json(revoked());
  return Response.json(path.endsWith('/activate')?{...original(),status:'PENDING',activated:true}:original());
 };
 try{
  const csrf=await checkCurrentSession(signal());await loadSessionBulkRevocationOptions('en',signal());const preview=await previewSessionBulkRevocation(csrf,signal());await replaySessionBulkRevocationPreview(csrf,{requestId:preview.requestId},signal());await readSessionBulkRevocationContext(signal());await activateSessionBulkRevocation(csrf,preview,signal());await cancelSessionBulkRevocation(csrf,{requestId},signal());await readSessionBulkRevocationResult(csrf,preview,signal());
  expect(calls.map(({path,method,body,csrf})=>({path,method,body,csrf}))).toEqual([
   {path:'/authentication/session',method:'GET',body:undefined,csrf:null},{path:root+'/options',method:'GET',body:undefined,csrf:null},
   {path:root+'/preview',method:'POST',body:{},csrf:'fresh-existing-session-csrf'},{path:root+'/preview',method:'POST',body:{requestId},csrf:'fresh-existing-session-csrf'},{path:root+'/context',method:'GET',body:undefined,csrf:null},
   ...['activate','cancel','result'].map(action=>({path:root+'/'+action,method:'POST',body:{requestId},csrf:'fresh-existing-session-csrf'})),
  ]);
  for(const call of calls){expect(call.path).not.toContain('?');expect(call.credentials).toBe('same-origin');expect(call.cache).toBe('no-store');expect(call.redirect).toBe('error');}
 }finally{globalThis.fetch=before;}
});

test('Malformed or extra commands fail before fetch and cancellation must match its original request',async()=>{
 const before=globalThis.fetch;let calls=0;globalThis.fetch=async()=>{calls++;return Response.json({requestId:otherRequest,status:'CANCELLED'});};
 try{
  for(const request of [{requestId:'invalid'},{requestId,targets:original().targets},{requestId,proofKey:'private'}])await expect(replaySessionBulkRevocationPreview('csrf',request,signal())).rejects.toBeInstanceOf(SessionHttpError);
  expect(calls).toBe(0);await expect(cancelSessionBulkRevocation('csrf',{requestId},signal())).rejects.toBeInstanceOf(SessionHttpError);expect(calls).toBe(1);
 }finally{globalThis.fetch=before;}
});

test('An original preview replay never accepts a replacement identity or recovers capability loss by rediscovery',async()=>{
 const before=globalThis.fetch,calls:{path:string;body:unknown}[]=[];
 try{
  globalThis.fetch=async(input,init)=>{calls.push({path:String(input),body:JSON.parse(String(init?.body))});return Response.json({...original(),requestId:otherRequest});};
  await expect(replaySessionBulkRevocationPreview('csrf',{requestId},signal())).rejects.toMatchObject({kind:'unavailable'});
  expect(calls).toEqual([{path:root+'/preview',body:{requestId}}]);calls.length=0;
  globalThis.fetch=async(input,init)=>{calls.push({path:String(input),body:JSON.parse(String(init?.body))});return Response.json({error_code:'NOT_FOUND_OR_NOT_DISCLOSABLE'},{status:404});};
  await expect(replaySessionBulkRevocationPreview('csrf',{requestId},signal())).rejects.toMatchObject({kind:'changed'});
  expect(calls).toEqual([{path:root+'/preview',body:{requestId}}]);
 }finally{globalThis.fetch=before;}
});

test('Only missing optional context is absence; result failures remain unconfirmed safe kinds without retries',async()=>{
 const before=globalThis.fetch,calls:string[]=[];
 try{
  globalThis.fetch=async()=>Response.json({privateDetail:'secret-server-detail'},{status:404});expect(await readSessionBulkRevocationContext(signal())).toBeUndefined();expect(await loadSessionBulkRevocationOptions('en',signal())).toBeUndefined();
  for(const [status,kind] of [[401,'authentication'],[403,'authentication'],[404,'changed'],[409,'changed'],[503,'unavailable']] as const){
   calls.length=0;globalThis.fetch=async input=>{calls.push(String(input));return Response.json({error_code:'RESULT_UNCONFIRMED',privateDetail:'secret-server-detail'},{status});};
   await expect(readSessionBulkRevocationResult('csrf',parseSessionBulkRevocationContext(original()),signal())).rejects.toMatchObject({kind});expect(calls).toEqual([root+'/result']);
   if(status!==404)await expect(readSessionBulkRevocationContext(signal())).rejects.toMatchObject({kind});
   if(status===401||status===403)await expect(loadSessionBulkRevocationOptions('en',signal())).rejects.toMatchObject({kind});
   try{await activateSessionBulkRevocation('csrf',parseSessionBulkRevocationContext(original()),signal());throw new Error('Expected rejection');}catch(error){expect(error).toBeInstanceOf(SessionHttpError);expect(JSON.stringify(error)).not.toContain('secret-server-detail');expect((error as Error).message).not.toContain('RESULT_UNCONFIRMED');}
  }
  calls.length=0;globalThis.fetch=async input=>{calls.push(String(input));throw new Error('lost response with private detail');};await expect(previewSessionBulkRevocation('csrf',signal())).rejects.toMatchObject({kind:'unavailable'});expect(calls).toEqual([root+'/preview']);
 }finally{globalThis.fetch=before;}
});

test('Malformed optional publication remains unavailable while an aborted load is an error',async()=>{
 const before=globalThis.fetch;
 try{globalThis.fetch=async()=>Response.json({locale:'en',content:{}});expect(await loadSessionBulkRevocationOptions('en',signal())).toBeUndefined();const controller=new AbortController();controller.abort();await expect(loadSessionBulkRevocationOptions('en',controller.signal)).rejects.toBeInstanceOf(SessionHttpError);}
 finally{globalThis.fetch=before;}
});
