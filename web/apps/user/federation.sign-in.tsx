import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {ContentText,FormErrorSummary,PrimaryButton} from '@tunner/ui';
import {parseMfaRequired} from './totp.api';
import {FederationHttpError,federationChallengePath,federationCsrf,federationPost,parseFederationResult} from './federation.api';
import type {FederationBootstrap,FederationOriginal,FederationPending,FederationProvider} from './federation.api';

export const federationSignInKeys=['federation.signIn.ready','federation.signIn.readyDescription','federation.signIn.complete','federation.signIn.pending','federation.signIn.pendingDescription','federation.signIn.unknown','federation.signIn.unknownDescription','federation.signIn.check','federation.signIn.cancel','federation.signIn.expired','federation.signIn.expiredDescription','federation.signIn.notCompleted','federation.signIn.notCompletedDescription','federation.signIn.back','federation.signIn.changed','federation.signIn.unavailable'] as const;
interface Props{
  configuration:FederationBootstrap|undefined;rememberMe:boolean;disabled:boolean;unavailableTitle:string;
  canStart:()=>boolean;isCurrent:()=>boolean;onBusyChange:(busy:boolean)=>void;onAuthenticated:()=>void;onMfaRequired:()=>void;onReset:(signal:AbortSignal)=>Promise<void>;
}
type Attempt=FederationOriginal;
type Phase='ready'|'working'|'verified'|'unknown'|'expired'|'notCompleted';
function phaseFor(value:FederationPending):Phase{return value.status==='EXPIRED'?'expired':value.status==='REJECTED'?'notCompleted':value.status==='VERIFIED'&&value.errorCode===null?'verified':'unknown';}

/** Existing provider slots plus safe original-result controls; no provider/token client or implicit session success. */
export function FederationSignIn(props:Props){
  const latest=useRef(props);latest.current=props;
  const mounted=useRef(true),controller=useRef(new AbortController()),inFlight=useRef(false);
  const initial=props.configuration?.pending;
  const original=useRef<Attempt|undefined>(initial?{requestId:initial.requestId,attemptId:initial.attemptId}:undefined);
  const [phase,setPhase]=useState<Phase>(initial?phaseFor(initial):'ready');
  const [working,setWorking]=useState(false),[error,setError]=useState<string>();
  const actions=useRef<HTMLDivElement>(null),restoreFocus=useRef(false);
  useLayoutEffect(()=>{if(original.current)latest.current.onBusyChange(true);},[]);
  useEffect(()=>()=>{mounted.current=false;controller.current.abort();latest.current.onBusyChange(false);},[]);
  useEffect(()=>{
    const pending=props.configuration?.pending;
    if(!pending||inFlight.current||original.current||phase!=='ready')return;
    original.current={requestId:pending.requestId,attemptId:pending.attemptId};setPhase(phaseFor(pending));latest.current.onBusyChange(true);
  },[props.configuration,phase]);
  useLayoutEffect(()=>{
    if(!restoreFocus.current||working||!mounted.current||!latest.current.isCurrent())return;
    restoreFocus.current=false;actions.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
  },[phase,working]);
  const current=(attempt:Attempt)=>mounted.current&&!controller.current.signal.aborted&&latest.current.isCurrent()&&original.current===attempt;
  function pending(value:boolean){inFlight.current=value;setWorking(value);}
  function unknown(attempt:Attempt,reason?:unknown){
    if(!current(attempt))return;setPhase('unknown');latest.current.onBusyChange(true);
    setError(reason instanceof FederationHttpError&&reason.code==='VERSION_CONFLICT'?'federation.signIn.changed':reason instanceof FederationHttpError&&reason.code==='RATE_OR_ABUSE_LIMITED'?'authentication.error.limited':undefined);
  }
  function accept(attempt:Attempt,raw:unknown){
    if(!current(attempt))return;
    if(parseMfaRequired(raw,attempt.requestId)){original.current=undefined;latest.current.onBusyChange(false);latest.current.onMfaRequired();return;}
    const result=parseFederationResult(raw,attempt);
    if(!current(attempt))return;
    attempt.attemptId=result.operation.attemptId;
    if(result.authenticated){
      if(result.operation.status!=='COMPLETED'||result.operation.errorCode!==null)throw new FederationHttpError(0,'OUTCOME_UNKNOWN');
      original.current=undefined;latest.current.onBusyChange(false);latest.current.onAuthenticated();return;
    }
    // Even COMPLETED in bootstrap or a surviving session is not an authenticated original result.
    setPhase(phaseFor(result.operation));latest.current.onBusyChange(true);
  }
  async function start(provider:FederationProvider){
    const config=latest.current.configuration;
    if(inFlight.current||original.current||latest.current.disabled||!latest.current.canStart()||!latest.current.isCurrent()||!(provider==='Google'?config?.googleAvailable:config?.microsoftAvailable))return;
    const attempt:Attempt={requestId:crypto.randomUUID()};original.current=attempt;
    restoreFocus.current=true;pending(true);latest.current.onBusyChange(true);setPhase('working');setError(undefined);
    const rememberMe=latest.current.rememberMe;
    try{
      const csrf=await federationCsrf(controller.current.signal);if(!current(attempt))return;
      const raw=await federationPost('start',csrf,{requestId:attempt.requestId,provider,rememberMe},controller.current.signal);
      if(!current(attempt))return;
      const result=parseFederationResult(raw,attempt);
      if(result.authenticated||result.operation.status!=='PREPARED'||result.operation.errorCode!==null||result.challengePath!==federationChallengePath(provider))throw new FederationHttpError(0,'OUTCOME_UNKNOWN');
      attempt.attemptId=result.operation.attemptId;
      if(!current(attempt))return;
      // Only a fresh, exact prepared Start admits this fixed clean local challenge. History/result never does.
      window.location.assign(result.challengePath);
    }catch(reason){unknown(attempt,reason);}
    finally{if(current(attempt)){pending(false);setPhase(value=>value==='working'?'unknown':value);}}
  }
  async function action(kind:'result'|'complete'|'cancel'){
    const attempt=original.current;if(!attempt||inFlight.current||!current(attempt)||kind==='complete'&&phase!=='verified')return;
    restoreFocus.current=true;pending(true);setError(undefined);
    try{
      const csrf=await federationCsrf(controller.current.signal);if(!current(attempt))return;
      const raw=await federationPost(kind,csrf,{},controller.current.signal);if(!current(attempt))return;
      if(kind==='cancel'&&!parseMfaRequired(raw,attempt.requestId)){
        const result=parseFederationResult(raw,attempt);
        if(!result.authenticated&&['REJECTED','EXPIRED'].includes(result.operation.status)){
          // Acknowledge only the same terminal original operation; refresh normal anonymous entry afterwards.
          await latest.current.onReset(controller.current.signal);if(!current(attempt))return;
          original.current=undefined;setPhase('ready');latest.current.onBusyChange(false);return;
        }
      }
      accept(attempt,raw);
    }catch(reason){unknown(attempt,reason);}
    finally{if(mounted.current&&latest.current.isCurrent())pending(false);}
  }
  const held=original.current!==undefined;
  const title=phase==='verified'?'ready':phase==='expired'?'expired':phase==='notCompleted'?'notCompleted':phase==='working'?'pending':'unknown';
  return <>
    <div className="authentication-providers">
      <button type="button" disabled={!props.configuration?.googleAvailable||props.disabled||held||working} {...(!props.configuration?.googleAvailable?{title:props.unavailableTitle}:{})} {...(working?{'aria-busy':true}:{})} onClick={()=>void start('Google')}><ContentText contentKey="login.google"/><img src="/registration/assets/login-google.svg" alt=""/></button>
      <button type="button" disabled={!props.configuration?.microsoftAvailable||props.disabled||held||working} {...(!props.configuration?.microsoftAvailable?{title:props.unavailableTitle}:{})} {...(working?{'aria-busy':true}:{})} onClick={()=>void start('Microsoft')}><ContentText contentKey="login.microsoft"/></button>
    </div>
    {phase!=='ready'&&<div className="authentication-federation" data-tunner-component="COMP-FEDERATION-SIGN-IN">
      <p role="status"><strong><ContentText contentKey={'federation.signIn.'+title}/></strong><ContentText contentKey={'federation.signIn.'+title+'Description'}/></p>
      <div ref={actions} className="authentication-federation-actions">
        {phase==='verified'?<PrimaryButton labelKey="federation.signIn.complete" disabled={working} aria-busy={working} onClick={()=>void action('complete')}/>:phase!=='expired'&&phase!=='notCompleted'&&<PrimaryButton labelKey="federation.signIn.check" appearance="outline" disabled={working} onClick={()=>void action('result')}/>}
        <PrimaryButton labelKey={phase==='expired'||phase==='notCompleted'?'federation.signIn.back':'federation.signIn.cancel'} appearance="outline" disabled={working} onClick={()=>void action('cancel')}/>
      </div>
      {error&&<FormErrorSummary contentKey={error}/>}
    </div>}
  </>;
}
