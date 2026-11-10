import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson,requirePublished} from './passkeys.api';
import {requiredTotpKeys,requiredTotpMfaKeys} from './totp.content-keys';

const uuid=z.uuid(),instant=z.iso.datetime({offset:true});
const publication=z.object({key:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1)});
const sessionEffect=z.enum(['PreserveExistingSessions','RevokeFactorSessions']);
export const totpOperationSchema=z.object({requestId:uuid,enrollmentId:uuid,kind:z.enum(['ENROLL','DISABLE','RESET']),authenticatorReference:uuid,
  expectedVersion:z.number().int().nonnegative(),status:z.enum(['PENDING_PROOF','AUTHORIZED','PREPARING','AWAITING_FIRST_CODE','COMPLETED','EXPIRED','REJECTED']),expiresAt:instant,
  disclosureAvailable:z.boolean(),disclosureConsumed:z.boolean(),activationCompleted:z.boolean(),factorSessionEffect:sessionEffect.nullable(),currentSessionEnds:z.boolean()});
export type TotpOperation=z.infer<typeof totpOperationSchema>;
export const totpMetadataSchema=z.object({authenticatorReference:uuid,state:z.enum(['INACTIVE','ACTIVE','DISABLED']),version:z.number().int().positive(),createdAt:instant,activatedAt:instant.nullable(),lastUsedAt:instant.nullable(),disabledAt:instant.nullable()});
export type TotpMetadata=z.infer<typeof totpMetadataSchema>;
export const totpMethodsSchema=z.object({authenticators:z.array(totpMetadataSchema)});
export type TotpMethods=z.infer<typeof totpMethodsSchema>;
export const totpBootstrapSchema=z.object({csrfToken:z.string().min(1),requestId:uuid,version:z.string().min(1),maximumActiveAuthenticators:z.number().int().positive(),maximumPendingOperations:z.number().int().positive(),
  disableSessionEffect:sessionEffect,resetSessionEffect:sessionEffect,resetBehavior:z.literal('RetireAffectedFactorThenSeparateEnrollment'),replacementOrdering:z.enum(['IndependentEnrollment','DisableBeforeEnrollment']),
  canStartEnrollment:z.boolean(),canStartDisable:z.boolean(),canStartReset:z.boolean(),finalEligibilityRequired:z.literal(true),recovery:totpOperationSchema.nullable().optional(),locale:z.string().min(1),content:z.record(z.string(),publication)});
export type TotpBootstrap=z.infer<typeof totpBootstrapSchema>;
export interface TotpStartSnapshot {readonly requestId:string;readonly kind:TotpOperation['kind'];readonly authenticatorReference:string|null;readonly expectedVersion:number}
export function matchesTotpStart(expected:TotpStartSnapshot,actual:TotpOperation){return expected.requestId===actual.requestId&&expected.kind===actual.kind&&expected.expectedVersion===actual.expectedVersion&&(expected.kind==='ENROLL'||expected.authenticatorReference===actual.authenticatorReference);}
export function sameTotpOperation(expected:Pick<TotpOperation,'requestId'|'enrollmentId'|'kind'|'authenticatorReference'|'expectedVersion'>,actual:TotpOperation){return expected.requestId===actual.requestId&&expected.enrollmentId===actual.enrollmentId&&expected.kind===actual.kind&&expected.authenticatorReference===actual.authenticatorReference&&expected.expectedVersion===actual.expectedVersion;}

/** Errors contain bounded safe classifications only, never raw response bodies, OTPs, keys or URIs. */
export class TotpHttpError extends Error {constructor(readonly status:number,readonly code:string){super('The verification request could not be completed.');}}
const safeCodes=new Set(['AUTHENTICATION_FAILED','VERSION_CONFLICT','DISCLOSURE_UNAVAILABLE','STEP_UP_REQUIRED','RATE_OR_ABUSE_LIMITED','REPLAY_CAPACITY_EXHAUSTED','VERIFICATION_BUDGET_EXHAUSTED','DEPENDENCY_UNAVAILABLE','CLOCK_OUT_OF_BOUNDS','PREPARATION_UNKNOWN','VALIDATION_ERROR','TOTP_LIMIT_REACHED','NOT_FOUND_OR_NOT_DISCLOSABLE','DISABLE_NOT_ALLOWED','RESET_NOT_ALLOWED','OUTCOME_UNKNOWN']);
export async function requestTotpJson(path:string,options:{csrfToken?:string;body?:unknown;signal?:AbortSignal}={}):Promise<unknown>{
  try{return await requestJson(path,options);}catch(error){if(error instanceof PasskeyHttpError)throw new TotpHttpError(error.status,safeCodes.has(error.code)?error.code:'DEPENDENCY_UNAVAILABLE');throw new TotpHttpError(0,'OUTCOME_UNKNOWN');}
}
/** Do not propagate Zod diagnostics from a possible secret-bearing response. */
function boundedParse<T>(schema:z.ZodType<T>,value:unknown):T{const parsed=schema.safeParse(value);if(!parsed.success)throw new TotpHttpError(0,'OUTCOME_UNKNOWN');return parsed.data;}
export function parseTotpOperation(value:unknown,expected?:Pick<TotpOperation,'requestId'|'enrollmentId'|'kind'|'authenticatorReference'|'expectedVersion'>){const operation=boundedParse(totpOperationSchema,value);if(expected&&!sameTotpOperation(expected,operation))throw new TotpHttpError(409,'VERSION_CONFLICT');return operation;}
export async function loadTotpOptions(signal?:AbortSignal):Promise<TotpBootstrap>{const result=boundedParse(totpBootstrapSchema,await requestTotpJson('/account/totp/options',signal?{signal}:{}));requirePublished(result.content,result.locale,requiredTotpKeys);return result;}
export async function loadTotpMethods(signal?:AbortSignal):Promise<TotpMethods>{return boundedParse(totpMethodsSchema,await requestTotpJson('/account/totp',signal?{signal}:{}));}

const disclosureSchema=z.object({status:z.literal('AWAITING_FIRST_CODE'),requestId:uuid,enrollmentId:uuid,manualKey:z.string().min(1).max(1024),otpAuthUri:z.string().min(1).max(4096),expiresAt:instant});
export type TotpDisclosure=z.infer<typeof disclosureSchema>;
export function parseTotpDisclosure(value:unknown,expected:Pick<TotpOperation,'requestId'|'enrollmentId'>):TotpDisclosure {
  const result=boundedParse(disclosureSchema,value);if(result.requestId!==expected.requestId||result.enrollmentId!==expected.enrollmentId)throw new TotpHttpError(409,'VERSION_CONFLICT');
  try{const uri=new URL(result.otpAuthUri);if(uri.protocol!=='otpauth:'||uri.hostname!=='totp'||uri.username||uri.password||uri.hash||uri.searchParams.getAll('secret').length!==1||uri.searchParams.get('secret')!==result.manualKey)throw new Error();}catch{throw new TotpHttpError(0,'OUTCOME_UNKNOWN');}
  return result;
}
export const mfaRequiredSchema=z.object({status:z.literal('MFA_REQUIRED'),authenticated:z.literal(false),requestId:uuid,expiresAt:instant});
export type TotpMfaRequired=z.infer<typeof mfaRequiredSchema>;
export function parseMfaRequired(value:unknown,requestId?:string):TotpMfaRequired|null{const result=mfaRequiredSchema.safeParse(value);if(!result.success)return null;if(requestId&&result.data.requestId!==requestId)throw new TotpHttpError(409,'VERSION_CONFLICT');return result.data;}
const httpsUri=z.string().url().refine(value=>{const uri=new URL(value);return uri.protocol==='https:'&&!uri.username&&!uri.password;});
export const totpMfaOptionsSchema=z.object({status:z.enum(['PENDING','COMPLETED','EXPIRED']),requestId:uuid,expiresAt:instant,authenticators:z.array(z.object({authenticatorReference:uuid})),csrfToken:z.string().min(1),locale:z.string().min(1),content:z.record(z.string(),publication),termsUri:httpsUri,privacyUri:httpsUri,supportUri:httpsUri.nullable()});
export type TotpMfaOptions=z.infer<typeof totpMfaOptionsSchema>;
export async function loadTotpMfaOptions(signal?:AbortSignal):Promise<TotpMfaOptions>{const result=boundedParse(totpMfaOptionsSchema,await requestTotpJson('/authentication/totp/options',signal?{signal}:{}));requirePublished(result.content,result.locale,requiredTotpMfaKeys);return result;}
const mfaCompletedSchema=z.object({status:z.literal('COMPLETED'),authenticated:z.literal(true),requestId:uuid});
export function parseTotpMfaCompleted(value:unknown,requestId:string){const result=boundedParse(mfaCompletedSchema,value);if(result.requestId!==requestId)throw new TotpHttpError(409,'VERSION_CONFLICT');return result;}
const terminalSchema=z.object({operation:totpOperationSchema,signInRequired:z.literal(true)});
export function parseTotpTerminal(value:unknown,expected:TotpOperation){const result=boundedParse(terminalSchema,value);if(!sameTotpOperation(expected,result.operation)||result.operation.status!=='COMPLETED'||!['DISABLE','RESET'].includes(result.operation.kind)||!result.operation.currentSessionEnds)throw new TotpHttpError(409,'VERSION_CONFLICT');return result;}
export function totpErrorKey(error:unknown){if(error instanceof TotpHttpError){if(error.status===429)return 'totp.error.limited';if(error.code==='TOTP_LIMIT_REACHED')return 'totp.error.limit';if(error.code==='DISABLE_NOT_ALLOWED'||error.code==='RESET_NOT_ALLOWED')return 'totp.error.eligibility';if(error.status===409||error.code==='STEP_UP_REQUIRED')return 'totp.error.changed';if(error.code==='VALIDATION_ERROR'||error.status===413||error.status===415)return 'totp.error.validation';if(error.status===503)return 'totp.error.policy';}return 'totp.error.unavailable';}
