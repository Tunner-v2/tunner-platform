import { z } from '@tunner/ui';
import type { PublishedContent } from '@tunner/ui';

const uuid=z.uuid();
const published=z.object({key:z.string().min(1),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1)});
export const operationSchema=z.object({requestId:uuid,ceremonyId:uuid,status:z.enum(['PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED']),kind:z.enum(['ENROLL','REVOKE','SIGN_IN']),credentialReference:uuid.nullable(),expectedVersion:z.number().int().nonnegative(),expiresAt:z.iso.datetime({offset:true}),publicKey:z.unknown().nullable(),resultHandle:z.string().min(1).nullable().optional()});
export type PasskeyOperation=z.infer<typeof operationSchema>;
export const recoveryOperationSchema=operationSchema.extend({rememberMe:z.boolean().nullable().optional(),signInRequired:z.boolean().optional()});
export function parseAuthenticationPasskeyRecovery(value:unknown){if(value===undefined||value===null)return undefined;const result=recoveryOperationSchema.parse(value);if(result.kind==='SIGN_IN'){if(typeof result.rememberMe!=='boolean')throw new Error('Original sign-in context is required.');}else if(result.kind!=='REVOKE'||result.status!=='COMPLETED'||result.signInRequired!==true||!result.resultHandle)throw new Error('A proved terminal operation is required.');return result;}
export interface PasskeyStartSnapshot{readonly requestId:string;readonly kind:'ENROLL'|'REVOKE';readonly credentialReference:string|null;readonly expectedVersion:number}
export function matchesPasskeyStart(expected:PasskeyStartSnapshot,actual:PasskeyOperation){return expected.requestId===actual.requestId&&expected.kind===actual.kind&&expected.credentialReference===actual.credentialReference&&expected.expectedVersion===actual.expectedVersion;}
export const credentialSchema=z.object({credentialReference:uuid,state:z.enum(['ACTIVE','REVOKED']),version:z.number().int().positive(),createdAt:z.iso.datetime({offset:true}),lastUsedAt:z.iso.datetime({offset:true}).nullable(),revokedAt:z.iso.datetime({offset:true}).nullable()});
export type PasskeyMetadata=z.infer<typeof credentialSchema>;
export const methodsSchema=z.object({credentials:z.array(credentialSchema),passwordAvailable:z.boolean(),enrollmentAvailable:z.boolean(),revocationRequiresCurrentEligibility:z.literal(true),protectionFactsAvailable:z.boolean(),activityAvailable:z.boolean()});
export type PasskeyMethods=z.infer<typeof methodsSchema>;
export const bootstrapSchema=z.object({csrfToken:z.string().min(1),requestId:uuid,maximumCredentialJsonBytes:z.number().int().min(1024).max(1048576),revocationSessionEffect:z.enum(['PreserveExistingSessions','RevokeCredentialSessions']),totpAvailable:z.boolean().optional(),locale:z.string().min(1),content:z.record(z.string(),published),recovery:operationSchema.nullable().optional()});
export type PasskeyBootstrap=z.infer<typeof bootstrapSchema>;
export const assertionCompleted=z.object({status:z.literal('COMPLETED'),requestId:uuid,ceremonyId:uuid,authenticated:z.literal(true)});
export const terminalCompleted=z.object({status:z.literal('COMPLETED'),requestId:uuid,ceremonyId:uuid,signInRequired:z.literal(true)});

export class PasskeyHttpError extends Error {
  constructor(readonly status:number,readonly code:string,readonly details:unknown=null){super('The passkey request could not be completed.');}
}
export async function requestJson(path:string,options:{csrfToken?:string;body?:unknown;signal?:AbortSignal}={}):Promise<unknown>{
  const response=await fetch(path,{method:options.body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',redirect:'error',...(options.signal?{signal:options.signal}:{}),...(options.body===undefined?{}:{headers:{'Content-Type':'application/json',...(options.csrfToken?{'X-Tunner-CSRF':options.csrfToken}:{})},body:JSON.stringify(options.body)})});
  if(!response.ok){let code='DEPENDENCY_UNAVAILABLE';let details:unknown=null;try{const problem=await response.json() as {error_code?:unknown;code?:unknown};details=problem;const candidate=problem.error_code??problem.code;if(typeof candidate==='string')code=candidate;}catch{/* HTTP status remains authoritative. */}throw new PasskeyHttpError(response.status,code,details);}
  return response.json() as Promise<unknown>;
}
export function sameOperation(expected:Pick<PasskeyOperation,'requestId'|'ceremonyId'|'kind'>,actual:Pick<PasskeyOperation,'requestId'|'ceremonyId'|'kind'>){return expected.requestId===actual.requestId&&expected.ceremonyId===actual.ceremonyId&&expected.kind===actual.kind;}
export function parseOperation(value:unknown,expected?:Pick<PasskeyOperation,'requestId'|'ceremonyId'|'kind'>){const result=operationSchema.parse(value);if(expected&&!sameOperation(expected,result))throw new Error('The original passkey operation changed.');return result;}
export function requirePublished(content:Record<string,PublishedContent>,locale:string,keys:readonly string[]){if(keys.some(key=>{const value=content[key];return !value||value.key!==key||value.locale!==locale||value.state!=='PUBLISHED'||![value.text,value.variantRef,value.versionRef,value.publicationRef].every(value=>typeof value==='string'&&value.trim().length>0);})){throw new Error('Required passkey publication is unavailable.');}}
export function passkeyErrorKey(error:unknown){
  if(error instanceof PasskeyHttpError){if(error.status===429)return 'passkeys.error.limited';if(error.code==='PASSKEY_LIMIT_REACHED')return 'passkeys.error.limit';if(error.code==='REVOCATION_NOT_ALLOWED')return 'passkeys.error.eligibility';if(error.status===409||error.code==='STEP_UP_REQUIRED')return 'passkeys.error.changed';if([400,413,415].includes(error.status))return 'passkeys.error.validation';if(error.status===503)return 'passkeys.error.policy';}
  return 'passkeys.error.unavailable';
}
