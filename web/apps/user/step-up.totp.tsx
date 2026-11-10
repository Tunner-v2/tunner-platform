import {useEffect,useId,useRef,useState} from 'react';
import {ContentText,FormErrorSummary,ModalDialog,PrimaryButton,useTunnerForm,z} from '@tunner/ui';
import type {StepUpBootstrap,StepUpCompletion,StepUpDismissal} from './step-up';
import {TotpHttpError,requestTotpJson} from './totp.api';
import type {TotpChoice} from './totp.api';
import {TotpCodeField} from './totp.code';
import {sameTotpChoice,TotpFactorPicker,TotpSelectedFactor} from './totp.picker';
import {totpCodeSchema} from './totp.validation';
import './totp.css';

export const stepUpTotpRecoverySchema=z.object({requestId:z.uuid(),attemptId:z.uuid(),authenticatorReference:z.uuid(),status:z.enum(['PROOF_ACTIVE','CONSUMED','EXPIRED']),expiresAt:z.iso.datetime({offset:true})});
type Recovery=z.infer<typeof stepUpTotpRecoverySchema>;
type Attempt=Pick<Recovery,'requestId'|'attemptId'|'authenticatorReference'>&{expiresAt?:string;status?:Recovery['status']};
type Context=StepUpBootstrap|{status:'COMPLETED';requestId:string|null;operationReference?:string|undefined;totpRecovery?:Recovery|null|undefined};
interface Props {bootstrap:StepUpBootstrap;load:(signal:AbortSignal)=>Promise<Context>;onComplete:(value:StepUpCompletion)=>void;onCancel:(value?:StepUpDismissal)=>void;onBack:()=>void;returnFocusTo?:HTMLElement|null|undefined;reconcile?:((signal:AbortSignal)=>Promise<StepUpCompletion|null>)|undefined}
const completion=z.object({status:z.literal('COMPLETED'),operationReference:z.string().min(1),signInRequired:z.boolean()});
export function StepUpTotpDialog(props:Props){
 const {bootstrap}=props,id=useId();const [factors,setFactors]=useState(bootstrap.totpAuthenticators??[]),[selected,setSelected]=useState<TotpChoice|undefined>(bootstrap.totpAuthenticators?.length===1?bootstrap.totpAuthenticators[0]:undefined);
 const original=useRef<Attempt|undefined>(bootstrap.totpRecovery??undefined),controller=useRef(new AbortController()),alive=useRef(true),flight=useRef(false),csrf=useRef(bootstrap.csrfToken);
 const [phase,setPhase]=useState<'entry'|'pending'|'unknown'|'issued'|'expired'>(original.current?original.current.status==='EXPIRED'?'expired':'unknown':'entry'),[code,setCode]=useState(''),[field,setField]=useState<string>(),[error,setError]=useState<string>();
 const form=useTunnerForm(totpCodeSchema({required:'stepUp.totp.error.required',invalid:'stepUp.totp.error.code'}),{code});
 useEffect(()=>{alive.current=true;controller.current=new AbortController();return()=>{alive.current=false;controller.current.abort();};},[]);
 useEffect(()=>{document.getElementById(selected&&phase==='entry'?id+'-code':id+'-heading')?.focus();},[selected,phase,id]);
 useEffect(()=>{if(field&&phase==='entry')document.getElementById(id+'-code')?.focus();},[field,phase,id]);
 const picker=!selected&&factors.length>0&&phase==='entry';const current=()=>alive.current&&!controller.current.signal.aborted;
 function choose(factor:TotpChoice){if(flight.current)return;setCode('');form.reset();setSelected(factor);original.current=undefined;setError(undefined);setField(undefined);}
 function finish(value:StepUpCompletion){if(value.operationReference!==bootstrap.operationReference)throw new TotpHttpError(409,'VERSION_CONFLICT');if(current()){setCode('');props.onComplete(value);}}
 async function owner(){if(!props.reconcile)return false;const result=await props.reconcile(controller.current.signal);if(!current())return true;if(result){finish(result);return true;}return false;}
 async function inspect(){
  if(await owner())return true;const value=await props.load(controller.current.signal);if(!current())return true;
  if(value.requestId!==bootstrap.requestId||value.operationReference!==bootstrap.operationReference)throw new TotpHttpError(409,'VERSION_CONFLICT');
  const recovery=value.totpRecovery;
  if(recovery&&(recovery.requestId!==bootstrap.requestId||original.current&&(recovery.attemptId!==original.current.attemptId||recovery.authenticatorReference!==original.current.authenticatorReference)))throw new TotpHttpError(409,'VERSION_CONFLICT');
  if(value.status==='COMPLETED'){if(!original.current||!recovery||recovery.status!=='CONSUMED'||!value.operationReference)throw new TotpHttpError(409,'VERSION_CONFLICT');finish({status:'COMPLETED',signInRequired:false,operationReference:value.operationReference});return true;}
  csrf.current=value.csrfToken;setFactors(value.totpAuthenticators??[]);
  if(recovery){original.current=recovery;setPhase(recovery.status==='PROOF_ACTIVE'?'issued':recovery.status==='EXPIRED'?'expired':'unknown');}
  else{setSelected(previous=>previous&&(value.totpAuthenticators??[]).some(factor=>sameTotpChoice(previous,factor))?previous:undefined);setPhase(original.current?'unknown':'entry');}
  return false;
 }
 function completed(value:unknown){const parsed=completion.safeParse(value);if(!parsed.success)throw new TotpHttpError(0,'OUTCOME_UNKNOWN');finish(parsed.data);}
 async function submit(data:{code:string}){
  if(flight.current||!selected||!bootstrap.operationReference)return;flight.current=true;setPhase('pending');setCode('');setField(undefined);setError(undefined);
  original.current??={requestId:bootstrap.requestId,attemptId:crypto.randomUUID(),authenticatorReference:selected.authenticatorReference};
  try{completed(await requestTotpJson('/authentication/step-up/totp',{csrfToken:csrf.current,body:{requestId:original.current.requestId,attemptId:original.current.attemptId,authenticatorReference:original.current.authenticatorReference,code:data.code},signal:controller.current.signal}));}
  catch(reason){if(!current())return;try{if(await inspect())return;if(original.current?.expiresAt)return;}catch{/* Keep the original proof attempt and recover only its exact owner context. */}
   if(current()){if(reason instanceof TotpHttpError&&['AUTHENTICATION_FAILED','VALIDATION_ERROR','CODE_REPLAYED'].includes(reason.code)){setPhase('entry');setField(reason.code==='CODE_REPLAYED'?'stepUp.totp.error.replayed':reason.code==='VALIDATION_ERROR'?'stepUp.totp.error.code':'stepUp.totp.error.credentials');}else if(reason instanceof TotpHttpError&&reason.status===429){setPhase('entry');setError('stepUp.error.limited');}else{setPhase('unknown');setError('stepUp.error.unavailable');}}}
  finally{if(current()){setCode('');form.reset();}flight.current=false;}
 }
 async function check(){if(flight.current)return;flight.current=true;setPhase('pending');setCode('');setError(undefined);try{await inspect();}catch{if(current()){setPhase('unknown');setError('stepUp.error.unavailable');}}finally{flight.current=false;}}
 async function resume(){const attempt=original.current;if(flight.current||!attempt||!['unknown','issued'].includes(phase))return;flight.current=true;setPhase('pending');setCode('');setError(undefined);try{completed(await requestTotpJson('/authentication/step-up/totp/continue',{csrfToken:csrf.current,body:{requestId:attempt.requestId,attemptId:attempt.attemptId,authenticatorReference:attempt.authenticatorReference},signal:controller.current.signal}));}catch{try{await inspect();}catch{if(current()){setPhase('unknown');setError('stepUp.error.unavailable');}}}finally{flight.current=false;}}
 function close(){if(flight.current)return;setCode('');props.onCancel(phase==='unknown'||phase==='issued'?{unresolved:true}:undefined);}
 return <ModalDialog labelledBy={id+'-heading'} describedBy={id+'-description'} className="step-up-dialog" busy={phase==='pending'} {...(props.returnFocusTo?{returnFocusTo:props.returnFocusTo}:{})} onRequestClose={close}><div className="step-up-card password totp-proof" data-tunner-component="COMP-STEP-UP-TOTP"><h1 id={id+'-heading'} tabIndex={-1}><ContentText contentKey={picker?'totp.selection.heading':'stepUp.heading'}/></h1><p className="step-up-intro" id={id+'-description'}><ContentText contentKey={picker?'totp.selection.proofDescription':'stepUp.totp.description'}/></p>{picker&&<TotpFactorPicker factors={factors} locale={bootstrap.locale} labelledBy={id+'-heading'} onSelect={choose}/ >}{phase==='entry'&&selected&&<form noValidate data-tunner-form="FORM-STEP-UP-TOTP" onSubmit={event=>void form.handleSubmit(event,submit)}>{factors.length>1&&<TotpSelectedFactor factor={selected} locale={bootstrap.locale} onChange={()=>{setCode('');setSelected(undefined);original.current=undefined;form.reset();setField(undefined);setError(undefined);}}/>}<TotpCodeField id={id+'-code'} proof code={code} errorKey={field??form.errors.code} onBlur={event=>form.onBlur('code',event)} onChange={value=>{setCode(value);setField(undefined);setError(undefined);}}/>{form.hasErrors&&<FormErrorSummary contentKey="stepUp.error.highlighted"/>}<div className="step-up-actions"><PrimaryButton labelKey="stepUp.back" appearance="outline" onClick={()=>{setCode('');props.onBack();}}/><PrimaryButton labelKey="stepUp.cancel" appearance="outline" onClick={close}/><PrimaryButton labelKey="stepUp.confirm" type="submit" disabled={form.submitPending}/></div></form>}{phase!=='entry'&&<p className="step-up-status" role="status"><ContentText contentKey={phase==='pending'?'stepUp.totp.pending':phase==='expired'?'stepUp.totp.expired':'stepUp.totp.unknown'}/></p>}{error&&<FormErrorSummary contentKey={error}/ >}{!(phase==='entry'&&selected)&&<div className="step-up-actions"><PrimaryButton labelKey={phase==='unknown'||phase==='issued'?'stepUp.totp.close':'stepUp.cancel'} appearance="outline" disabled={phase==='pending'} onClick={close}/>{(phase==='unknown'||phase==='issued')&&<><PrimaryButton labelKey="stepUp.totp.check" appearance="outline" onClick={()=>void check()}/>{original.current&&<PrimaryButton labelKey="stepUp.passkey.resume" onClick={()=>void resume()}/>}</>}</div>}</div></ModalDialog>;
}
