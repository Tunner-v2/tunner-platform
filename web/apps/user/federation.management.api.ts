import {z} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';
import {PasskeyHttpError,requestJson,requirePublished} from './passkeys.api';
import {federationCsrf,federationChallengePath} from './federation.api';

const uuid=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const version=z.number().int().positive().max(Number.MAX_SAFE_INTEGER),expectedVersion=z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const provider=z.enum(['Google','Microsoft']);
const operationSchema=z.object({operationId:uuid,requestId:uuid,kind:z.enum(['LINK','UNLINK']),provider,status:z.enum(['PENDING_PROVIDER','PENDING_CONFIRMATION','PENDING_PROOF','COMPLETED','REJECTED','EXPIRED','UNKNOWN']),version,targetLinkReference:uuid.nullable(),expectedLinkVersion:expectedVersion,expectedAccountVersion:version,resultLinkReference:uuid.nullable(),resultLinkVersion:version.nullable(),errorCode:z.string().max(128).nullable(),providerVerified:z.boolean(),canConfirm:z.boolean(),canRequestProof:z.boolean(),canCancel:z.boolean(),sessionEffect:z.enum(['PreserveExistingSessions','RevokeLinkedSessions']).nullable().optional(),impactKey:z.string().min(1).nullable().optional()}).superRefine((v,c)=>{
 if(v.kind==='UNLINK'&&(!v.targetLinkReference||v.expectedLinkVersion===0)||v.targetLinkReference===null&&v.expectedLinkVersion!==0)c.addIssue({code:'custom',message:'Invalid original operation'});
 if(v.status==='COMPLETED'&&(!v.resultLinkReference||v.resultLinkVersion===null||v.targetLinkReference!==null&&v.resultLinkReference!==v.targetLinkReference))c.addIssue({code:'custom',message:'Invalid original result'});
});
export type FederationManagementOperation=z.infer<typeof operationSchema>;
const linkSchema=z.object({linkReference:uuid,version,provider,state:z.enum(['ACTIVE','UNLINKED']),linkedAt:z.iso.datetime({offset:true}).nullable(),canStartUnlink:z.boolean()});
export type FederationManagedLink=z.infer<typeof linkSchema>;
const settingsSchema=z.object({accountVersion:version,googleAvailable:z.boolean(),microsoftAvailable:z.boolean(),canStartLink:z.boolean(),links:z.array(linkSchema),operation:operationSchema.nullable()}).superRefine((v,c)=>{if(new Set(v.links.map(x=>x.linkReference)).size!==v.links.length)c.addIssue({code:'custom',message:'Duplicate link reference'});});
export type FederationManagementSettings=z.infer<typeof settingsSchema>;
export interface FederationManagementStart{readonly requestId:string;readonly kind:'LINK'|'UNLINK';readonly provider:'Google'|'Microsoft';readonly targetLinkReference:string|null;readonly expectedLinkVersion:number;readonly expectedAccountVersion:number}
const resultSchema=z.object({operation:operationSchema,challengePath:z.enum(['/authentication/federation/google/challenge','/authentication/federation/microsoft/challenge']).optional()});
export class FederationManagementError extends Error{constructor(readonly status=0,readonly code:'CHANGED'|'LIMITED'|'UNAVAILABLE'='UNAVAILABLE'){super('The connection action could not be completed.');}}
function parse<T>(schema:z.ZodType<T>,value:unknown):T{const parsed=schema.safeParse(value);if(!parsed.success)throw new FederationManagementError();return parsed.data;}
export function sameManagementOriginal(a:FederationManagementStart,b:FederationManagementOperation){return a.requestId===b.requestId&&a.kind===b.kind&&a.provider===b.provider&&a.targetLinkReference===b.targetLinkReference&&a.expectedLinkVersion===b.expectedLinkVersion&&a.expectedAccountVersion===b.expectedAccountVersion;}
export function parseManagementOperation(value:unknown,expected?:FederationManagementStart|FederationManagementOperation){
 const result=parse(operationSchema,value);
 if(expected&&(!sameManagementOriginal(expected,result)||'operationId'in expected&&(expected.operationId!==result.operationId||result.version<expected.version||expected.kind==='UNLINK'&&(expected.sessionEffect!==result.sessionEffect||expected.impactKey!==result.impactKey))))throw new FederationManagementError(409,'CHANGED');
 return result;
}
async function safeJson(path:string,signal:AbortSignal,body?:Readonly<Record<string,unknown>>):Promise<unknown>{
 try{if(body===undefined)return await requestJson(path,{signal});const csrfToken=await federationCsrf(signal);return await requestJson(path,{signal,csrfToken,body});}
 catch(error){if(error instanceof PasskeyHttpError)throw new FederationManagementError(error.status,error.status===429?'LIMITED':[400,403,404,409].includes(error.status)?'CHANGED':'UNAVAILABLE');if(error instanceof FederationManagementError)throw error;throw new FederationManagementError();}
}
export async function loadFederationManagement(signal:AbortSignal){return parse(settingsSchema,await safeJson('/account/security/federation',signal));}
export async function startFederationManagement(original:FederationManagementStart,signal:AbortSignal){
 const raw=await safeJson('/account/security/federation/'+(original.kind==='LINK'?'link/start':'unlink/start'),signal,{requestId:original.requestId,provider:original.provider,expectedLinkVersion:original.expectedLinkVersion,expectedAccountVersion:original.expectedAccountVersion,...(original.targetLinkReference?{targetLinkReference:original.targetLinkReference}:{})});
 const result=parse(resultSchema,raw);const operation=parseManagementOperation(result.operation,original);
 return {operation,challengePath:result.challengePath};
}
export async function readFederationManagement(original:FederationManagementStart|FederationManagementOperation,signal:AbortSignal){
 const raw=await safeJson('/account/security/federation/result',signal,{requestId:original.requestId,...('operationId'in original?{operationId:original.operationId}:{})});
 return parseManagementOperation(parse(resultSchema,raw).operation,original);
}
export async function changeFederationManagement(action:'confirm'|'cancel',original:FederationManagementOperation,signal:AbortSignal){
 const body={operationId:original.operationId,requestId:original.requestId,...(action==='confirm'?{expectedVersion:original.version,confirmed:true}:{})};
 return parseManagementOperation(parse(resultSchema,await safeJson('/account/security/federation/'+action,signal,body)).operation,original);
}
export function managementChallengePath(original:FederationManagementStart,operation:FederationManagementOperation,path:string|undefined){
 if(original.kind!=='LINK'||operation.status!=='PENDING_PROVIDER'||operation.providerVerified||operation.errorCode!==null||path!==federationChallengePath(original.provider))throw new FederationManagementError();
 return federationChallengePath(original.provider);
}
export function federationUnlinkPublication(operation:FederationManagementOperation,content:Record<string,PublishedContent>,locale:string){
 if(operation.kind!=='UNLINK')return null;
 const key=operation.sessionEffect==='PreserveExistingSessions'?'federation.unlink.impact.preserve':operation.sessionEffect==='RevokeLinkedSessions'?'federation.unlink.impact.revokeLinked':null;
 if(!key||operation.impactKey!==key)return null;
 try{requirePublished(content,locale,[key]);return content[key]!;}catch{return null;}
}
export function managementErrorKey(reason:unknown){return reason instanceof FederationManagementError&&reason.code==='CHANGED'?'federation.management.changed':reason instanceof FederationManagementError&&reason.code==='LIMITED'?'federation.management.limited':'federation.management.unavailable';}
