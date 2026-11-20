import {useEffect,useRef,useState} from 'react';
import {z} from '@tunner/ui';
import {getPasskey,passkeysSupported} from './passkeys.native';
import {PasskeyHttpError,requestJson} from './passkeys.api';
import type {StepUpBootstrap,StepUpCompletion} from './step-up';
import {parseStepUpOutcome,parseStepUpProofReady,rejectStepUpRecoveryDelivery} from './step-up.completion';
import type {StepUpOutcome,StepUpProofReady} from './step-up.completion';
import type {RecoveryCodeDeliveryReceiver} from './recovery-codes.api';

export const stepUpPasskeyKeys=['pending','notCompleted','unknown','check','resume','unsupported','close'].map(key=>'stepUp.passkey.'+key);
const statuses=z.enum(['PENDING','PROOF_ISSUED','REJECTED','EXPIRED']);
export const stepUpPasskeyRecoverySchema=z.object({requestId:z.uuid(),attemptId:z.uuid(),ceremonyId:z.uuid(),operationReference:z.string().min(1),status:statuses,expiresAt:z.iso.datetime({offset:true}),publicKey:z.unknown().nullable()});
type Recovery=z.infer<typeof stepUpPasskeyRecoverySchema>;
type Attempt={readonly requestId:string;readonly attemptId:string;readonly operationReference:string;ceremonyId?:string;status:Recovery['status'];publicKey?:unknown;credential?:unknown};
type Mode='idle'|'pending'|'unknown'|'issued'|'not-completed'|'changed';
type Context=StepUpBootstrap|StepUpProofReady|{status:'COMPLETED';requestId:string|null;operationReference?:string|undefined};
interface Driver{bootstrap:StepUpBootstrap;load:(signal:AbortSignal)=>Promise<Context>;complete:(outcome:StepUpCompletion)=>void;proofReady?:((outcome:StepUpProofReady)=>void)|undefined;reconcile?:((signal:AbortSignal)=>Promise<StepUpOutcome|null>)|undefined;recoveryCodeDelivery?:RecoveryCodeDeliveryReceiver|undefined}
const completionSchema=z.object({status:z.literal('COMPLETED'),requestId:z.uuid(),attemptId:z.uuid(),ceremonyId:z.uuid(),operationReference:z.string().min(1),signInRequired:z.boolean()});
const proofReadySchema=completionSchema.omit({status:true,signInRequired:true}).extend({status:z.literal('PROOF_READY')});
const problemSchema=z.object({ceremony_status:statuses,requestId:z.uuid(),attemptId:z.uuid(),ceremonyId:z.uuid(),operationReference:z.string().min(1)});
function matches(attempt:Attempt,value:{requestId:string;attemptId:string;ceremonyId:string;operationReference:string}){return attempt.requestId===value.requestId&&attempt.attemptId===value.attemptId&&attempt.operationReference===value.operationReference&&(attempt.ceremonyId===undefined||attempt.ceremonyId===value.ceremonyId);}
function restored(bootstrap:StepUpBootstrap):Attempt|undefined{const recovery=bootstrap.passkeyRecovery;if(!recovery)return undefined;if(recovery.requestId!==bootstrap.requestId||recovery.operationReference!==bootstrap.operationReference)throw new Error('The original proof context changed.');return {...recovery,publicKey:undefined};}
function recoveryMode(attempt:Attempt|undefined):Mode{return !attempt?'idle':attempt.status==='PROOF_ISSUED'?'issued':attempt.status==='PENDING'?'unknown':'changed';}
export function useStepUpPasskey(driver:Driver){
  const latest=useRef(driver);latest.current=driver;const alive=useRef(true);const controller=useRef(new AbortController());const flight=useRef(false);const original=useRef<Attempt|undefined>(restored(driver.bootstrap));const csrf=useRef(driver.bootstrap.csrfToken);
  const [mode,setMode]=useState<Mode>(recoveryMode(original.current));const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();const [available,setAvailable]=useState(driver.bootstrap.passkeyAvailable===true);const availability=useRef(available);const maximum=useRef(driver.bootstrap.maximumCredentialJsonBytes);
  useEffect(()=>{alive.current=true;controller.current=new AbortController();return()=>{alive.current=false;controller.current.abort();if(original.current)original.current.credential=undefined;};},[]);
  const current=(attempt:Attempt)=>alive.current&&!controller.current.signal.aborted&&original.current===attempt;
  function working(value:boolean){flight.current=value;setBusy(value);}
  function finish(attempt:Attempt,outcome:StepUpOutcome){if(!current(attempt))return;
    if(outcome.status==='PROOF_READY'){const ready=parseStepUpProofReady(outcome,attempt.operationReference,!!latest.current.proofReady);attempt.credential=undefined;original.current=undefined;latest.current.proofReady!(ready);}
    else{attempt.credential=undefined;original.current=undefined;latest.current.complete(outcome);}
  }
  function adopt(attempt:Attempt,value:Pick<Recovery,'requestId'|'attemptId'|'ceremonyId'|'operationReference'|'status'|'publicKey'>){if(!matches(attempt,value))throw new Error('The original native proof changed.');attempt.ceremonyId=value.ceremonyId;attempt.status=value.status;attempt.publicKey=value.publicKey;if(current(attempt)){setMode(recoveryMode(attempt));if(value.status==='REJECTED'||value.status==='EXPIRED')setError('stepUp.error.changed');}}
  function final(attempt:Attempt,value:unknown){if(!current(attempt))return;const result=z.union([completionSchema,proofReadySchema]).parse(value);if(!matches(attempt,result))throw new Error('The original completion changed.');finish(attempt,parseStepUpOutcome(value,attempt.requestId,attempt.operationReference,latest.current.recoveryCodeDelivery,!!latest.current.proofReady));}
  async function reconcile(attempt:Attempt){
    const owner=latest.current.reconcile?await latest.current.reconcile(controller.current.signal):null;if(!current(attempt))return true;
    rejectStepUpRecoveryDelivery(owner);if(owner){if(owner.operationReference!==attempt.operationReference)throw new Error('The owner result changed.');finish(attempt,parseStepUpOutcome(owner,attempt.requestId,attempt.operationReference,undefined,!!latest.current.proofReady));return true;}
    const context=await latest.current.load(controller.current.signal);if(!current(attempt))return true;
    if(context.status==='PROOF_READY'){finish(attempt,parseStepUpProofReady(context,attempt.operationReference,!!latest.current.proofReady));return true;}
    if(context.requestId!==attempt.requestId||context.operationReference!==attempt.operationReference)throw new Error('The original proof view changed.');
    if(context.status==='COMPLETED'){finish(attempt,{status:'COMPLETED',signInRequired:false,operationReference:attempt.operationReference});return true;}
    csrf.current=context.csrfToken;maximum.current=context.maximumCredentialJsonBytes;availability.current=context.passkeyAvailable===true;setAvailable(availability.current);if(context.passkeyRecovery)adopt(attempt,context.passkeyRecovery);else setMode('unknown');return false;
  }
  function knownProblem(attempt:Attempt,reason:unknown){if(!(reason instanceof PasskeyHttpError))return false;const result=problemSchema.safeParse(reason.details);if(!result.success||!matches(attempt,result.data))return false;adopt(attempt,{...result.data,status:result.data.ceremony_status,publicKey:null});return result.data.ceremony_status==='REJECTED'||result.data.ceremony_status==='EXPIRED';}
  async function uncertain(attempt:Attempt,reason:unknown){if(!current(attempt))return;if(knownProblem(attempt,reason))return;try{if(await reconcile(attempt))return;}catch{/* A transport/status error cannot replace the original binding or prove completion. */}if(current(attempt)){if(attempt.status==='PROOF_ISSUED')setMode('issued');else if(attempt.status==='REJECTED'||attempt.status==='EXPIRED')setMode('changed');else setMode('unknown');setError(reason instanceof PasskeyHttpError&&reason.status===429?'stepUp.error.limited':'stepUp.error.unavailable');}}
  async function options(attempt:Attempt){const raw=await requestJson('/authentication/step-up/passkey/options',{csrfToken:csrf.current,body:{requestId:attempt.requestId,attemptId:attempt.attemptId},signal:controller.current.signal});rejectStepUpRecoveryDelivery(raw);if(!current(attempt))return;if(raw!==null&&typeof raw==='object'&&'status' in raw&&raw.status==='PROOF_READY'){finish(attempt,parseStepUpProofReady(raw,attempt.operationReference,!!latest.current.proofReady));return;}const value=stepUpPasskeyRecoverySchema.parse(raw);adopt(attempt,value);}
  async function execute(attempt:Attempt){
    if(!attempt.ceremonyId)throw new Error('The original native ceremony is required.');
    if(attempt.status==='PROOF_ISSUED'){final(attempt,await requestJson('/authentication/step-up/passkey/continue',{csrfToken:csrf.current,body:{requestId:attempt.requestId,attemptId:attempt.attemptId,ceremonyId:attempt.ceremonyId},signal:controller.current.signal}));return;}
    if(attempt.status!=='PENDING')throw new Error('The original attempt is no longer available.');
    if(attempt.credential===undefined){if(!availability.current||!passkeysSupported())throw new Error('Native verification is unavailable.');if(!attempt.publicKey)await options(attempt);if(!current(attempt))return;if(attempt.status!=='PENDING'||!attempt.publicKey||!maximum.current)throw new Error('The original native challenge is required.');setMode('pending');try{attempt.credential=await getPasskey(attempt.publicKey,maximum.current,controller.current.signal);}catch(reason){if(reason instanceof DOMException&&['NotAllowedError','AbortError'].includes(reason.name)&&current(attempt)){setMode('not-completed');setError(undefined);return;}throw reason;}}
    if(!current(attempt))return;final(attempt,await requestJson('/authentication/step-up/passkey',{csrfToken:csrf.current,body:{requestId:attempt.requestId,attemptId:attempt.attemptId,ceremonyId:attempt.ceremonyId,credential:attempt.credential},signal:controller.current.signal}));
  }
  async function begin(){const bootstrap=latest.current.bootstrap;if(flight.current||!availability.current||!bootstrap.operationReference||!passkeysSupported()||original.current&&mode!=='idle')return;const attempt:Attempt=original.current??{requestId:bootstrap.requestId,attemptId:crypto.randomUUID(),operationReference:bootstrap.operationReference,status:'PENDING'};original.current=attempt;working(true);setError(undefined);setMode('pending');try{await options(attempt);if(current(attempt))await execute(attempt);}catch(reason){await uncertain(attempt,reason);}finally{if(alive.current)working(false);}}
  async function check(){const attempt=original.current;if(!attempt||flight.current)return;working(true);setError(undefined);try{await reconcile(attempt);}catch(reason){await uncertain(attempt,reason);}finally{if(alive.current)working(false);}}
  async function resume(){const attempt=original.current;if(!attempt||flight.current)return;working(true);setError(undefined);try{if(await reconcile(attempt))return;if(!attempt.ceremonyId)await options(attempt);if(current(attempt))await execute(attempt);}catch(reason){await uncertain(attempt,reason);}finally{if(alive.current)working(false);}}
  function back(){if(flight.current||mode!=='not-completed')return;setError(undefined);setMode('idle');}
  return {mode,busy,error,active:mode!=='idle',unresolved:!!original.current&&(mode==='unknown'||mode==='issued'||mode==='pending'),canBegin:available&&passkeysSupported(),canResume:original.current?.status==='PROOF_ISSUED'||available&&passkeysSupported(),supported:passkeysSupported(),available,begin,check,resume,back};
}
