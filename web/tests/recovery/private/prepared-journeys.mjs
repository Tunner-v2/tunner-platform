import {createRecoveryAccountMechanics} from '../../totp/sensitive/real-journey.mjs';
import {budgets, fixedError, requireTrue, bounded, observedMap, journeys, registeredJourney, totpEnrollmentStages} from './contract.mjs';

// Imported only by the protected child. No process, artifact, reporter or file API.
// Fixed delegation still requires coordinator-owned current qualifications before
// private resources. Source presence does not prove a journey has executed.
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
  Object.assign(this,input);this.observations=observedMap(journeys[input.control.journeyId]);this.values=new Set();this.controlCalls=0;this.pages=[input.page];this.headerFacts=[];
  this.method=input.control.journeyId.includes('_PASSWORD_')?'password':input.control.journeyId.includes('_PASSKEY_')?'passkey':'totp';
  this.kind=input.control.journeyId.startsWith('RECOVERY_REPLACE_')?'REPLACE':'GENERATE';
  this.mechanics=createRecoveryAccountMechanics({...input,reportStep:this.method==='totp'?stage=>{if(totpEnrollmentStages.includes(stage)||stage==='JOURNEY')this.checkpoint?.(stage);}:()=>{},origin:input.control.origin,mailpitOrigin:input.control.mailpitOrigin,journeyId:input.control.journeyId,runId:input.control.runId,remember:(...values)=>this.remember(...values),fixtureControl:action=>this.fixture(action)});
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
  if(this.method==='passkey')await this.mechanics.setupPasskey(this.page,this.account.password,this.checkpoint);
  if(this.method==='totp')this.factor=await this.mechanics.enrollTotp(this.page,this.account.password);
  await this.page.goto(new URL(security,this.control.origin).href);await this.mechanics.at(this.page,security);
  await this.page.locator('[data-tunner-component="U-AUTH-06"]').first().waitFor();
  if(this.method==='passkey'){this.checkpoint?.('RECOVERY_BEGIN');await this.acknowledgePreparedPasskey();}
 }
 async acknowledgePreparedPasskey(){
  // A full navigation restores the prepared enrollment's safe result and its ordinary parent guard.
  const options=await this.mechanics.get(this.page,'/account/passkeys/options');requireTrue(options.ok);
  const enrollment=options.body.recovery;if(enrollment===null)return;
  requireTrue(object(enrollment)&&enrollment.kind==='ENROLL'&&enrollment.status==='COMPLETED'&&typeof enrollment.requestId==='string'&&typeof enrollment.ceremonyId==='string');
  const original=this.preparedPasskeyEnrollment??{requestId:enrollment.requestId,ceremonyId:enrollment.ceremonyId};
  requireTrue(enrollment.requestId===original.requestId&&enrollment.ceremonyId===original.ceremonyId);this.preparedPasskeyEnrollment=original;
  const check=this.page.locator('.passkeys-main > .passkeys-page-actions').getByRole('button',{name:'Check status',exact:true});
  const completed=await this.mechanics.response(this.page,'/account/passkeys/result',()=>check.click());
  requireTrue(completed.ok&&completed.sent.requestId===original.requestId&&completed.sent.ceremonyId===original.ceremonyId&&completed.body.requestId===original.requestId&&completed.body.ceremonyId===original.ceremonyId&&completed.body.kind==='ENROLL'&&completed.body.status==='COMPLETED');
  await check.waitFor({state:'detached'});
 }
 async begin(kind=this.kind,page=this.page){
  this.checkpoint?.('RECOVERY_BEGIN');
  if(new URL(page.url()).pathname!==security)await page.goto(new URL(security,this.control.origin).href);
  await this.mechanics.at(page,security);const boot=await this.options(page);
  requireTrue(boot[kind==='GENERATE'?'canGenerate':'canReplace']===true);
  const start=await this.response(page,prefix+'/'+kind.toLowerCase()+'/start',()=>page.getByRole('button',{name:kind==='GENERATE'?'Generate codes':'Replace codes',exact:true}).click());
  const op=start.body;
  this.checkpoint?.('RECOVERY_START_BINDING');
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',start.ok&&op.status==='PENDING_PROOF'&&op.ownerRequestId===start.sent.ownerRequestId&&op.expectedVersion===start.sent.expectedVersion&&op.kind===kind&&op.codesPerSet===boot.codesPerSet&&op.maximumCodesPerSet===boot.maximumCodesPerSet&&op.impact.canContinue===false);
  if(kind==='REPLACE'){
   const dialog=page.getByRole('dialog'),fresh=await this.options(page),current=fresh.recovery;
   requireTrue(sameOperation(op,current)&&current.impact.canContinue===true);
   const published=fresh.content[current.impact.impactKey];
   this.checkpoint?.('RECOVERY_REVIEW_BINDING');
   if(object(published)&&published.state==='PUBLISHED'&&published.locale===fresh.locale&&typeof published.text==='string'&&published.text.length>0)await dialog.getByText(published.text,{exact:true}).waitFor({state:'visible'});
   const displayed=object(published)&&published.state==='PUBLISHED'&&published.locale===fresh.locale&&typeof published.text==='string'&&published.text.length>0&&await dialog.getByText(published.text,{exact:true}).isVisible();
   this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',displayed);if(journeys[this.control.journeyId].includes('PUBLISHED_IMPACT_ORIGINAL_BINDING'))this.prove('PUBLISHED_IMPACT_ORIGINAL_BINDING',displayed);
   await dialog.getByRole('button',{name:'Continue',exact:true}).click();
  }
  await page.getByRole('dialog').getByRole('heading',{name:'Confirm it’s you',exact:true}).waitFor();
  this.checkpoint?.('RECOVERY_PROOF_BINDING');
  const proof=await this.mechanics.get(page,'/authentication/step-up/options');
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',proof.ok&&noDelivery(proof.body)&&proof.body.operationReference===op.operationReference&&proof.body.status==='PENDING');
  this.prove('NO_EARLY_DELIVERY',await page.locator('.recovery-codes-grid code').count()===0&&noDelivery(boot)&&noDelivery(start.body));
  this.checkpoint?.('JOURNEY');
  return{op,proof:proof.body,page,boot};
 }
 async proof(held,{denial=false,bodyLoss=false,publicationFault=false,hold=false}={}){
  const {page,op}=held,dialog=page.getByRole('dialog'),stats=this.mechanics.passkeyStats();
  const route='/authentication/step-up/'+this.method;
  const totpStage=label=>{if(this.method==='totp')this.checkpoint?.(label);};
  if(this.method==='password'){await dialog.getByRole('button',{name:'Other method',exact:true}).click();await privateFill(dialog.getByLabel('Password',{exact:true}),this.account.password);}
  if(this.method==='totp'){totpStage('REC_TOTP_CHOOSER');await dialog.getByRole('button',{name:'Authenticator Enter a 6-digit code',exact:true}).click();const input=dialog.getByLabel('Verification code',{exact:true});totpStage('REC_TOTP_ORACLE');const code=await this.mechanics.nextTotpCode(this.factor);totpStage('REC_TOTP_FILL');await privateFill(input,code);}
  if(denial){totpStage('REC_TOTP_DENY_ARM');await this.fixture('DENY_OWNER_ONCE');}
  if(bodyLoss)await this.fixture('DROP_FIRST_DELIVERY_ONCE');
  if(publicationFault){totpStage('REC_TOTP_PUBLICATION_ARM');await this.fixture('WITHDRAW_PUBLICATION_AFTER_APPLY');}
  const submit=()=>dialog.getByRole('button',{name:this.method==='passkey'?'Passkey':'Confirm',exact:true}).click();
  if(bodyLoss){
   const request=this.mechanics.request(page,route);await submit();const sent=await request;requireTrue(object(sent)&&sent.requestId===held.proof.requestId);
   await page.getByRole('heading',{name:'These codes are unavailable',exact:true}).waitFor();
   const safe=await this.result(op,page);
   this.prove('BODY_LOSS_NO_REDISCLOSURE',safe.status==='COMPLETED'&&safe.codesUnavailable===true&&await page.locator('.recovery-codes-grid code').count()===0&&await page.getByRole('button',{name:'Download',exact:true}).count()===0&&!await page.getByRole('dialog').getByText('These codes were already shown',{exact:true}).count());
   this.prove('ACTUAL_PROOF_METHOD',this.method==='password');return{safe,sent,bodyLost:true};
  }
  totpStage('REC_TOTP_PROOF_POST');const response=await this.response(page,route,submit);
  totpStage('REC_TOTP_REQUEST_BINDING');this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',response.sent.requestId===held.proof.requestId);
  this.prove('ACTUAL_PROOF_METHOD',this.method==='password'?typeof response.sent.password==='string'&&response.sent.password===this.account.password:this.method==='passkey'?object(response.sent.credential)&&this.mechanics.passkeyStats().asserted===stats.asserted+1:(totpStage('REC_TOTP_FACTOR_BINDING'),response.sent.authenticatorReference===this.factor.reference)&&(totpStage('REC_TOTP_CODE_SHAPE'),/^\d{6}$/.test(response.sent.code)));
  if(this.method==='passkey')this.prove('NATIVE_PASSKEY_PROOF',this.mechanics.passkeyStats().created===1&&this.mechanics.passkeyStats().asserted>stats.asserted);
  if(this.method==='totp')this.prove('NATIVE_TOTP_PROOF',(totpStage('REC_TOTP_NATIVE_FACTOR'),response.sent.authenticatorReference===this.factor.reference)&&(totpStage('REC_TOTP_NATIVE_CODE'),/^\d{6}$/.test(response.sent.code)));
  if(publicationFault){
   requireTrue((totpStage('REC_TOTP_PUBLICATION_HTTP'),!response.ok)&&(totpStage('REC_TOTP_PUBLICATION_NO_DELIVERY'),noDelivery(response.body)));totpStage('REC_TOTP_PUBLICATION_RESULT');const safe=await this.result(op,page,held.boot);totpStage('REC_TOTP_PUBLICATION_OPTIONS');const context=await this.mechanics.get(page,'/authentication/step-up/options');
   this.prove('CURRENT_PUBLICATION',(totpStage('REC_TOTP_PUBLICATION_PENDING'),safe.status==='PENDING_PROOF')&&(totpStage('REC_TOTP_PUBLICATION_OPTIONS_OK'),context.ok)&&(totpStage('REC_TOTP_PUBLICATION_REQUEST'),context.body.requestId===held.proof.requestId)&&(totpStage('REC_TOTP_PUBLICATION_ACTIVE'),context.body.totpRecovery?.status==='PROOF_ACTIVE')&&(totpStage('REC_TOTP_PUBLICATION_EMPTY'),await page.locator('.recovery-codes-grid code').count()===0));return{rejected:true,safe};
  }
  if(denial){
   if(hold)this.checkpoint?.('RECOVERY_SOURCE_DENIAL_CHECK');
   requireTrue((totpStage('REC_TOTP_DENIAL_STATUS'),response.status===503)&&(totpStage('REC_TOTP_DENIAL_NO_DELIVERY'),noDelivery(response.body)));
   if(hold)this.checkpoint?.('RECOVERY_SOURCE_HELD_OPTIONS');
   totpStage('REC_TOTP_DENIAL_OPTIONS');const current=await this.mechanics.get(page,'/authentication/step-up/options'),native=this.method==='passkey'?current.body.passkeyRecovery:current.body.totpRecovery;
   if(hold)this.checkpoint?.('RECOVERY_SOURCE_HELD_BINDING');
   requireTrue((totpStage('REC_TOTP_DENIAL_OPTIONS_OK'),current.ok)&&(totpStage('REC_TOTP_DENIAL_REQUEST'),native?.requestId===held.proof.requestId)&&(totpStage('REC_TOTP_DENIAL_ATTEMPT'),native.attemptId===response.sent.attemptId)&&(totpStage('REC_TOTP_DENIAL_OPERATION'),(this.method==='passkey'?native.operationReference:current.body.operationReference)===op.operationReference)&&(totpStage('REC_TOTP_DENIAL_PROOF_ACTIVE'),native.status===(this.method==='passkey'?'PROOF_ISSUED':'PROOF_ACTIVE')));
   if(hold){this.checkpoint?.('RECOVERY_SOURCE_HELD_RETURN');return{held,response,native};}
   totpStage('REC_TOTP_COUNTS_BEFORE');const before=await this.counts(),assertions=this.mechanics.passkeyStats().asserted;
   totpStage('REC_TOTP_RESUME_POST');const resumed=await this.response(page,route+'/continue',()=>dialog.getByRole('button',{name:'Continue this verification',exact:true}).click());
   totpStage('REC_TOTP_COUNTS_AFTER');const after=await this.counts();
   this.prove('OWNER_FAILURE_SAME_PROOF',(totpStage('REC_TOTP_RESUME_HTTP'),resumed.ok)&&(totpStage('REC_TOTP_RESUME_REQUEST'),resumed.sent.requestId===response.sent.requestId)&&(totpStage('REC_TOTP_RESUME_ATTEMPT'),resumed.sent.attemptId===response.sent.attemptId)&&(totpStage('REC_TOTP_RESUME_NO_CODE'),!Object.hasOwn(resumed.sent,'code'))&&(totpStage('REC_TOTP_RESUME_NO_CREDENTIAL'),!Object.hasOwn(resumed.sent,'credential'))&&(totpStage('REC_TOTP_RESUME_PROOFS'),after.proofs===before.proofs)&&(totpStage('REC_TOTP_RESUME_ACCEPTANCES'),after.codeAcceptances===before.codeAcceptances)&&(totpStage('REC_TOTP_RESUME_OWNER'),after.ownerCommits===before.ownerCommits+1)&&(totpStage('REC_TOTP_RESUME_CONSUMED'),after.consumedProofs===before.consumedProofs+1)&&(totpStage('REC_TOTP_RESUME_NATIVE_COUNT'),this.mechanics.passkeyStats().asserted===assertions));
   totpStage('REC_TOTP_CAPTURE');return this.capture(held,resumed);
  }
  totpStage('REC_TOTP_CAPTURE');return this.capture(held,response);
 }
 async capture(held,response){
  const {op,proof,page}=held,d=response.body.recoveryCodeFirstDelivery;
  this.prove('EXACT_OWNER_AND_STEP_UP_BINDING',response.ok&&response.body.status==='COMPLETED'&&response.body.signInRequired===false&&response.body.operationReference===op.operationReference&&object(d)&&d.ownerRequestId===op.ownerRequestId&&d.stepUpRequestId===proof.requestId&&['operationReference','setReference','ownerContextDigest','deliveryProfileVersion','formatProfileVersion'].every(k=>d[k]===op[k]));
  const codes=d.codes;requireTrue(Array.isArray(codes));this.remember(...codes);
  this.prove('CANONICAL_FIRST_DELIVERY',codes.length===op.codesPerSet&&codes.length<=op.maximumCodesPerSet&&codes.every(c=>typeof c==='string'&&/^[0-9A-F]{32}$/.test(c))&&new Set(codes).size===codes.length);
  await page.locator('.recovery-codes-grid code').first().waitFor();
  this.prove('CANONICAL_FIRST_DELIVERY',same(await page.locator('.recovery-codes-grid code').allTextContents(),codes));
  await this.privacy();
  return{codes,held,response};
 }
 async duplicate(held,complete){
  const before=await this.counts(),boot=await this.options(held.page),safe=await this.result(held.op,held.page);
  const duplicate=await this.mechanics.post(held.page,prefix+'/start-result',{ownerRequestId:held.op.ownerRequestId,expectedVersion:held.op.expectedVersion,kind:held.op.kind},boot.csrfToken),after=await this.counts();
  this.prove('DUPLICATE_NO_REDISCLOSURE',safe.status==='COMPLETED'&&safe.codesUnavailable===true&&duplicate.ok&&noDelivery(duplicate.body)&&sameOperation(held.op,duplicate.body)&&duplicate.body.status==='COMPLETED'&&sameCounts(before,after)&&noDelivery(boot)&&(!complete.codes||same(await held.page.locator('.recovery-codes-grid code').allTextContents(),complete.codes)));
 }
 async response(page,pathname,action){
  // Observe selected protection headers on this exact response, never a broad payload listener.
  const protection=page.waitForResponse(r=>new URL(r.url()).origin===this.control.origin&&new URL(r.url()).pathname===pathname&&r.request().method()==='POST',{timeout:budgets.action}).then(async r=>{const h=await r.allHeaders();return /(?:^|,)\s*no-store\s*(?:,|$)/i.test(h['cache-control']??'')&&h['referrer-policy']==='no-referrer';}).catch(()=>false);
  const result=await this.mechanics.response(page,pathname,action);this.headerFacts.push(await protection);return result;
 }
 async privacy(){
  requireExpectedRecoveryPages(this.browser,this.pages);let safe=true,cookiesSafe=true,hadProtected=false,headersSafe=this.headerFacts.length>0&&this.headerFacts.every(Boolean);
  for(const page of this.pages){
   requireTrue(!page.isClosed());
   const state=await bounded(page.evaluate(async()=>{
    const registrations='serviceWorker'in navigator?await navigator.serviceWorker.getRegistrations():[];
    const cacheNames='caches'in window?await caches.keys():[];
    return{url:location.href,referrer:document.referrer,name:window.name,history:JSON.stringify(history.state),local:Object.entries(localStorage),session:Object.entries(sessionStorage),service_worker_controller:!!navigator.serviceWorker?.controller,service_worker_registrations:registrations.length,cache_storage_names:cacheNames.length};
   }),budgets.action,'PREDICATE_FAILED');
   const cookies=await page.context().cookies(),protectedCookies=cookies.filter(c=>c.name.startsWith('__Host-'));hadProtected ||= protectedCookies.length>0;
   cookiesSafe &&= protectedCookies.every(c=>c.httpOnly&&c.secure&&c.path==='/'&&['Strict','Lax'].includes(c.sameSite));
   const carrier=JSON.stringify({...state,cookies});safe &&= state.local.length===0&&state.session.length===0&&!state.service_worker_controller&&state.service_worker_registrations===0&&state.cache_storage_names===0&&[...this.values].every(v=>!carrier.includes(v))&&!carrier.includes('otpauth://');
  }
  // Safe metadata reads only. Error/status responses must retain the same protections.
  for(const pathname of ['/authentication/session','/account/recovery-codes/options','/authentication/step-up/options']){
   const response=await this.page.request.get(new URL(pathname,this.control.origin).href,{timeout:budgets.action,failOnStatusCode:false});
   try{const h=response.headers();headersSafe &&= recoveryMetadataProtection(pathname,h);requireTrue(noDelivery((await this.mechanics.read(response)).body));}finally{await response.dispose();}
  }
  this.prove('COOKIE_FLAGS',cookiesSafe&&hadProtected);
  this.prove('NO_SECRET_URL_OR_STORAGE',safe&&headersSafe);
 }

 async clear(original,{pagehide=false}={}){
  if(pagehide){
   // Real full-page departure. BFCache persistence is observed, never assumed.
   await this.page.goto(new URL('/signed-in',this.control.origin).href);await this.mechanics.at(this.page,'/signed-in');await this.page.goBack();await this.mechanics.at(this.page,security);
  }else await this.page.getByRole('button',{name:'Done',exact:true}).click();
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0);await this.page.reload();await this.mechanics.at(this.page,security);
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0);
  if(this.method==='passkey')await this.acknowledgePreparedPasskey();
  const safe=await this.result(original);requireTrue(safe.status==='COMPLETED'&&safe.codesUnavailable===true);
  // Reload recovers the safe original result, which is acknowledged through the approved L02 path.
  await this.page.locator('.recovery-codes-card').getByRole('button',{name:'Check status',exact:true}).click();
  const dialog=this.page.getByRole('dialog');
  await dialog.getByRole('heading',{name:'These codes are unavailable',exact:true}).waitFor();
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0&&await this.page.getByRole('button',{name:'Download',exact:true}).count()===0);
  await dialog.getByRole('button',{name:'Close',exact:true}).click();await dialog.waitFor({state:'detached'});
  requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0);this.prove('VOLATILE_CLEAR',true);
  if(pagehide)this.prove('PAGEHIDE_NO_REVEAL',true);
  const text=await this.page.locator('body').textContent();this.prove('REQUESTED_NOT_SAVED',!text.includes('file was saved')&&!text.includes('Download requested'));
 }
 async execute(){
  await this.setup();
  if(this.kind==='REPLACE'){
   const initial=this.control.journeyId==='RECOVERY_REPLACE_PASSWORD_V1'?3:1;
   for(let i=0;i<initial;i++){const h=await this.begin('GENERATE'),r=await this.proof(h);await this.duplicate(h,r);await this.clear(h.op);}
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
   this.prove('CANONICAL_FIRST_DELIVERY',same(await this.page.locator('.recovery-codes-grid code').allTextContents(),completed.codes));
   await this.privacy();
   await this.clear(held.op);this.prove('CLOSE_NO_REVEAL',true);
  }else if(completed.bodyLost){await this.page.getByRole('button',{name:'Close',exact:true}).click();await this.page.reload();this.prove('VOLATILE_CLEAR',await this.page.locator('.recovery-codes-grid code').count()===0);this.prove('REQUESTED_NOT_SAVED',await this.page.getByRole('status').count()===0);}
  else await this.clear(held.op,{pagehide:this.control.journeyId==='RECOVERY_GENERATE_TOTP_V1'});
  if(this.kind==='REPLACE'){const unchanged=after.sessions===before.sessions&&(await this.mechanics.session(this.page)).ok;requireTrue(unchanged);if(journeys[this.control.journeyId].includes('NO_SESSION_MUTATION'))this.prove('NO_SESSION_MUTATION',unchanged);}
  if(this.control.journeyId==='RECOVERY_REPLACE_TOTP_V1'){
   const current=await this.counts(),pending=await this.begin(),rejected=await this.proof(pending,{publicationFault:true}),end=await this.counts();
   this.prove('CURRENT_PUBLICATION',rejected.rejected&&end.ownerCommits===current.ownerCommits&&end.sets===current.sets&&end.replacedSets===current.replacedSets&&end.entries===current.entries&&end.consumedProofs===current.consumedProofs&&await this.page.locator('.recovery-codes-grid code').count()===0);
  }
  if(this.control.journeyId==='RECOVERY_REPLACE_PASSKEY_V1'){
   const pending=await this.begin();this.checkpoint?.('RECOVERY_SOURCE_HOLD_REQUEST');
   const retained=await this.proof(pending,{denial:true,hold:true});
   this.checkpoint?.('RECOVERY_SOURCE_SECOND_CONTEXT');const other=await this.mechanics.newPage();this.pages.push(other);
   this.checkpoint?.('RECOVERY_SOURCE_SECOND_LOGIN');await this.mechanics.passwordLogin(other,this.account,false);
   this.checkpoint?.('RECOVERY_SOURCE_REVOKE');await this.mechanics.revokePasskey(other,this.account.password,this.checkpoint);
   this.checkpoint?.('RECOVERY_SOURCE_COUNTS_BEFORE');const current=await this.counts();
   this.checkpoint?.('RECOVERY_SOURCE_CONTINUE');
   const dialog=this.page.getByRole('dialog'),resume=dialog.getByRole('button',{name:'Continue this verification',exact:true});
   const refreshed=this.page.waitForResponse(response=>new URL(response.url()).origin===this.control.origin&&new URL(response.url()).pathname==='/authentication/step-up/options'&&response.request().method()==='GET',{timeout:budgets.action});refreshed.catch(()=>{});
   await resume.click();const shared=await this.mechanics.read(await refreshed),expired=shared.body.passkeyRecovery;
   requireTrue(shared.ok&&noDelivery(shared.body)&&shared.body.status==='PENDING'&&shared.body.requestId===retained.response.sent.requestId&&shared.body.operationReference===pending.op.operationReference&&expired?.requestId===retained.response.sent.requestId&&expired.attemptId===retained.response.sent.attemptId&&expired.ceremonyId===retained.native.ceremonyId&&expired.operationReference===pending.op.operationReference&&expired.status==='EXPIRED');
   await resume.waitFor({state:'detached'});requireTrue(await this.page.locator('.recovery-codes-grid code').count()===0&&await this.page.getByRole('button',{name:'Download',exact:true}).count()===0);
   // The UI fails closed before sending. This independent backend-bypass request uses the exact retained code-free command; APIRequestContext has no page request event.
   const continuation={requestId:retained.response.sent.requestId,attemptId:retained.response.sent.attemptId,ceremonyId:retained.native.ceremonyId};
   const rejected=await this.mechanics.post(this.page,'/authentication/step-up/passkey/continue',continuation,shared.body.csrfToken);
   this.checkpoint?.('RECOVERY_SOURCE_COUNTS_AFTER');const end=await this.counts();
   this.checkpoint?.('RECOVERY_SOURCE_CHECK');this.prove('SOURCE_FRESHNESS',!rejected.ok&&['STEP_UP_REQUIRED','VERSION_CONFLICT'].includes(rejected.body.error_code)&&continuation.requestId===shared.body.requestId&&continuation.attemptId===expired.attemptId&&continuation.ceremonyId===expired.ceremonyId&&rejected.body.requestId===continuation.requestId&&rejected.body.attemptId===continuation.attemptId&&rejected.body.ceremonyId===continuation.ceremonyId&&rejected.body.operationReference===pending.op.operationReference&&end.ownerCommits===current.ownerCommits&&end.sets===current.sets&&end.replacedSets===current.replacedSets&&end.entries===current.entries&&end.consumedProofs===current.consumedProofs&&noDelivery(rejected.body)&&await this.page.locator('.recovery-codes-grid code').count()===0);
  }
  await this.privacy();this.prove('STRICT_OWNED_SURFACE',await this.inspectOwnedSurface());
 }
 async close(){try{await this.mechanics.close();}finally{this.values.clear();}}
}

export async function runPreparedJourney(input){
 requireTrue(registeredJourney(input.control?.journeyId)&&typeof input.browser?.contexts==='function'&&typeof input.onContext==='function'&&typeof input.fixtureControl==='function'&&typeof input.nativeCode==='function'&&typeof input.requestOwnedDownload==='function'&&typeof input.inspectOwnedSurface==='function','INPUT_INVALID');
 const run=new RecoveryJourney(input);let failed=false;
 try{await run.execute();}catch{failed=true;}
 try{run.observations.finish(input.check);}catch{failed=true;}
 try{await run.close();}catch{throw fixedError('CLEANUP_FAILED');}
 if(failed)throw fixedError('PREDICATE_FAILED');
}

/** Exact inventory of this positively owned browser only; closed/unobserved pages fail. */
export function requireExpectedRecoveryPages(browser,pages){
 requireTrue(Array.isArray(pages)&&pages.length>=1&&pages.length<=2&&new Set(pages).size===pages.length);
 for(const page of pages)requireTrue(!page.isClosed());
 const expectedContexts=[...new Set(pages.map(page=>page.context()))],contexts=browser.contexts();
 requireTrue(contexts.length===expectedContexts.length&&contexts.every(context=>expectedContexts.includes(context)));
 const actual=contexts.flatMap(context=>context.pages());requireTrue(actual.length===pages.length&&actual.every(page=>pages.includes(page)));
}

/** Exact existing route ownership, not a new global BFF header policy. */
export function recoveryMetadataProtection(pathname,headers){
 requireTrue(['/authentication/session','/account/recovery-codes/options','/authentication/step-up/options'].includes(pathname),'INPUT_INVALID');
 const noStore=/(?:^|,)\s*no-store\s*(?:,|$)/i.test(headers['cache-control']??'');
 return noStore&&(pathname==='/authentication/session'||headers['referrer-policy']==='no-referrer');
}
