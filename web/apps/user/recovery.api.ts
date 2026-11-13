import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson} from './passkeys.api';

const reference=z.string().min(1),id=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const publication=z.object({key:reference,text:reference,locale:reference,state:z.literal('PUBLISHED'),variantRef:reference,versionRef:reference,publicationRef:reference});
const goal=z.object({reference,labelKey:reference,descriptionKey:reference});
const detail=z.object({reference,labelKey:reference,valueKey:reference});
const effect=z.object({kind:z.literal('TUNNER_SESSION_INVALIDATION'),labelKey:reference,valueKey:reference});
const result=z.object({outcome:z.literal('PASSWORD_REPAIRED_NO_SESSION'),effects:z.array(effect),followUps:z.array(detail)});
const operation=z.object({requestId:id,status:z.enum(['AWAITING_PROOF','CANCELLED','EXPIRED','PASSWORD_REPAIRED_NO_SESSION']),version:z.number().int().positive().safe(),goalReference:reference,canCancel:z.boolean(),canSubmitRepair:z.boolean(),codeSubmission:z.literal('ATOMIC_REPAIR_ONLY'),authenticated:z.literal(false),result:result.optional()});
const options=z.object({available:z.literal(true),profileVersion:reference,publicationVersion:reference,locale:reference,content:z.record(z.string(),publication),goals:z.array(goal),repair:z.object({available:z.literal(true),passwordMinimumLength:z.number().int().positive().safe(),passwordMaximumLength:z.number().int().positive().safe(),sessionEffect:z.literal('InvalidateExistingTunnerSessions'),effectLabelKey:reference,effectValueKey:reference}),operation:operation.optional()});
const authentication=z.object({csrfToken:reference,recoveryAvailable:z.literal(true),termsUri:reference,privacyUri:reference});
const commandResult=z.object({operation});

export type RecoveryOptions=z.infer<typeof options>;
export type RecoveryBootstrap=RecoveryOptions&{termsUri:string;privacyUri:string};
export type RecoveryOperation=z.infer<typeof operation>;
export interface RecoveryOriginal {requestId:string;goalReference:string;version:number}
export class RecoveryHttpError extends Error {
 constructor(readonly kind:'changed'|'unknown'|'unavailable'){super('The original recovery request could not be confirmed.');}
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
  if(error instanceof RecoveryHttpError)throw error;
  // Retain only a closed safe classification. Do not store a problem body, submitted email, code or credentials.
  if(error instanceof PasskeyHttpError&&error.status===409)throw new RecoveryHttpError('changed');
  throw new RecoveryHttpError('unknown');
 }
}
