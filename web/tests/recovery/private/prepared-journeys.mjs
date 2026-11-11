import {createRecoveryAccountMechanics} from '../../totp/sensitive/real-journey.mjs';
import {budgets, fixedError, requireTrue, observedMap, journeys, registeredJourney} from './contract.mjs';

// Imported only by the protected child. No process, artifact, reporter or file API.
// The committed dispatcher remains closed; root must qualify and separately open it.
const security='/account/security',prefix='/account/recovery-codes';
const object=v=>v!==null&&typeof v==='object'&&!Array.isArray(v);
const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const bindingKeys=['ownerRequestId','operationReference','setReference','ownerContextDigest','deliveryProfileVersion','formatProfileVersion','codesPerSet','maximumCodesPerSet','expectedVersion','kind'];
const sameOperation=(a,b)=>object(a)&&object(b)&&bindingKeys.every(k=>a[k]===b[k])&&same(a.impact.publicationValidity,b.impact.publicationValidity)&&['impactKey','factsDigest','publicationVersion','publicationDigest'].every(k=>a.impact[k]===b.impact[k]);
const noDelivery=value=>object(value)&&!Object.hasOwn(value,'recoveryCodeFirstDelivery')&&!Object.hasOwn(value,'codes');
const sameCounts=(a,b)=>Object.keys(a).length===Object.keys(b).length&&Object.keys(a).every(k=>a[k]===b[k]);
const privateFill=async(locator,value)=>{try{await locator.fill(value);}catch{throw fixedError('ACTION_FAILED');}};

class RecoveryJourney {
 constructor(input){
  Object.assign(this,input);this.observations=observedMap(journeys[input.control.journeyId]);this.values=new Set();this.controlCalls=0;
  this.method=input.control.journeyId.includes('_PASSWORD_')?'password':input.control.journeyId.includes('_PASSKEY_')?'passkey':'totp';
  this.kind=input.control.journeyId.startsWith('RECOVERY_REPLACE_')?'REPLACE':'GENERATE';
  this.mechanics=createRecoveryAccountMechanics({...input,origin:input.control.origin,mailpitOrigin:input.control.mailpitOrigin,journeyId:input.control.journeyId,runId:input.control.runId,remember:(...values)=>this.remember(...values),fixtureControl:action=>this.fixture(action)});
 }
 remember(...values){for(const value of values)if(typeof value==='string'&&value.length){this.values.add(value);this.markers.remember(value);}requireTrue(this.values.size<=256);}
 observe(id,value){this.observations.observe(id,value===true);}
 prove(id,value){this.observe(id,value);requireTrue(value===true);}
 async fixture(action){requireTrue(++this.controlCalls<=16,'PROTOCOL_FAILED');const value=await this.fixtureControl(action);requireTrue(value.status==='OK','PROTOCOL_FAILED');return value;}
 async counts(){return(await this.fixture('READ_SAFE_COUNTS')).counts;}
 async options(page=this.page){const r=await this.mechanics.get(page,prefix+'/options');requireTrue(r.ok&&noDelivery(r.body));return r.body;}
 async result(operation,page=this.page,retainedBoot){const boot=retainedBoot??await this.options(page),r=await this.mechanics.post(page,prefix+'/result',{ownerRequestId:operation.ownerRequestId,operationReference:operation.operationReference},boot.csrfToken);requireTrue(r.ok&&noDelivery(r.body)&&sameOperation(operation,r.body));return r.body;}
 async setup(){
  this.account=await this.mechanics.bootstrap();this.prove('BFF_IDENTITY',true);
  if(this.method==='passkey')await this.mechanics.setupPasskey(this.page,this.account.password);
  if(this.method==='totp')this.factor=await this.mechanics.enrollTotp(this.page,this.account.password);
  await this.page.goto(new URL(security,this.control.origin).href);await this.mechanics.at(this.page,security);
  await this.page.locator('[data-tunner-component="U-AUTH-06"]').first().waitFor();
 }
 async begin(kind=this.kind,page=this.page){
  if(new URL(page.url()).pathname!==security)await page.goto(new URL(security,this.control.origin).href);
  await this.mechanics.at(page,security);const boot=await this.options(page);
  requireTrue(boot[kind==='GENERATE'?'canGenerate':'canReplace']===true);
  const start=await this.mechanics.response(page,prefix+'/'+kind.toLowerCase()+'/start',()=>page.getByRole('button',{name:kind==='GENERATE'?'Generate codes':'Replace codes',exact:true}).click());
  const op=start.body;
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',start.ok&&op.status==='PENDING_PROOF'&&op.ownerRequestId===start.sent.ownerRequestId&&op.expectedVersion===start.sent.expectedVersion&&op.kind===kind&&op.codesPerSet===boot.codesPerSet&&op.maximumCodesPerSet===boot.maximumCodesPerSet&&op.impact.canContinue===false);
  if(kind==='REPLACE'){
   const dialog=page.getByRole('dialog'),fresh=await this.options(page),current=fresh.recovery;
   requireTrue(sameOperation(op,current)&&current.impact.canContinue===true);
   const published=fresh.content[current.impact.impactKey];
   const displayed=object(published)&&published.state==='PUBLISHED'&&published.locale===fresh.locale&&typeof published.text==='string'&&published.text.length>0&&await dialog.getByText(published.text,{exact:true}).isVisible();
   this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',displayed);if(journeys[this.control.journeyId].includes('PUBLISHED_IMPACT_ORIGINAL_BINDING'))this.prove('PUBLISHED_IMPACT_ORIGINAL_BINDING',displayed);
   await dialog.getByRole('button',{name:'Continue',exact:true}).click();
  }
  await page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true}).waitFor();
  const proof=await this.mechanics.get(page,'/authentication/step-up/options');
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',proof.ok&&noDelivery(proof.body)&&proof.body.operationReference===op.operationReference&&proof.body.status==='PENDING');
  this.prove('NO_EARLY_DELIVERY',await page.locator('.recovery-codes-grid code').count()===0&&noDelivery(boot)&&noDelivery(start.body));
  return{op,proof:proof.body,page,boot};
 }
 async proof(held,{denial=false,bodyLoss=false,publicationFault=false,hold=false}={}){
  const {page,op}=held,dialog=page.getByRole('dialog'),stats=this.mechanics.passkeyStats();
  const route='/authentication/step-up/'+this.method;
  if(this.method==='password'){await dialog.getByRole('button',{name:'Other method',exact:true}).click();await privateFill(dialog.getByLabel('Password',{exact:true}),this.account.password);}
  if(this.method==='totp'){await dialog.getByRole('button',{name:'Authenticator Enter a 6-digit code',exact:true}).click();await privateFill(dialog.getByLabel('Verification code',{exact:true}),await this.mechanics.nextTotpCode(this.factor));}
  if(denial)await this.fixture('DENY_OWNER_ONCE');
  if(bodyLoss)await this.fixture('DROP_FIRST_DELIVERY_ONCE');
  if(publicationFault)await this.fixture('WITHDRAW_PUBLICATION_AFTER_APPLY');
  const submit=()=>dialog.getByRole('button',{name:this.method==='passkey'?'Passkey':'Confirm',exact:true}).click();
  if(bodyLoss){
   const request=this.mechanics.request(page,route);await submit();const sent=await request;requireTrue(object(sent)&&sent.requestId===held.proof.requestId);
   await page.getByRole('heading',{name:'These codes are unavailable',exact:true}).waitFor();
   const safe=await this.result(op,page);
   this.prove('BODY_LOSS_NO_REDISCLOSURE',safe.status==='COMPLETED'&&safe.codesUnavailable===true&&await page.locator('.recovery-codes-grid code').count()===0&&await page.getByRole('button',{name:'Download',exact:true}).count()===0&&!await page.getByRole('dialog').getByText('These codes were already shown',{exact:true}).count());
   this.prove('ACTUAL_PROOF_METHOD',this.method==='password');return{safe,sent,bodyLost:true};
  }
  const response=await this.mechanics.response(page,route,submit);
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',response.sent.requestId===held.proof.requestId);
  this.prove('ACTUAL_PROOF_METHOD',this.method==='password'?typeof response.sent.password==='string'&&response.sent.password===this.account.password:this.method==='passkey'?object(response.sent.credential)&&this.mechanics.passkeyStats().asserted===stats.asserted+1:response.sent.authenticatorReference===this.factor.reference&&/^\d{6}$/.test(response.sent.code));
  if(this.method==='passkey')this.prove('NATIVE_PASSKEY_PROOF',this.mechanics.passkeyStats().created===1&&this.mechanics.passkeyStats().asserted>stats.asserted);
  if(this.method==='totp')this.prove('NATIVE_TOTP_PROOF',response.sent.authenticatorReference===this.factor.reference&&/^\d{6}$/.test(response.sent.code));
  if(publicationFault){
   requireTrue(!response.ok&&noDelivery(response.body));const safe=await this.result(op,page,held.boot),context=await this.mechanics.get(page,'/authentication/step-up/options');
   this.prove('CURRENT_PUBLICATION',safe.status==='PENDING_PROOF'&&context.ok&&context.body.requestId===held.proof.requestId&&context.body.totpRecovery?.status==='PROOF_ACTIVE'&&await page.locator('.recovery-codes-grid code').count()===0);return{rejected:true,safe};
  }
  if(denial){
   requireTrue(response.status===503&&noDelivery(response.body));
   const current=await this.mechanics.get(page,'/authentication/step-up/options'),native=this.method==='passkey'?current.body.passkeyRecovery:current.body.totpRecovery;
   requireTrue(current.ok&&native?.requestId===held.proof.requestId&&native.attemptId===response.sent.attemptId&&native.operationReference===op.operationReference&&native.status===(this.method==='passkey'?'PROOF_ISSUED':'PROOF_ACTIVE'));
   if(hold)return{held,response,native};
   const before=await this.counts(),assertions=this.mechanics.passkeyStats().asserted;
   const resumed=await this.mechanics.response(page,route+'/continue',()=>dialog.getByRole('button',{name:'Continue this verification',exact:true}).click());
   const after=await this.counts();
   this.prove('OWNER_FAILURE_SAME_PROOF',resumed.ok&&resumed.sent.requestId===response.sent.requestId&&resumed.sent.attemptId===response.sent.attemptId&&!Object.hasOwn(resumed.sent,'code')&&!Object.hasOwn(resumed.sent,'credential')&&after.proofs===before.proofs&&after.codeAcceptances===before.codeAcceptances&&after.ownerCommits===before.ownerCommits+1&&after.consumedProofs===before.consumedProofs+1&&this.mechanics.passkeyStats().asserted===assertions);
   return this.capture(held,resumed);
  }
  return this.capture(held,response);
 }
 async capture(held,response){
  const {op,proof,page}=held,d=response.body.recoveryCodeFirstDelivery;
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',response.ok&&response.body.status==='COMPLETED'&&response.body.signInRequired===false&&response.body.operationReference===op.operationReference&&object(d)&&d.ownerRequestId===op.ownerRequestId&&d.stepUpRequestId===proof.requestId&&['operationReference','setReference','ownerContextDigest','deliveryProfileVersion','formatProfileVersion'].every(k=>d[k]===op[k]));
  const codes=d.codes;requireTrue(Array.isArray(codes));this.remember(...codes);
  this.prove('CANONICAL_FIRST_DELIVERY',codes.length===op.codesPerSet&&codes.length<=op.maximumCodesPerSet&&codes.every(c=>typeof c==='string'&&/^[0-9A-F]{32}$/.test(c))&&new Set(codes).size===codes.length);
  await page.locator('.recovery-codes-grid code').first().waitFor();
  this.prove('CANONICAL_FIRST_DELIVERY',same(await page.locator('.recovery-codes-grid code').allTextContents(),codes));
  return{codes,held,response};
 }
 async duplicate(held,complete){
  const before=await this.counts(),boot=await this.options(held.page),safe=await this.result(held.op,held.page);
  const duplicate=await this.mechanics.post(held.page,prefix+'/start-result',{ownerRequestId:held.op.ownerRequestId,expectedVersion:held.op.expectedVersion,kind:held.op.kind},boot.csrfToken),after=await this.counts();
  this.prove('DUPLICATE_NO_REDISCLOSURE',safe.status==='COMPLETED'&&safe.codesUnavailable===true&&duplicate.ok&&noDelivery(duplicate.body)&&sameOperation(held.op,duplicate.body)&&duplicate.body.status==='COMPLETED'&&sameCounts(before,after)&&noDelivery(boot)&&(!complete.codes||same(await held.page.locator('.recovery-codes-grid code').allTextContents(),complete.codes)));
 }
 async privacy(){
  const state=await this.page.evaluate(()=>({url:location.href,referrer:document.referrer,name:window.name,history:JSON.stringify(history.state),local:Object.entries(localStorage),session:Object.entries(sessionStorage)}));
  const cookies=await this.context.cookies(),protectedCookies=cookies.filter(c=>c.name.startsWith('__Host-'));
  this.prove('COOKIE_FLAGS',protectedCookies.length>0&&protectedCookies.every(c=>c.httpOnly&&c.secure&&c.path==='/'&&['Strict','Lax'].includes(c.sameSite)));
  const carrier=JSON.stringify({...state,cookies});this.prove('NO_SECRET_URL_OR_STORAGE',state.local.length===0&&state.session.length===0&&[...this.values].every(v=>!carrier.includes(v))&&!carrier.includes('otpauth://'));
 }
 async clear({pagehide=false}={}){
  if(pagehide){
   // Real full-page departure. BFCache persistence is observed, never assumed.
   await this.page.goto(new URL('/signed-in',this.control.origin).href);await this.mechanics.at(this.page,'/signed-in');await this.page.goBack();await this.mechanics.at(this.page,security);
  }else await this.page.getByRole('button',{name:'Done',exact:true}).click();
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0);await this.page.reload();await this.mechanics.at(this.page,security);
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0);this.prove('VOLATILE_CLEAR',true);
  if(pagehide)this.prove('PAGEHIDE_NO_REVEAL',true);
  const text=await this.page.locator('body').textContent();this.prove('REQUESTED_NOT_SAVED',!text.includes('file was saved')&&!text.includes('Download requested'));
 }
 async execute(){
  await this.setup();
  if(this.kind==='REPLACE'){
   const initial=this.control.journeyId==='RECOVERY_REPLACE_PASSWORD_V1'?3:1;
   for(let i=0;i<initial;i++){const h=await this.begin('GENERATE'),r=await this.proof(h);await this.duplicate(h,r);await this.clear();}
  }
  const before=await this.counts(),held=await this.begin();
  const completed=await this.proof(held,{denial:this.kind==='GENERATE'&&this.method!=='password',bodyLoss:this.control.journeyId==='RECOVERY_REPLACE_PASSWORD_V1'});
  await this.duplicate(held,completed);const after=await this.counts();
  requireTrue(after.ownerCommits===before.ownerCommits+1&&after.sets===before.sets+1&&after.entries===before.entries+held.op.codesPerSet);
  if(this.control.journeyId==='RECOVERY_GENERATE_PASSWORD_V1'){
   const expected=Buffer.from(completed.codes.join('\n')+'\n');
   try{await this.requestOwnedDownload(this.page,expected);}finally{expected.fill(0);}
   const status=this.page.getByRole('status');this.prove('REQUESTED_NOT_SAVED',(await status.textContent()).includes('Download requested')&&(await status.textContent()).includes('cannot confirm that the file was saved'));
   this.prove('ONE_REQUESTED_DOWNLOAD',await this.page.getByRole('button',{name:'Download',exact:true}).isDisabled());
   await this.clear();this.prove('CLOSE_NO_REVEAL',true);
  }else if(completed.bodyLost){await this.page.getByRole('button',{name:'Close',exact:true}).click();await this.page.reload();this.prove('VOLATILE_CLEAR',await this.page.locator('.recovery-codes-grid code').count()===0);this.prove('REQUESTED_NOT_SAVED',await this.page.getByRole('status').count()===0);}
  else await this.clear({pagehide:this.control.journeyId==='RECOVERY_GENERATE_TOTP_V1'});
  if(this.kind==='REPLACE'){const unchanged=after.sessions===before.sessions&&(await this.mechanics.session(this.page)).ok;requireTrue(unchanged);if(journeys[this.control.journeyId].includes('NO_SESSION_MUTATION'))this.prove('NO_SESSION_MUTATION',unchanged);}
  if(this.control.journeyId==='RECOVERY_REPLACE_TOTP_V1'){
   const current=await this.counts(),pending=await this.begin(),rejected=await this.proof(pending,{publicationFault:true}),end=await this.counts();
   this.prove('CURRENT_PUBLICATION',rejected.rejected&&end.ownerCommits===current.ownerCommits&&end.sets===current.sets&&end.replacedSets===current.replacedSets&&end.entries===current.entries&&end.consumedProofs===current.consumedProofs&&await this.page.locator('.recovery-codes-grid code').count()===0);
  }
  if(this.control.journeyId==='RECOVERY_REPLACE_PASSKEY_V1'){
   const pending=await this.begin(),retained=await this.proof(pending,{denial:true,hold:true});
   const other=await this.mechanics.newPage();await this.mechanics.passwordLogin(other,this.account,false);
   await this.mechanics.revokePasskey(other,this.account.password);
   const current=await this.counts(),rejected=await this.mechanics.response(this.page,'/authentication/step-up/passkey/continue',()=>this.page.getByRole('dialog').getByRole('button',{name:'Continue this verification',exact:true}).click()),end=await this.counts();
   this.prove('SOURCE_FRESHNESS',!rejected.ok&&['STEP_UP_REQUIRED','VERSION_CONFLICT'].includes(rejected.body.error_code)&&rejected.sent.requestId===retained.response.sent.requestId&&rejected.sent.attemptId===retained.response.sent.attemptId&&end.ownerCommits===current.ownerCommits&&end.sets===current.sets&&end.replacedSets===current.replacedSets&&end.entries===current.entries&&end.consumedProofs===current.consumedProofs&&noDelivery(rejected.body)&&await this.page.locator('.recovery-codes-grid code').count()===0);
  }
  await this.privacy();this.prove('STRICT_OWNED_SURFACE',await this.inspectOwnedSurface());
 }
 async close(){try{await this.mechanics.close();}finally{this.values.clear();}}
}

export async function runPreparedJourney(input){
 requireTrue(registeredJourney(input.control?.journeyId)&&typeof input.fixtureControl==='function'&&typeof input.nativeCode==='function'&&typeof input.requestOwnedDownload==='function'&&typeof input.inspectOwnedSurface==='function','INPUT_INVALID');
 const run=new RecoveryJourney(input);let failed=false;
 try{await run.execute();}catch{failed=true;}
 try{run.observations.finish(input.check);}catch{failed=true;}
 try{await run.close();}catch{throw fixedError('CLEANUP_FAILED');}
 if(failed)throw fixedError('PREDICATE_FAILED');
}
