import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson} from './passkeys.api';
import {getPasskey} from './passkeys.native';
import type {RecoveryPasswordEntry} from './recovery';

const reference=z.string().min(1),id=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const publication=z.object({key:reference,text:reference,locale:reference,state:z.literal('PUBLISHED'),variantRef:reference,versionRef:reference,publicationRef:reference});
const goal=z.object({reference,labelKey:reference,descriptionKey:reference});
const detail=z.object({reference,labelKey:reference,valueKey:reference});
const effect=z.object({kind:z.literal('TUNNER_SESSION_INVALIDATION'),labelKey:reference,valueKey:reference});
const result=z.object({outcome:z.literal('PASSWORD_REPAIRED_NO_SESSION'),effects:z.array(effect),followUps:z.array(detail)});
const operation=z.object({requestId:id,status:z.enum(['AWAITING_PROOF','CANCELLED','EXPIRED','PASSWORD_REPAIRED_NO_SESSION']),version:z.number().int().positive().safe(),goalReference:reference,canCancel:z.boolean(),canSubmitRepair:z.boolean(),codeSubmission:z.literal('ATOMIC_REPAIR_ONLY'),authenticated:z.literal(false),result:result.optional()});
const options=z.object({available:z.literal(true),profileVersion:reference,publicationVersion:reference,locale:reference,content:z.record(z.string(),publication),goals:z.array(goal),repair:z.object({available:z.literal(true),passwordMinimumLength:z.number().int().positive().safe(),passwordMaximumLength:z.number().int().positive().safe(),sessionEffect:z.literal('InvalidateExistingTunnerSessions'),effectLabelKey:reference,effectValueKey:reference}),operation:operation.optional(),code:z.object({available:z.boolean()}).optional(),passkey:z.object({available:z.boolean(),maximumCredentialJsonBytes:z.number().int().positive().safe().nullish()}).optional(),contact:z.object({available:z.boolean(),proofDigits:z.number().int().min(6).max(9)}).optional()});
const authentication=z.object({csrfToken:reference,recoveryAvailable:z.literal(true),termsUri:reference,privacyUri:reference});
const commandResult=z.object({operation});
const contactOptions=z.object({requestId:id,challengeId:id,expiresAt:z.iso.datetime({offset:true})});
const passkeyOptions=z.object({requestId:id,ceremonyId:id,publicKey:z.record(z.string(),z.unknown()),expiresAt:z.iso.datetime({offset:true})});

export type RecoveryOptions=z.infer<typeof options>;
export type RecoveryBootstrap=RecoveryOptions&{termsUri:string;privacyUri:string};
export type RecoveryOperation=z.infer<typeof operation>;
export interface RecoveryOriginal {requestId:string;goalReference:string;version:number}
export type RecoveryProof={kind:'code';code:string}|{kind:'contact';challengeId:string;code:string}|{kind:'passkey'};
export class RecoveryHttpError extends Error {
 constructor(readonly kind:'changed'|'unknown'|'unavailable'|'validation'){super('The original recovery request could not be confirmed.');}
}
function parse<T>(schema:z.ZodType<T>,value:unknown):T{const parsed=schema.safeParse(value);if(!parsed.success)throw new RecoveryHttpError('unavailable');return parsed.data;}
export function parseRecoveryOperation(value:unknown,original?:RecoveryOriginal):RecoveryOperation {
 const parsed=parse(operation,value);
 if(original&&(parsed.requestId!==original.requestId||parsed.goalReference!==original.goalReference||parsed.version<original.version))throw new RecoveryHttpError('changed');
 if(parsed.status==='PASSWORD_REPAIRED_NO_SESSION'&&!parsed.result||parsed.status!=='PASSWORD_REPAIRED_NO_SESSION'&&parsed.result)throw new RecoveryHttpError('unavailable');
 return parsed;
}
export function parseRecoveryOptions(value:unknown):RecoveryOptions {
 const parsed=parse(options,value);
 if(parsed.repair.passwordMinimumLength>parsed.repair.passwordMaximumLength||new Set(parsed.goals.map(goal=>goal.reference)).size!==parsed.goals.length)throw new RecoveryHttpError('unavailable');
 if(parsed.operation)parseRecoveryOperation(parsed.operation);
 return parsed;
}
async function recoveryContext(signal:AbortSignal){return parse(authentication,await requestJson('/authentication/options',{signal}));}
function failure(error:unknown):RecoveryHttpError {
 if(error instanceof RecoveryHttpError)return error;
 if(error instanceof PasskeyHttpError){
  if(error.status===409)return new RecoveryHttpError('changed');
  if(error.status===400&&error.code==='VALIDATION_ERROR')return new RecoveryHttpError('validation');
 }
 return new RecoveryHttpError('unknown');
}
async function post(path:string,body:unknown,signal:AbortSignal):Promise<unknown>{
 const {csrfToken}=await recoveryContext(signal);
 return requestJson('/recovery/'+path,{csrfToken,body,signal});
}
export async function prepareRecoveryContact(original:RecoveryOriginal,signal:AbortSignal){
 try{
  const value=parse(contactOptions,await post('contact/options',{requestId:original.requestId},signal));
  if(value.requestId!==original.requestId)throw new RecoveryHttpError('changed');
  return value;
 }catch(error){throw failure(error);}
}
/** The native owner verifies proof and repairs atomically; this adapter never verifies a code separately. */
export async function repairRecoveryPassword(original:RecoveryOriginal,proof:RecoveryProof,entry:RecoveryPasswordEntry,options:RecoveryOptions,signal:AbortSignal):Promise<RecoveryOperation>{
 try{
  let path:string,body:unknown;
  const password={requestId:original.requestId,expectedVersion:original.version,password:entry.password,passwordConfirmation:entry.passwordConfirmation};
  if(proof.kind==='code'){
   if(options.code?.available!==true||!proof.code)throw new RecoveryHttpError('unavailable');
   path='repair';body={...password,code:proof.code};
  }else if(proof.kind==='contact'){
   if(options.contact?.available!==true||!proof.code||!id.safeParse(proof.challengeId).success)throw new RecoveryHttpError('unavailable');
   path='contact/repair';body={...password,challengeId:proof.challengeId,code:proof.code};
  }else{
   const maximum=options.passkey?.maximumCredentialJsonBytes;
   if(options.passkey?.available!==true||maximum==null)throw new RecoveryHttpError('unavailable');
   const ceremony=parse(passkeyOptions,await post('passkey/options',{requestId:original.requestId},signal));
   if(ceremony.requestId!==original.requestId)throw new RecoveryHttpError('changed');
   const credential=await getPasskey(ceremony.publicKey,maximum,signal);
   path='passkey/repair';body={...password,ceremonyId:ceremony.ceremonyId,credential};
  }
  const response=parse(commandResult,await post(path,body,signal));
  return parseRecoveryOperation(response.operation,original);
 }catch(error){throw failure(error);}
}
export async function loadRecoveryOptions(signal:AbortSignal):Promise<RecoveryBootstrap>{
 // This creates/reads the existing anonymous browser and current CSRF context; it proves no recovery outcome.
 const context=await recoveryContext(signal);
 return {...parseRecoveryOptions(await requestJson('/recovery/options',{signal})),termsUri:context.termsUri,privacyUri:context.privacyUri};
}
export async function recoveryCommand(action:'start'|'result'|'cancel',original:RecoveryOriginal,signal:AbortSignal,entry?:{email:string}):Promise<RecoveryOperation>{
 try{
  const {csrfToken}=await recoveryContext(signal);
  if(action==='start'&&!entry)throw new RecoveryHttpError('unavailable');
  const body=action==='start'?{requestId:original.requestId,email:entry!.email,goalReference:original.goalReference}:{requestId:original.requestId};
  const response=parse(commandResult,await requestJson('/recovery/'+action,{csrfToken,body,signal}));
  return parseRecoveryOperation(response.operation,original);
 }catch(error){
  // Retain only a closed safe classification. Do not store a problem body, submitted email, code or credentials.
  throw failure(error);
 }
}
