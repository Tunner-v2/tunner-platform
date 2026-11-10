import {test} from 'node:test';
import assert from 'node:assert/strict';
import {validateTunnerForm} from '../packages/ui/src/index';
import {canonicalTotpCode,totpCodeSchema} from '../apps/user/totp.validation';
import {matchesTotpStart,parseMfaRequired,parseTotpDisclosure,parseTotpMfaCompleted,parseTotpOperation,parseTotpTerminal,TotpHttpError} from '../apps/user/totp.api';

const requestId='11111111-1111-4111-8111-111111111111',enrollmentId='22222222-2222-4222-8222-222222222222',reference='33333333-3333-4333-8333-333333333333',other='44444444-4444-4444-8444-444444444444';
const operation={requestId,enrollmentId,kind:'ENROLL' as const,authenticatorReference:reference,expectedVersion:0,status:'AWAITING_FIRST_CODE' as const,expiresAt:'2026-11-10T20:00:00Z',disclosureAvailable:false,disclosureConsumed:true,activationCompleted:false,factorSessionEffect:null,currentSessionEnds:false};

test('TOTP code grammar preserves leading zeroes and mirrors the server ASCII separators and raw bound',()=>{
  for(const value of ['000123','000 123','00-01-23',' 000123-'])assert.equal(canonicalTotpCode(value),'000123');
  for(const value of ['12345','1234567','１２３４５６','١٢٣٤٥٦','123\t456','123\n456','123_456','123.456','123\u00a0456',' '.repeat(27)+'000123'])assert.equal(canonicalTotpCode(value),null);
  const result=validateTunnerForm(totpCodeSchema(),{code:'00-01-23'});assert.equal(result.valid,true);if(result.valid)assert.equal(result.data.code,'000123');
  const required=validateTunnerForm(totpCodeSchema(),{code:''});assert.equal(required.errors.code,'totp.error.required');
  const proof=validateTunnerForm(totpCodeSchema({required:'stepUp.totp.error.required',invalid:'stepUp.totp.error.code'}),{code:'wrong'});assert.equal(proof.errors.code,'stepUp.totp.error.code');
});
test('TOTP reconciliation rejects another operation, selected source, kind or expected version',()=>{
  assert.equal(parseTotpOperation(operation,operation).enrollmentId,enrollmentId);
  for(const changed of [{requestId:other},{enrollmentId:other},{authenticatorReference:other},{kind:'RESET'},{expectedVersion:2}])assert.throws(()=>parseTotpOperation({...operation,...changed},operation),error=>error instanceof TotpHttpError&&error.code==='VERSION_CONFLICT');
  assert.equal(matchesTotpStart({requestId,kind:'ENROLL',authenticatorReference:null,expectedVersion:0},operation),true);
  const disable={...operation,kind:'DISABLE' as const,expectedVersion:4};assert.equal(matchesTotpStart({requestId,kind:'DISABLE',authenticatorReference:other,expectedVersion:4},disable),false);
});
test('one-time disclosure requires exact original IDs and matching local otpauth/manual-key material without raw diagnostics',()=>{
  const marker='PUBLIC_NONFUNCTIONAL_SENTINEL';const value={status:'AWAITING_FIRST_CODE',requestId,enrollmentId,expiresAt:operation.expiresAt,manualKey:marker,otpAuthUri:'otpauth://totp/Public?secret='+marker};
  assert.equal(parseTotpDisclosure(value,operation).manualKey,marker);
  for(const changed of [{requestId:other},{enrollmentId:other},{otpAuthUri:'https://example.invalid/?secret='+marker},{otpAuthUri:'otpauth://totp/Public?secret=OTHER'},{otpAuthUri:'otpauth://totp/Public?secret='+marker+'&secret='+marker},{manualKey:{secret:marker}}]){
    let caught:unknown;try{parseTotpDisclosure({...value,...changed},operation);}catch(error){caught=error;}assert.ok(caught instanceof TotpHttpError);assert.equal(String(caught).includes(marker),false);assert.equal(JSON.stringify(caught).includes(marker),false);
  }
});
test('MFA-required is not final authentication and final recovery requires the exact request receipt',()=>{
  const pending={status:'MFA_REQUIRED',authenticated:false,requestId,expiresAt:operation.expiresAt};assert.equal(parseMfaRequired(pending,requestId)?.authenticated,false);
  assert.equal(parseMfaRequired({...pending,authenticated:true}),null);assert.throws(()=>parseMfaRequired({...pending,requestId:other},requestId),TotpHttpError);
  assert.throws(()=>parseTotpMfaCompleted({status:'COMPLETED'},requestId),TotpHttpError);
  assert.throws(()=>parseTotpMfaCompleted({status:'COMPLETED',authenticated:true,requestId:other},requestId),TotpHttpError);
  assert.equal(parseTotpMfaCompleted({status:'COMPLETED',authenticated:true,requestId},requestId).requestId,requestId);
});
test('terminal result cannot turn a generic session loss or another factor action into confirmed removal',()=>{
  const removal={...operation,kind:'DISABLE' as const,status:'COMPLETED' as const,expectedVersion:3,factorSessionEffect:'RevokeFactorSessions' as const,currentSessionEnds:true};
  assert.equal(parseTotpTerminal({operation:removal,signInRequired:true},removal).signInRequired,true);
  for(const changed of [{currentSessionEnds:false},{kind:'ENROLL'},{status:'AWAITING_FIRST_CODE'},{authenticatorReference:other}])assert.throws(()=>parseTotpTerminal({operation:{...removal,...changed},signInRequired:true},removal),TotpHttpError);
});
