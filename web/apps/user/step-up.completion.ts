import {z} from '@tunner/ui';
import {rejectRecoveryCodeDelivery} from './recovery-codes.api';
import type {RecoveryCodeDeliveryReceiver} from './recovery-codes.api';

export interface StepUpCompletion{status:'COMPLETED';signInRequired:boolean;operationReference?:string}
const completion=z.object({status:z.literal('COMPLETED'),signInRequired:z.boolean().optional(),operationReference:z.string().min(1).optional()});
/** Safe read/reconcile responses have no first-delivery grant. */
export const rejectStepUpRecoveryDelivery=rejectRecoveryCodeDelivery;
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
