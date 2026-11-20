import {z} from '@tunner/ui';
import {federationCsrf,federationPost,parseFederationResult} from './federation.api';
const id=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const version=z.number().int().positive().max(Number.MAX_SAFE_INTEGER);
const intakeSchema=z.object({mode:z.literal('FEDERATED'),provider:z.literal('Google'),verifiedEmail:z.string().min(1).max(256).nullable(),federationRequestId:id,attemptId:id,registrationKey:id,expectedVersion:version,canContinue:z.boolean()}).superRefine((value,ctx)=>{if(value.canContinue&&!value.verifiedEmail)ctx.addIssue({code:'custom',message:'Unavailable'});});
export type FederationIntake=z.infer<typeof intakeSchema>;
export const federationRegistrationKeys=['registration.federation.description','registration.federation.emailSource.Google'] as const;
export const federationNoticeSchema=z.object({notices:z.boolean().refine(value=>value,'registration.error.notices')});
export function parseFederationIntake(value:unknown,registrationKey:string|undefined):FederationIntake|undefined{
 if(value===undefined)return undefined;
 const parsed=intakeSchema.safeParse(value);if(!parsed.success||parsed.data.registrationKey!==registrationKey)throw new Error('Unavailable');return parsed.data;
}
export function sameFederationIntake(a:FederationIntake,b:FederationIntake|undefined){return b?.canContinue===true&&a.federationRequestId===b.federationRequestId&&a.attemptId===b.attemptId&&a.registrationKey===b.registrationKey&&a.expectedVersion===b.expectedVersion&&a.provider===b.provider&&a.verifiedEmail===b.verifiedEmail;}
const registrationResult=z.object({operationId:id,correlationId:z.string().min(1),status:z.enum(['REQUIRES_ACTION','SUCCEEDED']),version,expiresAt:z.string().nullable(),resendAfter:z.string().nullable(),nextAction:z.enum(['PERSONAL_INFORMATION','CONSENT_REQUIRED','COMPLETE']),personalInformation:z.object({fullName:z.string().nullable(),displayName:z.string().nullable(),verifiedEmail:z.string(),fullNameMaximumLength:z.number().int().nonnegative(),displayNameMaximumLength:z.number().int().nonnegative()}).nullable().optional()});
export async function federatedRegistrationCommand(kind:'complete'|'result',intake:FederationIntake,notices:{termsVersion:string;privacyVersion:string},signal:AbortSignal){
 const csrf=await federationCsrf(signal);
 const raw=await federationPost(kind,csrf,kind==='result'?{}:{attemptId:intake.attemptId,federationRequestId:intake.federationRequestId,registrationKey:intake.registrationKey,expectedVersion:intake.expectedVersion,termsVersion:notices.termsVersion,privacyVersion:notices.privacyVersion,acknowledgedNotices:true},signal);
 const result=parseFederationResult(raw,{requestId:intake.federationRequestId,attemptId:intake.attemptId});
 if(result.authenticated||result.operation.resolution!=='REGISTRATION_INTAKE'||result.challengePath!==undefined)throw new Error('Unavailable');
 if(result.operation.status!=='COMPLETED'||result.operation.errorCode!==null)return undefined;
 const body=z.object({registration:registrationResult}).safeParse(raw);if(!body.success)throw new Error('Unavailable');return body.data.registration;
}
