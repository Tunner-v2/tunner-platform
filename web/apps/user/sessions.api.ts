import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson,requirePublished} from './passkeys.api';
import {sessionContentKeys} from './sessions.content-keys';

const reference=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const timestamp=z.iso.datetime({offset:true});
const state=z.enum(['ACTIVE','REVOKED','EXPIRED']);
const publication=z.object({key:z.string().min(1),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1)});
const options=z.object({locale:z.string().min(1),content:z.record(z.string(),publication)});
const authenticated=z.object({authenticated:z.literal(true),csrfToken:z.string().min(1)});
const item=z.strictObject({reference,version:z.string().regex(/^[1-9][0-9]*$/).refine(value=>BigInt(value)<=9223372036854775807n),isCurrent:z.boolean(),authenticatedAt:timestamp,lastSeenAt:timestamp,expiresAt:timestamp,recordedState:state,effectiveState:state,evaluatedAt:timestamp});
const page=z.strictObject({sessions:z.array(item),hasMore:z.boolean(),continuation:z.string().min(1).nullable()});
export type SessionItem=z.infer<typeof item>;
export type SessionPage=z.infer<typeof page>;
export type SessionOptions=z.infer<typeof options>;
export type SessionContext={options:SessionOptions;csrfToken:string};
export type SessionFailureKind='authentication'|'changed'|'unavailable';
export class SessionHttpError extends Error{
 constructor(readonly kind:SessionFailureKind){super('The session request could not be completed.');}
}
function safeError(error:unknown):SessionHttpError{
 if(error instanceof SessionHttpError)return error;
 if(error instanceof PasskeyHttpError){if(error.status===401||error.status===403)return new SessionHttpError('authentication');if(error.status===404||error.status===409)return new SessionHttpError('changed');}
 return new SessionHttpError('unavailable');
}
export function parseSessionOptions(value:unknown):SessionOptions{
 try{const parsed=options.parse(value);requirePublished(parsed.content,parsed.locale,sessionContentKeys);new Intl.DateTimeFormat(parsed.locale);return parsed;}catch{throw new SessionHttpError('unavailable');}
}
export function parseSessionPage(value:unknown):SessionPage{
 try{const parsed=page.parse(value);if(parsed.hasMore!==(parsed.continuation!==null)||parsed.hasMore&&parsed.sessions.length===0||new Set(parsed.sessions.map(x=>x.reference)).size!==parsed.sessions.length||parsed.sessions.filter(x=>x.isCurrent).length>1)throw new Error('Inconsistent page');return parsed;}catch{throw new SessionHttpError('unavailable');}
}
/** Current antiforgery comes from authenticated session context, never anonymous sign-in options. */
export async function loadSessionContext(signal:AbortSignal):Promise<SessionContext>{
 try{const session=authenticated.parse(await requestJson('/authentication/session',{signal}));const presentation=parseSessionOptions(await requestJson('/account/sessions/options',{signal}));return {options:presentation,csrfToken:session.csrfToken};}catch(error){throw safeError(error);}
}
export async function readSessionPage(csrfToken:string,continuation:string|null,signal:AbortSignal):Promise<SessionPage>{
 try{return parseSessionPage(await requestJson('/account/sessions/list',{csrfToken,body:{continuation},signal}));}catch(error){throw safeError(error);}
}
/** Existing ordinary actor sign-out only. An uncertain response is never retried automatically. */
export async function signOutCurrentSession(csrfToken:string,signal:AbortSignal):Promise<void>{
 try{const result=await requestJson('/authentication/sign-out',{csrfToken,body:{},signal});z.object({authenticated:z.literal(false)}).parse(result);}catch(error){throw safeError(error);}
}
export async function checkCurrentSession(signal:AbortSignal):Promise<string>{
 try{return authenticated.parse(await requestJson('/authentication/session',{signal})).csrfToken;}catch(error){throw safeError(error);}
}
