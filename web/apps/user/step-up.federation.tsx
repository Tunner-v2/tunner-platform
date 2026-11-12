import {useEffect,useRef,useState} from 'react';
import {ContentText,z} from '@tunner/ui';
import {PasskeyHttpError,requestJson} from './passkeys.api';
import {federationChallengePath,federationCsrf} from './federation.api';
import {parseStepUpCompletion,rejectStepUpRecoveryDelivery} from './step-up.completion';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import type {RecoveryCodeDeliveryReceiver} from './recovery-codes.api';

const uuid=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const provider=z.enum(['Google','Microsoft']);
const statuses=z.enum(['PREPARED','CHALLENGE_ADMITTED','VERIFIED','PROOF_ISSUED','REJECTED','EXPIRED','UNKNOWN']);
const choiceSchema=z.object({provider,linkReference:uuid});
const recoverySchema=z.object({requestId:uuid,attemptId:uuid,provider,linkReference:uuid,status:statuses,expiresAt:z.iso.datetime({offset:true}),errorCode:z.string().max(128).nullable()});
export const stepUpFederationSchema=z.object({choices:z.array(choiceSchema),recovery:recoverySchema.nullable()}).superRefine((value,context)=>{if(new Set(value.choices.map(choice=>choice.provider+':'+choice.linkReference)).size!==value.choices.length)context.addIssue({code:'custom',message:'Duplicate proof source'});});
type Options=z.infer<typeof stepUpFederationSchema>;
type Choice=z.infer<typeof choiceSchema>;
type Recovery=z.infer<typeof recoverySchema>;
type Attempt=Choice&{requestId:string;operationReference:string;attemptId?:string;status:Recovery['status'];errorCode:string|null};
const envelopeSchema=z.object({requestId:uuid,operationReference:z.string().min(1),federation:recoverySchema,challengePath:z.enum(['/authentication/federation/google/challenge','/authentication/federation/microsoft/challenge']).nullable().optional()});
const ownerReceiptSchema=z.object({status:z.literal('COMPLETED'),operationReference:z.string().min(1)});
const cancelSchema=z.object({status:z.enum(['REJECTED','EXPIRED']),requestId:uuid,operationReference:z.string().min(1)});
type Mode='idle'|'pending'|'unknown'|'issued'|'changed';
interface Driver{bootstrap:StepUpBootstrap;blocked:()=>boolean;complete:(outcome:StepUpCompletion)=>void;dismiss:()=>void;reconcile?:((signal:AbortSignal)=>Promise<StepUpCompletion|null>)|undefined;recoveryCodeDelivery?:RecoveryCodeDeliveryReceiver|undefined}
class FederationProofError extends Error{constructor(readonly kind:'changed'|'limited'|'unavailable'='unavailable'){super('Verification unavailable');}}
function parse<T>(schema:z.ZodType<T>,value:unknown):T{const result=schema.safeParse(value);if(!result.success)throw new FederationProofError();return result.data;}
export function stepUpFederationKeys(options:Options|undefined){
 if(!options||!options.choices.length&&!options.recovery)return [];
 const providers=new Set([...options.choices.map(choice=>choice.provider),...(options.recovery?[options.recovery.provider]:[])]);
 return [...['description','source','pending','unknown','check','continue','cancel'].map(key=>'stepUp.federation.'+key),...[...providers].map(value=>'stepUp.federation.'+value)];
}
export function StepUpFederationChoices({options,disabled,onSelect}:{options:Options|undefined;disabled:boolean;onSelect:(choice:Choice)=>void}){
 return <>{options?.choices.map(choice=><button className="step-up-choice" key={choice.provider+':'+choice.linkReference} type="button" disabled={disabled} onClick={()=>onSelect(choice)}><strong><ContentText contentKey={'stepUp.federation.'+choice.provider}/></strong><small><ContentText contentKey="stepUp.federation.source"/></small><span aria-hidden="true">›</span></button>)}</>;
}
function modeFor(attempt:Attempt|undefined):Mode{return !attempt?'idle':attempt.status==='REJECTED'||attempt.status==='EXPIRED'?'changed':attempt.errorCode===null&&['VERIFIED','PROOF_ISSUED'].includes(attempt.status)?'issued':'unknown';}
export function useStepUpFederation(driver:Driver){
 const latest=useRef(driver);latest.current=driver;const alive=useRef(true),departed=useRef(false),control=useRef(new AbortController()),flight=useRef(false);
 const restored=driver.bootstrap.federation?.recovery;
 const original=useRef<Attempt|undefined>(restored?{...restored,operationReference:driver.bootstrap.operationReference!}:undefined);
 const [mode,setMode]=useState<Mode>(modeFor(original.current)),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
 useEffect(()=>{
  alive.current=true;departed.current=false;control.current=new AbortController();
  const leave=()=>{departed.current=true;control.current.abort();flight.current=false;};
  const returned=(event:PageTransitionEvent)=>{if(!event.persisted||!alive.current)return;departed.current=false;control.current=new AbortController();setBusy(false);if(original.current)setMode('unknown');};
  window.addEventListener('pagehide',leave);window.addEventListener('pageshow',returned);
  return()=>{alive.current=false;control.current.abort();window.removeEventListener('pagehide',leave);window.removeEventListener('pageshow',returned);};
 },[]);
 const current=(attempt:Attempt,signal:AbortSignal)=>alive.current&&!departed.current&&!signal.aborted&&control.current.signal===signal&&original.current===attempt;
 function running(value:boolean){flight.current=value;setBusy(value);}
 function finish(attempt:Attempt,signal:AbortSignal,outcome:StepUpCompletion){if(!current(attempt,signal))return;if(outcome.operationReference!==attempt.operationReference)throw new FederationProofError('changed');original.current=undefined;latest.current.complete(outcome);}
 async function post(action:'start'|'result'|'continue'|'cancel',attempt:Attempt,signal:AbortSignal,body:Record<string,unknown>):Promise<unknown>{
  try{const csrfToken=await federationCsrf(signal);if(!current(attempt,signal))throw new FederationProofError('changed');return await requestJson('/authentication/step-up/federation/'+action,{csrfToken,body,signal});}
  catch(reason){if(reason instanceof FederationProofError)throw reason;throw new FederationProofError(reason instanceof PasskeyHttpError?reason.status===429?'limited':[400,401,403,404,409].includes(reason.status)?'changed':'unavailable':'unavailable');}
 }
 function ownerReceipt(attempt:Attempt,signal:AbortSignal,raw:unknown){
  rejectStepUpRecoveryDelivery(raw);const receipt=ownerReceiptSchema.safeParse(raw);if(!receipt.success)return false;
  if(receipt.data.operationReference!==attempt.operationReference)throw new FederationProofError('changed');
  finish(attempt,signal,parseStepUpCompletion(raw,attempt.requestId,attempt.operationReference));return true;
 }
 function adopt(attempt:Attempt,signal:AbortSignal,raw:unknown){
  rejectStepUpRecoveryDelivery(raw);const response=parse(envelopeSchema,raw),value=response.federation;
  if(response.requestId!==attempt.requestId||response.operationReference!==attempt.operationReference||value.requestId!==attempt.requestId||value.provider!==attempt.provider||value.linkReference!==attempt.linkReference||attempt.attemptId!==undefined&&value.attemptId!==attempt.attemptId)throw new FederationProofError('changed');
  if(current(attempt,signal)){attempt.attemptId=value.attemptId;attempt.status=value.status;attempt.errorCode=value.errorCode;setMode(modeFor(attempt));}
  return response;
 }
 function failed(attempt:Attempt,signal:AbortSignal,reason:unknown){if(!current(attempt,signal))return;setMode('unknown');setError(reason instanceof FederationProofError&&reason.kind==='limited'?'stepUp.error.limited':reason instanceof FederationProofError&&reason.kind==='changed'?'stepUp.error.changed':'stepUp.error.unavailable');}
 async function owner(attempt:Attempt,signal:AbortSignal){
  if(!latest.current.reconcile)return false;const result=await latest.current.reconcile(signal);if(!current(attempt,signal))return false;rejectStepUpRecoveryDelivery(result);if(result?.status==='COMPLETED'){finish(attempt,signal,result);return true;}return false;
 }
 async function inspect(attempt:Attempt,signal:AbortSignal){
  const raw=await post('result',attempt,signal,{requestId:attempt.requestId});if(!current(attempt,signal))return true;
  if(ownerReceipt(attempt,signal,raw))return true;adopt(attempt,signal,raw);return false;
 }
 async function begin(choice:Choice){
  const boot=latest.current.bootstrap;
  if(!alive.current||departed.current||control.current.signal.aborted||flight.current||original.current||latest.current.blocked()||!boot.operationReference||!boot.federation?.choices.some(value=>value.provider===choice.provider&&value.linkReference===choice.linkReference))return;
  const attempt:Attempt={...choice,requestId:boot.requestId,operationReference:boot.operationReference,status:'PREPARED',errorCode:null};original.current=attempt;const signal=control.current.signal;running(true);setMode('pending');setError(undefined);
  try{const raw=await post('start',attempt,signal,{requestId:attempt.requestId,provider:attempt.provider,linkReference:attempt.linkReference});if(!current(attempt,signal))return;if(ownerReceipt(attempt,signal,raw))return;
   const response=adopt(attempt,signal,raw);if(response.federation.status!=='PREPARED'||response.federation.errorCode!==null||response.challengePath!==federationChallengePath(attempt.provider))throw new FederationProofError('changed');
   if(current(attempt,signal))window.location.assign(federationChallengePath(attempt.provider));
  }catch(reason){failed(attempt,signal,reason);}finally{if(current(attempt,signal))running(false);}
 }
 async function check(){const attempt=original.current;if(!attempt||flight.current)return;const signal=control.current.signal;running(true);setError(undefined);try{if(!await owner(attempt,signal)&&current(attempt,signal))await inspect(attempt,signal);}catch(reason){failed(attempt,signal,reason);}finally{if(current(attempt,signal))running(false);}}
 async function resume(){
  const attempt=original.current;if(!attempt||flight.current||mode!=='issued')return;const signal=control.current.signal;running(true);setError(undefined);
  try{
   if(await owner(attempt,signal)||!current(attempt,signal)||await inspect(attempt,signal)||!current(attempt,signal))return;
   if(!attempt.attemptId||attempt.errorCode!==null||!['VERIFIED','PROOF_ISSUED'].includes(attempt.status))throw new FederationProofError('changed');
   const raw=await post('continue',attempt,signal,{requestId:attempt.requestId});if(!current(attempt,signal))return;
   // The existing synchronous parser owns first-delivery capture and signInRequired. This driver never reads its payload.
   finish(attempt,signal,parseStepUpCompletion(raw,attempt.requestId,attempt.operationReference,latest.current.recoveryCodeDelivery));
  }catch(reason){failed(attempt,signal,reason);}finally{if(current(attempt,signal))running(false);}
 }
 async function cancel(){
  const attempt=original.current;if(!attempt||flight.current)return;const signal=control.current.signal;running(true);setError(undefined);
  try{const raw=await post('cancel',attempt,signal,{requestId:attempt.requestId});if(!current(attempt,signal))return;rejectStepUpRecoveryDelivery(raw);const result=parse(cancelSchema,raw);
   if(result.requestId!==attempt.requestId||result.operationReference!==attempt.operationReference)throw new FederationProofError('changed');
   // The method is acknowledged; its owner is preserved. Reopening is explicit and fetches a new server request.
   original.current=undefined;latest.current.dismiss();
  }catch(reason){failed(attempt,signal,reason);}finally{if(current(attempt,signal))running(false);}
 }
 return {mode,busy,error,active:!!original.current,canContinue:mode==='issued'&&!busy,begin,check,resume,cancel};
}
