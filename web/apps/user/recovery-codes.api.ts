import {z} from '@tunner/ui';
import {requirePublished} from './passkeys.api';

const uuid=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const profile=z.string().min(1).max(256).refine(value=>!/[\u0000-\u001f\u007f]/.test(value));
const version=z.number().int().positive().max(Number.MAX_SAFE_INTEGER);
const count=z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const size=z.number().int().positive().max(2147483647);
const reference=z.string().regex(/^[0-9a-f]{32}$/),digest=z.string().regex(/^[0-9A-F]{64}$/);
const published=z.object({key:z.string().min(1),variantRef:z.string().min(1),versionRef:z.string().min(1),publicationRef:z.string().min(1),locale:z.string().min(1),state:z.literal('PUBLISHED'),text:z.string().min(1)}).strict();
const publicationValidity=z.object({from:z.iso.datetime({offset:true}),includesFrom:z.boolean(),until:z.iso.datetime({offset:true}).nullable(),includesUntil:z.boolean()}).strict();
const impact=z.object({impactKey:z.string().min(1),factsDigest:digest,publicationVersion:profile,publicationDigest:digest,publicationValidity,canContinue:z.boolean(),priorSetCount:count,remainingCodeCount:count,sessionEffect:z.literal('NO_SESSION_MUTATION'),replacementMechanism:z.literal('ATOMIC_RETIRE_EXACT_EXPECTED_SETS_AT_ISSUE_COMMIT_V1')}).strict();
const operation=z.object({status:z.enum(['PENDING_PROOF','COMPLETED','REJECTED','EXPIRED']),ownerRequestId:uuid,operationReference:reference,setReference:uuid,ownerContextDigest:digest,deliveryProfileVersion:profile,formatProfileVersion:z.literal('NATIVE_HEX32_V1'),codesPerSet:size,maximumCodesPerSet:size,expiresAt:z.iso.datetime({offset:true}),expectedVersion:version,kind:z.enum(['GENERATE','REPLACE']),impact,codesUnavailable:z.boolean()}).strict().superRefine((value,context)=>{
 if(value.codesPerSet>value.maximumCodesPerSet||value.status==='COMPLETED'&&!value.codesUnavailable||value.status!=='PENDING_PROOF'&&value.impact.canContinue)context.addIssue({code:'custom',message:'Invalid recovery operation'});
});
const options=z.object({csrfToken:z.string().min(1),ownerRequestId:uuid,expectedVersion:version,policyVersion:profile,formatProfileVersion:z.literal('NATIVE_HEX32_V1'),deliveryProfileVersion:profile,codesPerSet:size,maximumCodesPerSet:size,canGenerate:z.boolean(),canReplace:z.boolean(),activeSetCount:count,remainingCodeCount:count,locale:z.string().min(1),content:z.record(z.string(),published),recovery:operation.nullable()}).strict().superRefine((value,context)=>{if(value.codesPerSet>value.maximumCodesPerSet)context.addIssue({code:'custom',message:'Invalid recovery options'});});
export type RecoveryCodeOperation=z.infer<typeof operation>;
export type RecoveryCodeOptions=z.infer<typeof options>;
export type RecoveryCodeKind=RecoveryCodeOperation['kind'];
export interface RecoveryCodeStart {readonly ownerRequestId:string;readonly expectedVersion:number;readonly kind:RecoveryCodeKind}

export const requiredRecoveryCodeKeys=['heading','card.description','card.unavailable','card.pending','error.unavailable','action.generate','action.replace','action.cancel','action.continue','action.close','action.check','action.download','action.done','review.heading','review.description','review.details','review.unavailable','reveal.heading','reveal.description','reveal.once','reveal.warning','download.requested','download.description','download.unavailable','unavailable.heading','unavailable.description','presented.heading','presented.description','unknown.heading','unknown.description'].map(key=>'recoveryCodes.'+key);

export class RecoveryCodeHttpError extends Error {
 constructor(readonly status:number,readonly code:'UNAVAILABLE'|'CHANGED'|'UNAUTHENTICATED'='UNAVAILABLE'){super('Recovery-code request unavailable');}
}
function unavailable():never{throw new RecoveryCodeHttpError(0);}
export function rejectRecoveryCodeDelivery(value:unknown){if(value!==null&&typeof value==='object'&&Object.hasOwn(value,'recoveryCodeFirstDelivery'))unavailable();}
function parse<T>(schema:z.ZodType<T>,value:unknown):T{const result=schema.safeParse(value);if(!result.success)unavailable();return result.data;}
export function matchesRecoveryStart(expected:RecoveryCodeStart,actual:RecoveryCodeOperation){return actual.ownerRequestId===expected.ownerRequestId&&actual.expectedVersion===expected.expectedVersion&&actual.kind===expected.kind;}
export function matchesRecoveryOperation(expected:RecoveryCodeOperation,actual:RecoveryCodeOperation){const a=expected.impact,b=actual.impact;return matchesRecoveryStart(expected,actual)&&actual.operationReference===expected.operationReference&&actual.setReference===expected.setReference&&actual.ownerContextDigest===expected.ownerContextDigest&&actual.deliveryProfileVersion===expected.deliveryProfileVersion&&actual.formatProfileVersion===expected.formatProfileVersion&&actual.codesPerSet===expected.codesPerSet&&actual.maximumCodesPerSet===expected.maximumCodesPerSet&&b.factsDigest===a.factsDigest&&b.impactKey===a.impactKey&&b.publicationVersion===a.publicationVersion&&b.publicationDigest===a.publicationDigest&&b.publicationValidity.from===a.publicationValidity.from&&b.publicationValidity.until===a.publicationValidity.until&&b.publicationValidity.includesFrom===a.publicationValidity.includesFrom&&b.publicationValidity.includesUntil===a.publicationValidity.includesUntil;}
export function parseRecoveryOperation(value:unknown,expected?:RecoveryCodeStart|RecoveryCodeOperation){
 rejectRecoveryCodeDelivery(value);const result=parse(operation,value);
 if(expected&&(!matchesRecoveryStart(expected,result)||'operationReference' in expected&&!matchesRecoveryOperation(expected,result)))unavailable();return result;
}
export function parseRecoveryOptions(value:unknown){
 rejectRecoveryCodeDelivery(value);const result=parse(options,value);requirePublished(result.content,result.locale,[...requiredRecoveryCodeKeys,...(result.recovery?[result.recovery.impact.impactKey]:[])]);return result;
}
export function requireRecoveryImpact(value:RecoveryCodeOperation,boot:RecoveryCodeOptions){
 if(value.status!=='PENDING_PROOF'||!boot.recovery||!matchesRecoveryOperation(value,boot.recovery)||!boot.recovery.impact.canContinue)unavailable();requirePublished(boot.content,boot.locale,[value.impact.impactKey]);
}
const routes={options:'/account/recovery-codes/options',GENERATE:'/account/recovery-codes/generate/start',REPLACE:'/account/recovery-codes/replace/start',startResult:'/account/recovery-codes/start-result',result:'/account/recovery-codes/result',cancel:'/account/recovery-codes/cancel'} as const;
async function request(route:keyof typeof routes,signal:AbortSignal,csrfToken?:string,body?:unknown):Promise<unknown>{
 const response=await fetch(routes[route],{method:body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',redirect:'error',signal,...(body===undefined?{}:{headers:{'Content-Type':'application/json','X-Tunner-CSRF':csrfToken??''},body:JSON.stringify(body)})});
 if(!response.ok)throw new RecoveryCodeHttpError(response.status,response.status===401?'UNAUTHENTICATED':[403,404,409].includes(response.status)?'CHANGED':'UNAVAILABLE');
 return response.json() as Promise<unknown>;
}
export async function loadRecoveryCodeOptions(signal:AbortSignal){return parseRecoveryOptions(await request('options',signal));}
export async function startRecoveryCodeChange(boot:RecoveryCodeOptions,kind:RecoveryCodeKind,signal:AbortSignal){
 const snapshot:RecoveryCodeStart={ownerRequestId:boot.ownerRequestId,expectedVersion:boot.expectedVersion,kind};
 return parseRecoveryOperation(await request(kind,signal,boot.csrfToken,{ownerRequestId:snapshot.ownerRequestId,expectedVersion:snapshot.expectedVersion}),snapshot);
}
export async function readRecoveryCodeStart(boot:RecoveryCodeOptions,snapshot:RecoveryCodeStart,signal:AbortSignal){return parseRecoveryOperation(await request('startResult',signal,boot.csrfToken,{ownerRequestId:snapshot.ownerRequestId,expectedVersion:snapshot.expectedVersion,kind:snapshot.kind}),snapshot);}
export async function readRecoveryCodeOperation(boot:RecoveryCodeOptions,original:RecoveryCodeOperation,signal:AbortSignal,action:'result'|'cancel'='result'){
 return parseRecoveryOperation(await request(action,signal,boot.csrfToken,{ownerRequestId:original.ownerRequestId,operationReference:original.operationReference}),original);
}

const firstDelivery=z.object({kind:z.literal('RECOVERY_CODES_FIRST_DELIVERY_V1'),ownerRequestId:uuid,stepUpRequestId:uuid,operationReference:reference,setReference:uuid,ownerContextDigest:digest,deliveryProfileVersion:profile,formatProfileVersion:z.literal('NATIVE_HEX32_V1'),codes:z.array(z.string().regex(/^[0-9A-F]{32}$/)).nonempty()}).strict();
export type RecoveryCodeValues=readonly [string,...string[]];
/** Literal recovery-owned synchronous port. Generic StepUpCompletion never contains codes. */
export interface RecoveryCodeDeliveryReceiver {readonly operationReference:string;readonly stepUpRequestId:string;consume:(value:unknown)=>void}
export function recoveryCodeDeliveryReceiver(original:RecoveryCodeOperation,stepUpRequestId:string,current:()=>boolean,capture:(codes:RecoveryCodeValues)=>void):RecoveryCodeDeliveryReceiver{
 const expected={ownerRequestId:original.ownerRequestId,operationReference:original.operationReference,setReference:original.setReference,ownerContextDigest:original.ownerContextDigest,deliveryProfileVersion:original.deliveryProfileVersion,formatProfileVersion:original.formatProfileVersion,codesPerSet:original.codesPerSet,maximumCodesPerSet:original.maximumCodesPerSet};let consumed=false;
 return Object.freeze({operationReference:expected.operationReference,stepUpRequestId,consume(value:unknown){
  if(consumed||!current()||original.status!=='PENDING_PROOF'||!original.impact.canContinue||!uuid.safeParse(stepUpRequestId).success)unavailable();
  if(value===null||typeof value!=='object'||!('codes' in value)||!Array.isArray(value.codes)||value.codes.length!==expected.codesPerSet||value.codes.length>expected.maximumCodesPerSet)unavailable();
  const result=parse(firstDelivery,value);
  if(result.ownerRequestId!==expected.ownerRequestId||result.stepUpRequestId!==stepUpRequestId||result.operationReference!==expected.operationReference||result.setReference!==expected.setReference||result.ownerContextDigest!==expected.ownerContextDigest||result.deliveryProfileVersion!==expected.deliveryProfileVersion||result.formatProfileVersion!==expected.formatProfileVersion||new Set(result.codes).size!==result.codes.length||!current())unavailable();
  consumed=true;capture(result.codes as [string,...string[]]);
 }});
}
