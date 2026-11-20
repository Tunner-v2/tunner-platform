import {z} from '@tunner/ui';
import {rejectRecoveryCodeDelivery} from './recovery-codes.api';
import type {RecoveryCodeDeliveryReceiver} from './recovery-codes.api';

export interface StepUpCompletion{status:'COMPLETED';signInRequired:boolean;operationReference?:string}
export interface StepUpProofReady{status:'PROOF_READY';operationReference:string}
export type StepUpOutcome=StepUpCompletion|StepUpProofReady;
const completion=z.object({status:z.literal('COMPLETED'),signInRequired:z.boolean().optional(),operationReference:z.string().min(1).optional()});
const proofReady=z.object({status:z.literal('PROOF_READY'),operationReference:z.string().min(1).refine(value=>value.trim().length>0)});
/** Safe read/reconcile responses have no first-delivery grant. */
export const rejectStepUpRecoveryDelivery=rejectRecoveryCodeDelivery;
/** Readiness is accepted only by an explicit exact-owner opt-in; it never carries first-delivery authority. */
export function parseStepUpProofReady(value:unknown,expectedOperation:string|undefined,allowed:boolean):StepUpProofReady{
 rejectStepUpRecoveryDelivery(value);
 const result=proofReady.safeParse(value);
 if(!allowed||!expectedOperation?.trim()||!result.success||result.data.operationReference!==expectedOperation||
   value!==null&&typeof value==='object'&&Object.hasOwn(value,'signInRequired'))throw new Error('Step-up proof readiness unavailable');
 return result.data;
}
export function parseStepUpOutcome(value:unknown,requestId:string,expectedOperation:string|undefined,receiver?:RecoveryCodeDeliveryReceiver,allowProofReady=false):StepUpOutcome{
 if(value!==null&&typeof value==='object'&&'status' in value&&value.status==='PROOF_READY')return parseStepUpProofReady(value,expectedOperation,allowProofReady);
 return parseStepUpCompletion(value,requestId,expectedOperation,receiver);
}
/** Native method identity is checked by its driver before this common synchronous projection. */
export function parseStepUpCompletion(value:unknown,requestId:string,expectedOperation:string|undefined,receiver?:RecoveryCodeDeliveryReceiver):StepUpCompletion{
 const result=completion.safeParse(value);if(!result.success||expectedOperation!==undefined&&result.data.operationReference!==expectedOperation)throw new Error('Step-up completion unavailable');
 const delivery=value!==null&&typeof value==='object'&&Object.hasOwn(value,'recoveryCodeFirstDelivery');
 if(delivery){
  if(!receiver||receiver.stepUpRequestId!==requestId||receiver.operationReference!==expectedOperation||result.data.signInRequired===true||!result.data.operationReference)throw new Error('Step-up completion unavailable');
  receiver.consume((value as {recoveryCodeFirstDelivery:unknown}).recoveryCodeFirstDelivery);
 }
 return {status:'COMPLETED',signInRequired:result.data.signInRequired===true,...(result.data.operationReference?{operationReference:result.data.operationReference}:{})};
}
