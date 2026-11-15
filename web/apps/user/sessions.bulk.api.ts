import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson,requirePublished} from './passkeys.api';
import {SessionHttpError} from './sessions.api';

/** Conditional bulk publication; listing and single-session requirements remain independent. */
export const sessionBulkRevocationContentKeys=[
 'account.sessions.bulk.action','account.sessions.bulk.review.title','account.sessions.bulk.review.description',
 'account.sessions.bulk.review.originalSet','account.sessions.bulk.confirm','account.sessions.bulk.cancel',
 'account.sessions.bulk.loading','account.sessions.bulk.empty','account.sessions.bulk.changed',
 'account.sessions.bulk.completed','account.sessions.bulk.unknown','account.sessions.bulk.check',
] as const;
const root='/account/sessions/bulk-revocation';
const reference=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const version=z.string().regex(/^[1-9][0-9]*$/).refine(value=>BigInt(value)<=9223372036854775807n);
const timestamp=z.iso.datetime({offset:true});
const text=z.string().refine(value=>value.trim().length>0);
const publication=z.strictObject({key:text,variantRef:text,versionRef:text,publicationRef:text,locale:text,state:z.literal('PUBLISHED'),text});
const options=z.strictObject({locale:text,content:z.record(z.string(),publication)});
const command=z.strictObject({requestId:reference});
const target=z.strictObject({reference,version:version.refine(value=>BigInt(value)<9223372036854775807n)});
const context=z.strictObject({requestId:reference,operationReference:text,status:z.enum(['DRAFT','PENDING','CHANGED','COMPLETED']),activated:z.boolean(),targets:z.array(target).min(1),expiresAt:timestamp});
const cancellation=z.strictObject({requestId:reference,status:z.literal('CANCELLED')});
const result=z.strictObject({requestId:reference,operationReference:text,status:z.literal('REVOKED'),targets:z.array(z.strictObject({reference,version,revokedAt:timestamp})).min(1)});
export type SessionBulkRevocationOptions=z.infer<typeof options>;
export type SessionBulkRevocationRequest=z.infer<typeof command>;
export type SessionBulkRevocationContext=z.infer<typeof context>;
export type SessionBulkRevocationCancellation=z.infer<typeof cancellation>;
export type SessionBulkRevocationResult=z.infer<typeof result>;

function safeError(error:unknown):SessionHttpError{
 if(error instanceof SessionHttpError)return error;
 if(error instanceof PasskeyHttpError){
  if(error.status===401||error.status===403)return new SessionHttpError('authentication');
  if(error.status===404||error.status===409)return new SessionHttpError('changed');
 }
 return new SessionHttpError('unavailable');
}
export function parseSessionBulkRevocationOptions(value:unknown,locale:string):SessionBulkRevocationOptions{
 try{const parsed=options.parse(value);if(parsed.locale!==locale)throw new Error('Publication locale changed');new Intl.DateTimeFormat(parsed.locale);requirePublished(parsed.content,parsed.locale,sessionBulkRevocationContentKeys);return {...parsed,content:Object.fromEntries(sessionBulkRevocationContentKeys.map(key=>[key,parsed.content[key]!]))};}
 catch{throw new SessionHttpError('unavailable');}
}
/** Capability absence is optional. Authentication and cancellation still stop the caller. */
export async function loadSessionBulkRevocationOptions(locale:string,signal:AbortSignal):Promise<SessionBulkRevocationOptions|undefined>{
 try{return parseSessionBulkRevocationOptions(await requestJson(root+'/options',{signal}),locale);}
 catch(error){if(signal.aborted||error instanceof PasskeyHttpError&&(error.status===401||error.status===403))throw safeError(error);return undefined;}
}
export function parseSessionBulkRevocationContext(value:unknown,expected?:SessionBulkRevocationRequest|SessionBulkRevocationContext):SessionBulkRevocationContext{
 try{
  const parsed=context.parse(value);
  if(new Set(parsed.targets.map(item=>item.reference)).size!==parsed.targets.length||parsed.status==='DRAFT'&&parsed.activated||parsed.status==='PENDING'&&!parsed.activated)throw new Error('Inconsistent original');
  if(expected){
   if(parsed.requestId!==expected.requestId)throw new Error('Original request changed');
   if('targets' in expected){
    const original=context.parse(expected),versions=new Map(original.targets.map(item=>[item.reference,item.version]));
    if(parsed.operationReference!==original.operationReference||parsed.expiresAt!==original.expiresAt||parsed.targets.length!==original.targets.length||parsed.targets.some(item=>versions.get(item.reference)!==item.version))throw new Error('Original manifest changed');
   }
  }
  return parsed;
 }catch{throw new SessionHttpError('unavailable');}
}
/** A new preview uses a server-issued request identity, never a browser-selected replacement. */
export async function previewSessionBulkRevocation(csrfToken:string,signal:AbortSignal):Promise<SessionBulkRevocationContext>{
 try{return parseSessionBulkRevocationContext(await requestJson(root+'/preview',{csrfToken,body:{},signal}));}catch(error){throw safeError(error);}
}
/** Replay requires the exact existing protected original; capability loss cannot create a replacement. */
export async function replaySessionBulkRevocationPreview(csrfToken:string,request:SessionBulkRevocationRequest,signal:AbortSignal):Promise<SessionBulkRevocationContext>{
 try{const body=command.parse(request);return parseSessionBulkRevocationContext(await requestJson(root+'/preview',{csrfToken,body,signal}),body);}catch(error){throw safeError(error);}
}
export async function readSessionBulkRevocationContext(signal:AbortSignal):Promise<SessionBulkRevocationContext|undefined>{
 try{return parseSessionBulkRevocationContext(await requestJson(root+'/context',{signal}));}catch(error){if(error instanceof PasskeyHttpError&&error.status===404)return undefined;throw safeError(error);}
}
/** Activates only the original manifest. No target selection or proof is sent by this client. */
export async function activateSessionBulkRevocation(csrfToken:string,original:SessionBulkRevocationContext,signal:AbortSignal):Promise<SessionBulkRevocationContext>{
 try{const expected=parseSessionBulkRevocationContext(original);return parseSessionBulkRevocationContext(await requestJson(root+'/activate',{csrfToken,body:{requestId:expected.requestId},signal}),expected);}catch(error){throw safeError(error);}
}
export async function cancelSessionBulkRevocation(csrfToken:string,request:SessionBulkRevocationRequest,signal:AbortSignal):Promise<SessionBulkRevocationCancellation>{
 try{const body=command.parse(request),parsed=cancellation.parse(await requestJson(root+'/cancel',{csrfToken,body,signal}));if(parsed.requestId!==body.requestId)throw new Error('Original cancellation changed');return parsed;}catch(error){throw safeError(error);}
}
export function parseSessionBulkRevocationResult(value:unknown,original:SessionBulkRevocationContext):SessionBulkRevocationResult{
 try{
  const expected=parseSessionBulkRevocationContext(original),parsed=result.parse(value),versions=new Map(expected.targets.map(item=>[item.reference,BigInt(item.version)+1n]));
  if(parsed.requestId!==expected.requestId||parsed.operationReference!==expected.operationReference||parsed.targets.length!==expected.targets.length||new Set(parsed.targets.map(item=>item.reference)).size!==parsed.targets.length||parsed.targets.some(item=>versions.get(item.reference)!==BigInt(item.version)))throw new Error('Original causal receipt changed');
  return parsed;
 }catch{throw new SessionHttpError('unavailable');}
}
/** An uncertain result stays unconfirmed. There is no automatic preview, activation or proof replay. */
export async function readSessionBulkRevocationResult(csrfToken:string,original:SessionBulkRevocationContext,signal:AbortSignal):Promise<SessionBulkRevocationResult>{
 try{const expected=parseSessionBulkRevocationContext(original);return parseSessionBulkRevocationResult(await requestJson(root+'/result',{csrfToken,body:{requestId:expected.requestId},signal}),expected);}catch(error){throw safeError(error);}
}
