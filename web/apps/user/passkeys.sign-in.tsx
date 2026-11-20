import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {ContentText,FormErrorSummary,PrimaryButton,z} from '@tunner/ui';
import {assertionCompleted,parseAuthenticationPasskeyRecovery,parseOperation,PasskeyHttpError,requestJson,terminalCompleted} from './passkeys.api';
import type {PasskeyOperation} from './passkeys.api';
import {getPasskey,passkeysSupported} from './passkeys.native';
import {parseMfaRequired} from './totp.api';

export interface PasskeySignInProps{available:boolean;maximumCredentialJsonBytes:number;csrfToken:string;rememberMe:boolean;disabled:boolean;recovery?:unknown;onBusyChange:(busy:boolean)=>void;onAuthenticated:()=>void;onMfaRequired?:()=>void}
export const loginPasskeyKeys=['login.passkey.submit','login.passkey.pending','login.passkey.notCompleted','login.passkey.unknown','login.passkey.check','login.passkey.resume','login.passkey.unsupported','login.passkey.changed','login.passkey.limited','login.passkey.removed','login.passkey.sessionEnded'] as const;
type Attempt={requestId:string;rememberMe:boolean;csrfToken:string;operation?:PasskeyOperation;credential?:unknown};
const readOnlyState=z.object({status:z.enum(['PENDING','AUTHORIZED','COMPLETED','REJECTED','EXPIRED']),requestId:z.uuid(),ceremonyId:z.uuid(),authenticated:z.boolean().optional(),signInRequired:z.boolean().optional()});
function restoreAttempt(value:unknown,csrfToken:string):Attempt|undefined{const recovery=parseAuthenticationPasskeyRecovery(value);if(recovery?.kind!=='SIGN_IN')return undefined;if(typeof recovery.rememberMe!=='boolean')throw new Error('Original sign-in context is required.');return {requestId:recovery.requestId,rememberMe:recovery.rememberMe,csrfToken,operation:recovery};}
export function PasskeySignIn(props:PasskeySignInProps){
  const latest=useRef(props);latest.current=props;const mounted=useRef(true);const controller=useRef(new AbortController());const inFlight=useRef(false);const original=useRef<Attempt|undefined>(restoreAttempt(props.recovery,props.csrfToken));
  const [phase,setPhase]=useState<'ready'|'pending'|'unknown'|'not-completed'>(original.current?'unknown':'ready');const [working,setWorking]=useState(false);const [error,setError]=useState<string>();const [notice,setNotice]=useState<string|undefined>(original.current?'login.passkey.unknown':undefined);
  const actions=useRef<HTMLDivElement>(null);const restoreActionFocus=useRef(false);
  useLayoutEffect(()=>{
    if(!restoreActionFocus.current||working||!mounted.current||controller.current.signal.aborted||phase!=='unknown'&&phase!=='not-completed')return;
    restoreActionFocus.current=false;
    actions.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
  },[phase,working,props.available,props.disabled]);
  useLayoutEffect(()=>{if(original.current)latest.current.onBusyChange(true);},[]);
  useEffect(()=>()=>{restoreActionFocus.current=false;mounted.current=false;controller.current.abort();latest.current.onBusyChange(false);},[]);
  useEffect(()=>{
    if(!props.recovery)return;const parsed=(()=>{try{return parseOperation(props.recovery);}catch{return undefined;}})();
    if(!parsed||parsed.kind!=='REVOKE'||parsed.status!=='COMPLETED'||!parsed.resultHandle)return;
    const signal=controller.current.signal;
    void requestJson('/account/passkeys/terminal-result',{csrfToken:props.csrfToken,body:{requestId:parsed.requestId,ceremonyId:parsed.ceremonyId,resultHandle:parsed.resultHandle},signal}).then(value=>{
      const result=terminalCompleted.parse(value);if(result.requestId!==parsed.requestId||result.ceremonyId!==parsed.ceremonyId)throw new Error('The original result changed.');if(mounted.current&&!signal.aborted)setNotice('login.passkey.removed');
    }).catch(()=>{if(mounted.current&&!signal.aborted)setNotice('login.passkey.sessionEnded');});
  },[props.recovery,props.csrfToken]);
  const current=(attempt:Attempt)=>mounted.current&&!controller.current.signal.aborted&&original.current===attempt;
  function pending(value:boolean){inFlight.current=value;setWorking(value);}
  function unresolved(){setPhase('unknown');setNotice('login.passkey.unknown');latest.current.onBusyChange(true);}
  function completed(attempt:Attempt,value:unknown){if(parseMfaRequired(value,attempt.requestId)){if(!latest.current.onMfaRequired)throw new Error('Additional verification is required.');if(current(attempt)){attempt.credential=undefined;original.current=undefined;latest.current.onBusyChange(false);latest.current.onMfaRequired();}return;}const result=assertionCompleted.parse(value);if(!attempt.operation||result.requestId!==attempt.requestId||result.ceremonyId!==attempt.operation.ceremonyId)throw new Error('The original sign-in result changed.');if(current(attempt)){original.current=undefined;latest.current.onBusyChange(false);latest.current.onAuthenticated();}}
  async function freshCsrf(signal:AbortSignal){
    try{const value=await requestJson('/authentication/session',{signal}) as {authenticated?:unknown;csrfToken?:unknown};if(value.authenticated===true&&typeof value.csrfToken==='string'&&value.csrfToken)return value.csrfToken;}catch{/* A surviving session is never success evidence. */}
    const value=await requestJson('/authentication/options',{signal}) as {csrfToken?:unknown};if(typeof value.csrfToken!=='string'||!value.csrfToken)throw new Error('Fresh CSRF unavailable.');return value.csrfToken;
  }
  async function recoverChallenge(attempt:Attempt){
    const anonymous=await requestJson('/authentication/options',{signal:controller.current.signal}) as {csrfToken?:unknown};if(typeof anonymous.csrfToken!=='string'||!anonymous.csrfToken)throw new Error('Anonymous CSRF unavailable.');if(!current(attempt))return;
    attempt.csrfToken=anonymous.csrfToken;const recovered=parseOperation(await requestJson('/authentication/passkeys/options',{csrfToken:anonymous.csrfToken,body:{requestId:attempt.requestId,rememberMe:attempt.rememberMe},signal:controller.current.signal}),attempt.operation);
    if(recovered.requestId!==attempt.requestId||recovered.kind!=='SIGN_IN')throw new Error('The original challenge changed.');attempt.operation=recovered;
  }
  async function result(attempt:Attempt){
    if(!attempt.operation)await recoverChallenge(attempt);if(!attempt.operation||!current(attempt))return false;
    const csrfToken=await freshCsrf(controller.current.signal);if(!current(attempt))return false;
    const raw=await requestJson('/authentication/passkeys/result',{csrfToken,body:{requestId:attempt.requestId,ceremonyId:attempt.operation.ceremonyId},signal:controller.current.signal});
    if(parseMfaRequired(raw,attempt.requestId)){completed(attempt,raw);return true;}
    const value=readOnlyState.parse(raw);
    if(value.requestId!==attempt.requestId||value.ceremonyId!==attempt.operation.ceremonyId)throw new Error('The original result changed.');
    if(value.status==='COMPLETED'){if(value.authenticated===true){completed(attempt,value);return true;}if(value.authenticated===false&&value.signInRequired===true){if(current(attempt)){original.current=undefined;setPhase('not-completed');setNotice('login.passkey.notCompleted');latest.current.onBusyChange(false);}return true;}throw new Error('The original authentication result is unresolved.');}
    if(['REJECTED','EXPIRED'].includes(value.status)&&current(attempt)){original.current=undefined;setPhase('not-completed');setNotice('login.passkey.notCompleted');latest.current.onBusyChange(false);return true;}
    return false;
  }
  async function submit(attempt:Attempt,refreshAnonymous=false){if(!attempt.operation||!attempt.credential)return;if(refreshAnonymous){const options=await requestJson('/authentication/options',{signal:controller.current.signal}) as {csrfToken?:unknown};if(typeof options.csrfToken!=='string'||!options.csrfToken)throw new Error('Anonymous CSRF unavailable.');attempt.csrfToken=options.csrfToken;}if(!current(attempt))return;completed(attempt,await requestJson('/authentication/passkeys/sign-in',{csrfToken:attempt.csrfToken,body:{requestId:attempt.requestId,ceremonyId:attempt.operation.ceremonyId,credential:attempt.credential},signal:controller.current.signal}));}
  async function start(){
    if(inFlight.current||original.current||props.disabled||!props.available||!passkeysSupported())return;
    restoreActionFocus.current=true;
    controller.current.abort();controller.current=new AbortController();const attempt:Attempt={requestId:crypto.randomUUID(),rememberMe:props.rememberMe,csrfToken:props.csrfToken};original.current=attempt;pending(true);latest.current.onBusyChange(true);setPhase('pending');setNotice(undefined);setError(undefined);
    try{const value=parseOperation(await requestJson('/authentication/passkeys/options',{csrfToken:props.csrfToken,body:{requestId:attempt.requestId,rememberMe:attempt.rememberMe},signal:controller.current.signal}));if(value.requestId!==attempt.requestId||value.kind!=='SIGN_IN'||value.status!=='PENDING'||!value.publicKey)throw new Error('The sign-in challenge changed.');attempt.operation=value;if(!current(attempt))return;
      try{attempt.credential=await getPasskey(value.publicKey,props.maximumCredentialJsonBytes,controller.current.signal);}catch(reason){if(!current(attempt))return;if(reason instanceof DOMException&&['NotAllowedError','AbortError'].includes(reason.name)){original.current=undefined;setPhase('not-completed');setNotice('login.passkey.notCompleted');latest.current.onBusyChange(false);return;}throw reason;}
      if(!current(attempt))return;await submit(attempt);
    }catch(reason){if(!current(attempt))return;if(reason instanceof PasskeyHttpError&&reason.status===429&&!attempt.credential){original.current=undefined;setPhase('not-completed');setError('login.passkey.limited');latest.current.onBusyChange(false);}else if(reason instanceof PasskeyHttpError&&[400,403,409,413,415].includes(reason.status)&&!attempt.credential){original.current=undefined;setPhase('not-completed');setError('login.passkey.changed');latest.current.onBusyChange(false);}else{try{if(await result(attempt))return;}catch{/* Exact result did not prove completion. */}if(current(attempt))unresolved();}}
    finally{if(mounted.current)pending(false);}
  }
  async function check(){const attempt=original.current;if(!attempt||inFlight.current)return;restoreActionFocus.current=true;pending(true);setError(undefined);try{if(!await result(attempt)&&current(attempt))unresolved();}catch{if(current(attempt))unresolved();}finally{if(mounted.current)pending(false);}}
  async function resume(){const attempt=original.current;if(!attempt||inFlight.current)return;restoreActionFocus.current=true;pending(true);setError(undefined);try{try{if(await result(attempt))return;}catch{/* Explicit continuation retains the original IDs and payload; backend rechecks its original browser context. */}if(!current(attempt)||!attempt.operation)return;if(attempt.credential===undefined){if(attempt.operation.status==='PENDING'&&!attempt.operation.publicKey)await recoverChallenge(attempt);if(!current(attempt)||!attempt.operation||attempt.operation.status!=='PENDING'||!attempt.operation.publicKey){unresolved();return;}try{attempt.credential=await getPasskey(attempt.operation.publicKey,latest.current.maximumCredentialJsonBytes,controller.current.signal);}catch(reason){if(reason instanceof DOMException&&['NotAllowedError','AbortError'].includes(reason.name)){original.current=undefined;setPhase('not-completed');setNotice('login.passkey.notCompleted');latest.current.onBusyChange(false);return;}throw reason;}}if(current(attempt))await submit(attempt,true);}catch{if(current(attempt))unresolved();}finally{if(mounted.current)pending(false);}}
  if(!props.available&&!props.recovery)return null;
  return <div ref={actions} className="authentication-passkey" data-tunner-component="COMP-PASSKEY-SIGN-IN">
    {phase==='unknown'?<><PrimaryButton labelKey="login.passkey.check" appearance="outline" disabled={working} onClick={()=>void check()}/><PrimaryButton labelKey="login.passkey.resume" appearance="outline" disabled={working||!props.available||original.current?.credential===undefined&&!passkeysSupported()} onClick={()=>void resume()}/></>:props.available&&<PrimaryButton labelKey="login.passkey.submit" appearance="outline" disabled={props.disabled||working||!passkeysSupported()} aria-busy={working} onClick={()=>void start()}/>}
    {props.available&&!passkeysSupported()&&<p role="status"><ContentText contentKey="login.passkey.unsupported"/></p>}{phase==='pending'&&<p role="status"><ContentText contentKey="login.passkey.pending"/></p>}{notice&&<p role="status"><ContentText contentKey={notice}/></p>}{error&&<FormErrorSummary contentKey={error}/>}
  </div>;
}
