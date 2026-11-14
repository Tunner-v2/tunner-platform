import {test,expect} from '@playwright/test';
import proposal from '../apps/user/sessions.content.proposal.json' with {type:'json'};
import {sessionContentKeys,sessionRevocationContentKeys} from '../apps/user/sessions.content-keys';
import {checkCurrentSession,loadSessionContext,loadSessionRevocationOptions,parseSessionOptions,parseSessionPage,parseSessionRevocationContext,parseSessionRevocationResult,prepareSessionRevocation,readSessionRevocationContext,readSessionRevocationResult,readSessionPage,SessionHttpError,signOutCurrentSession} from '../apps/user/sessions.api';

const reference='11111111-1111-4111-8111-111111111111';
const item=()=>({reference,version:'9007199254740993',isCurrent:true,authenticatedAt:'2026-11-14T02:00:00Z',lastSeenAt:'2026-11-14T04:00:00Z',expiresAt:'2026-11-15T02:00:00Z',recordedState:'ACTIVE',effectiveState:'ACTIVE',evaluatedAt:'2026-11-14T04:01:00Z'});
const published=()=>({locale:'en',content:Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'synthetic-review-variant',versionRef:'synthetic-review-version',publicationRef:'synthetic-test-publication-not-production'}]))});
test('Exact safe DTO preserves long versions as strings and rejects private/unrecognized fields',()=>{
 const page=parseSessionPage({sessions:[item()],hasMore:false,continuation:null});expect(page.sessions[0]?.version).toBe('9007199254740993');
 for(const change of [{version:9007199254740992},{version:'01'},{version:'9223372036854775808'},{accountId:'private'},{effectiveState:'UNKNOWN'},{reference:'00000000-0000-0000-0000-000000000000'}])expect(()=>parseSessionPage({sessions:[{...item(),...change}],hasMore:false,continuation:null})).toThrow(SessionHttpError);
});
test('Malformed, duplicate or inconsistent pages fail closed without making a selection',()=>{
 for(const value of [null,[],{sessions:[],hasMore:true,continuation:'protected'},{sessions:[item(),item()],hasMore:false,continuation:null},{sessions:[item()],hasMore:true,continuation:null},{sessions:[item()],hasMore:false,continuation:'protected'}])expect(()=>parseSessionPage(value)).toThrow(SessionHttpError);
});
test('Every required key must be published with matching locale/provenance; draft copy is not bootstrap',()=>{
 expect(Object.keys(proposal.content).sort()).toEqual([...sessionContentKeys,...sessionRevocationContentKeys].sort());expect(sessionContentKeys).toHaveLength(74);expect(parseSessionOptions(published()).locale).toBe('en');expect(()=>parseSessionOptions(proposal)).toThrow(SessionHttpError);
 const missing=published();delete (missing.content as Record<string,unknown>)['account.sessions.loading'];expect(()=>parseSessionOptions(missing)).toThrow(SessionHttpError);
 const draft=published();draft.content['brand.tunner']!.state='DRAFT';expect(()=>parseSessionOptions(draft)).toThrow(SessionHttpError);
});

const ownerRequest={requestId:'44444444-4444-4444-8444-444444444444',targetReference:reference,expectedVersion:'9007199254740993'};
const original=()=>({requestId:ownerRequest.requestId,operationReference:'fixture-owned-session-operation',status:'PENDING' as const,target:{reference,version:ownerRequest.expectedVersion,lastSeenAt:'2026-11-14T04:00:00Z'},expiresAt:'2026-11-14T04:05:00Z'});
const revoked=()=>({requestId:ownerRequest.requestId,operationReference:original().operationReference,status:'REVOKED',target:{...original().target,version:'9007199254740994'},revokedAt:'2026-11-14T04:02:00Z'});
test('Legacy74 listing publications remain valid; revocation requires separate complete matching publication',async()=>{
 const legacy=published();for(const key of sessionRevocationContentKeys)delete (legacy.content as Record<string,unknown>)[key];expect(parseSessionOptions(legacy).locale).toBe('en');
 const before=globalThis.fetch;try{
  globalThis.fetch=async()=>Response.json({error_code:'NOT_FOUND'},{status:404});expect(await loadSessionRevocationOptions('en',new AbortController().signal)).toBeUndefined();expect(await readSessionRevocationContext(new AbortController().signal)).toBeUndefined();
  globalThis.fetch=async()=>Response.json(legacy);expect(await loadSessionRevocationOptions('en',new AbortController().signal)).toBeUndefined();
  globalThis.fetch=async()=>Response.json(published());expect(await loadSessionRevocationOptions('en',new AbortController().signal)).toBeDefined();expect(await loadSessionRevocationOptions('fr',new AbortController().signal)).toBeUndefined();
 }finally{globalThis.fetch=before;}
});
test('Protected original projection is strict and bound to frozen owner request, target and string version',()=>{
 expect(parseSessionRevocationContext(original(),ownerRequest).target.version).toBe('9007199254740993');
 for(const value of [{...original(),requestId:reference},{...original(),target:{...original().target,version:'9007199254740994'}},{...original(),proofKey:'private'},{...original(),target:{...original().target,version:9007199254740992}}])expect(()=>parseSessionRevocationContext(value,ownerRequest)).toThrow(SessionHttpError);
});
test('Only the exact causal result with next string version is accepted as signed out',()=>{
 const context=parseSessionRevocationContext(original());expect(parseSessionRevocationResult(revoked(),context).status).toBe('REVOKED');
 for(const value of [{...revoked(),operationReference:'other-owner'},{...revoked(),requestId:reference},{...revoked(),status:'COMPLETED'},{...revoked(),target:{...revoked().target,version:ownerRequest.expectedVersion}},{...revoked(),target:{...revoked().target,lastSeenAt:'2026-11-14T04:03:00Z'}},{...revoked(),proofKey:'private'}])expect(()=>parseSessionRevocationResult(value,context)).toThrow(SessionHttpError);
});
test('Prepare sends only frozen selection; result checks only original requestId with current CSRF',async()=>{
 const before=globalThis.fetch,calls:{path:string;body:unknown;csrf:string|null}[]=[];globalThis.fetch=async(input,init)=>{calls.push({path:String(input),body:JSON.parse(String(init?.body)),csrf:new Headers(init?.headers).get('X-Tunner-CSRF')});return Response.json(String(input).endsWith('/prepare')?original():revoked());};
 try{const context=await prepareSessionRevocation('fresh-csrf',ownerRequest,new AbortController().signal);await readSessionRevocationResult('fresh-csrf',context,new AbortController().signal);expect(calls).toEqual([{path:'/account/sessions/revocation/prepare',body:ownerRequest,csrf:'fresh-csrf'},{path:'/account/sessions/revocation/result',body:{requestId:ownerRequest.requestId},csrf:'fresh-csrf'}]);}finally{globalThis.fetch=before;}
});
test('An unavailable original result is one read and never an automatic prepare or proof replay',async()=>{
 const before=globalThis.fetch,paths:string[]=[];globalThis.fetch=async input=>{paths.push(String(input));return Response.json({privateDetail:'never-present'},{status:409});};
 try{await expect(readSessionRevocationResult('csrf',parseSessionRevocationContext(original()),new AbortController().signal)).rejects.toBeInstanceOf(SessionHttpError);expect(paths).toEqual(['/account/sessions/revocation/result']);}finally{globalThis.fetch=before;}
});
test('Authenticated CSRF precedes published options and never uses anonymous authentication options',async()=>{
 const before=globalThis.fetch,paths:string[]=[];globalThis.fetch=async input=>{paths.push(String(input));return Response.json(String(input)==='/authentication/session'?{authenticated:true,csrfToken:'current-principal-csrf'}:published());};
 try{const result=await loadSessionContext(new AbortController().signal);expect(result.csrfToken).toBe('current-principal-csrf');expect(paths).toEqual(['/authentication/session','/account/sessions/options']);}finally{globalThis.fetch=before;}
});
test('Continuation is only a POST body value, with no browser selector/budget/query or private error retention',async()=>{
 const before=globalThis.fetch,requests:{path:string;body:unknown;csrf:string|null}[]=[];globalThis.fetch=async(input,init)=>{requests.push({path:String(input),body:JSON.parse(String(init?.body)),csrf:new Headers(init?.headers).get('X-Tunner-CSRF')});return Response.json({sessions:[item()],hasMore:false,continuation:null});};
 try{await readSessionPage('current-csrf','protected-body-only',new AbortController().signal);expect(requests).toEqual([{path:'/account/sessions/list',body:{continuation:'protected-body-only'},csrf:'current-csrf'}]);}finally{globalThis.fetch=before;}
});
test('HTTP failures use closed safe kinds and do not retain server/private problem content',async()=>{
 const before=globalThis.fetch;
 try{for(const [status,kind] of [[401,'authentication'],[403,'authentication'],[404,'changed'],[409,'changed'],[503,'unavailable']] as const){globalThis.fetch=async()=>Response.json({error_code:'VERSION_CONFLICT',privateDetail:'not-for-presentation'},{status});try{await readSessionPage('csrf',null,new AbortController().signal);throw new Error('Expected rejection');}catch(error){expect(error).toBeInstanceOf(SessionHttpError);expect((error as SessionHttpError).kind).toBe(kind);expect(JSON.stringify(error)).not.toContain('not-for-presentation');}}}finally{globalThis.fetch=before;}
});
test('Lost ordinary sign-out is not retried; checking status is a separate authenticated read',async()=>{
 const before=globalThis.fetch,requests:string[]=[];globalThis.fetch=async(input,init)=>{requests.push((init?.method??'GET')+' '+String(input));if(String(input)==='/authentication/sign-out')throw new Error('synthetic response loss');return Response.json({authenticated:true,csrfToken:'fresh-current-csrf'});};
 try{await expect(signOutCurrentSession('csrf',new AbortController().signal)).rejects.toBeInstanceOf(SessionHttpError);expect(await checkCurrentSession(new AbortController().signal)).toBe('fresh-current-csrf');expect(requests).toEqual(['POST /authentication/sign-out','GET /authentication/session']);}finally{globalThis.fetch=before;}
});
