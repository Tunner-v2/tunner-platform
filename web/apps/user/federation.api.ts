import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson} from './passkeys.api';

export type FederationProvider='Google'|'Microsoft';
const id=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const pendingSchema=z.object({attemptId:id,requestId:id,status:z.enum(['PREPARED','CHALLENGE_ADMITTED','VERIFIED','COMPLETED','REJECTED','EXPIRED','UNKNOWN']),errorCode:z.string().max(128).nullable(),resolution:z.literal('REGISTRATION_INTAKE').optional()});
export type FederationPending=z.infer<typeof pendingSchema>;
const bootstrapSchema=z.object({googleAvailable:z.boolean(),microsoftAvailable:z.boolean(),pending:pendingSchema.nullable()});
export type FederationBootstrap=z.infer<typeof bootstrapSchema>;
export interface FederationOriginal{requestId:string;attemptId?:string}
const resultSchema=z.object({authenticated:z.boolean(),operation:pendingSchema,challengePath:z.enum(['/authentication/federation/google/challenge','/authentication/federation/microsoft/challenge']).optional()});
const csrfSchema=z.object({csrfToken:z.string().min(1)});

/** Closed safe classification only. Never retain a provider response, claims or parser diagnostics. */
export class FederationHttpError extends Error{
  constructor(readonly status:number,readonly code:'VERSION_CONFLICT'|'RATE_OR_ABUSE_LIMITED'|'OUTCOME_UNKNOWN'){super('The sign-in request could not be completed.');}
}
function bounded<T>(schema:z.ZodType<T>,value:unknown):T{const parsed=schema.safeParse(value);if(!parsed.success)throw new FederationHttpError(0,'OUTCOME_UNKNOWN');return parsed.data;}
export function parseFederationBootstrap(value:unknown):FederationBootstrap|undefined{
  if(value===undefined||value===null)return undefined;
  // preserveOriginalBrowser is deliberately stripped: it is internal cookie mechanics, not outcome evidence.
  return bounded(bootstrapSchema,value);
}
export function parseFederationResult(value:unknown,original:FederationOriginal){
  const result=bounded(resultSchema,value);
  if(result.operation.requestId!==original.requestId||original.attemptId!==undefined&&result.operation.attemptId!==original.attemptId)throw new FederationHttpError(409,'VERSION_CONFLICT');
  return result;
}
export function federationChallengePath(provider:FederationProvider){return provider==='Google'?'/authentication/federation/google/challenge':'/authentication/federation/microsoft/challenge';}
export async function federationPost(action:'start'|'result'|'complete'|'cancel',csrfToken:string,body:Readonly<Record<string,unknown>>,signal:AbortSignal):Promise<unknown>{
  try{return await requestJson('/authentication/federation/'+action,{csrfToken,body,signal});}
  catch(error){if(error instanceof PasskeyHttpError)throw new FederationHttpError(error.status,error.code==='VERSION_CONFLICT'?'VERSION_CONFLICT':error.status===429?'RATE_OR_ABUSE_LIMITED':'OUTCOME_UNKNOWN');throw new FederationHttpError(0,'OUTCOME_UNKNOWN');}
}
export async function federationCsrf(signal:AbortSignal):Promise<string>{
  try{
    const value=await requestJson('/authentication/session',{signal});
    const session=bounded(csrfSchema.extend({authenticated:z.literal(true)}),value);
    return session.csrfToken;
  }catch(error){
    // A session is solely a current-CSRF source. Only an actual anonymous401 permits the options fallback.
    if(!(error instanceof PasskeyHttpError)||error.status!==401)throw new FederationHttpError(0,'OUTCOME_UNKNOWN');
  }
  return bounded(csrfSchema,await requestJson('/authentication/options',{signal})).csrfToken;
}
