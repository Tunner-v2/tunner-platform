import {createHash, randomUUID} from 'node:crypto';
import {publicMarker} from './contract.mjs';

// This module is imported only by the protected raw child. It has no reporter,
// artifact, process, file, clipboard, storage-export or diagnostic-output API.
const common = Object.freeze(['BFF_ISOLATED','REQUEST_BINDING','OPERATION_BINDING','DUPLICATE_COUNTS','ONE_TIME_DISCLOSURE','INACTIVE_BEFORE_ACTIVATION','NATIVE_FIRST_CODE_ACTIVE','WRONG_CODE_REJECTED','REPLAY_REJECTED','RESULT_RECOVERY','COOKIE_FLAGS','NO_URL_OR_STORAGE_SECRET','UI_ASSET_TYPOGRAPHY']);
const extra = Object.freeze({
  TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1: Object.freeze(['DISCLOSURE_LOSS_NO_REVEAL','NATIVE_PASSWORD_MFA','NO_EARLY_SESSION','REPLAY_SEQUENCE_REJECTED','CONCURRENT_CODE_SINGLE_ACCEPT','SOURCE_FRESHNESS','PASSWORD_REGRESSION','DISABLE_PRESERVES_CONFIGURED_SESSIONS']),
  TOTP_OPERATION_PROOF_CAUSAL_REVOKE_V1: Object.freeze(['NATIVE_OPERATION_PROOF','OWNER_FAILURE_SAME_PROOF','SOURCE_FRESHNESS','EXPLICIT_CAUSAL_SESSION_EFFECT','UNRELATED_FACTOR_UNCHANGED','DISABLE_REJECTS_OLD_SOURCE','PASSWORD_REGRESSION']),
  TOTP_PASSKEY_MFA_RESET_V1: Object.freeze(['NATIVE_PASSKEY_MFA','NO_EARLY_SESSION','SOURCE_FRESHNESS','RESET_RETIRE_THEN_SEPARATE_ENROLLMENT','UNRELATED_FACTOR_UNCHANGED','PASSKEY_REGRESSION','PASSWORD_REGRESSION'])
});
const countKeys = Object.freeze(['ownerCommits','proofs','operations','sessions','outbox','codeAcceptances','mfaSessions']);
const manage = '/account/security/authenticator';
const fail = () => { throw new Error('REAL_JOURNEY_FAILED'); };
const requireTrue = value => { if (value !== true) fail(); };
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const sameCounts = (left,right) => Object.keys(left).length === Object.keys(right).length && Object.keys(left).every(key => left[key] === right[key]);
const operationIds = value => ({requestId:value.requestId,enrollmentId:value.enrollmentId});
const sameOperation = (left,right) => left.requestId === right.requestId && left.enrollmentId === right.enrollmentId && left.authenticatorReference === right.authenticatorReference && left.kind === right.kind;
const factorIdentity = value => JSON.stringify([value.authenticatorReference,value.state,value.version,value.createdAt,value.activatedAt,value.disabledAt]);
async function fillPrivate(locator,value,options) { try { await locator.fill(value,options); } catch { throw new Error('REAL_JOURNEY_FAILED'); } }
async function browserState(page) { return page.evaluate(() => { let local=[],session=[],storageReadable=true; try { local=Object.entries(localStorage); session=Object.entries(sessionStorage); } catch { storageReadable=false; } return {url:location.href,referrer:document.referrer,name:window.name,history:JSON.stringify(history.state),local,session,storageReadable}; }); }

class Journey {
  constructor(input) {
    Object.assign(this,input);
    this.observed = new Map(); this.secrets = new Set(); this.ownedContexts = []; this.pages = [input.page]; this.contexts = [input.context];
    this.cdp = undefined; this.device = undefined; this.created = 0; this.asserted = 0; this.controlCalls = 0;
    const origin = new URL(input.origin), mail = new URL(input.mailpitOrigin);
    requireTrue(origin.protocol === 'https:' && ['localhost','127.0.0.1'].includes(origin.hostname) && origin.pathname === '/' && !origin.search && !origin.hash);
    requireTrue(mail.protocol === 'http:' && ['localhost','127.0.0.1'].includes(mail.hostname) && mail.pathname === '/' && !mail.search && !mail.hash);
    this.origin = origin.origin; this.mailpitOrigin = mail.origin;
  }
  observe(id,value) { requireTrue(common.includes(id) || extra[this.journeyId].includes(id)); this.observed.set(id,(this.observed.get(id) ?? true) && value === true); }
  supports(id) { return common.includes(id) || extra[this.journeyId].includes(id); }
  prove(id,value) { this.observe(id,value); requireTrue(value === true); }
  remember(...values) { for (const value of values) if (typeof value === 'string' && value.length) this.secrets.add(value); this.privateRemember?.(...values); requireTrue(this.secrets.size <= 256); }
  active() { if (this.signal?.aborted) fail(); }
  url(path) { return new URL(path,this.origin).href; }
  async read(response) {
    this.active(); const text = await response.text(); requireTrue(Buffer.byteLength(text,'utf8') <= 1048576);
    let body; try { body = text === '' && !response.ok() ? {} : JSON.parse(text); } catch { fail(); }
    requireTrue(object(body)); return {status:response.status(),ok:response.ok(),body};
  }
  async get(page,path) { return this.read(await page.request.get(this.url(path),{timeout:5000,failOnStatusCode:false})); }
  async post(page,path,body,csrf) {
    requireTrue(typeof csrf === 'string' && csrf.length > 0); this.active();
    return this.read(await page.request.post(this.url(path),{headers:{Origin:this.origin,'X-Tunner-CSRF':csrf},data:body,timeout:5000,failOnStatusCode:false}));
  }
  async response(page,path,action) {
    const pending = page.waitForResponse(response => new URL(response.url()).origin === this.origin && new URL(response.url()).pathname === path && response.request().method() === 'POST',{timeout:5000});
    pending.catch(() => {}); await action(); const response = await pending;
    const sent = response.request().postDataJSON(); requireTrue(object(sent)); return {...await this.read(response),sent};
  }
  request(page,path) {
    return page.waitForRequest(request => new URL(request.url()).origin === this.origin && new URL(request.url()).pathname === path && request.method() === 'POST',{timeout:5000}).then(request => request.postDataJSON()).catch(() => null);
  }
  async at(page,path) { await page.waitForURL(url => url.origin === this.origin && url.pathname === path && !url.search && !url.hash,{timeout:10000}); }
  async control(action) {
    requireTrue(['READ_SAFE_COUNTS','DENY_OWNER_ONCE','DROP_DISCLOSURE_ONCE','DROP_ACTIVATION_ONCE','DROP_MFA_ONCE'].includes(action));
    requireTrue(++this.controlCalls <= 16);
    const reply = await this.fixtureControl(action); requireTrue(object(reply) && reply.status === 'OK' && object(reply.counts));
    requireTrue(Object.keys(reply.counts).sort().join('|') === [...(this.safeCountKeys ?? countKeys)].sort().join('|') && (this.safeCountKeys ?? countKeys).every(key => Number.isSafeInteger(reply.counts[key]) && reply.counts[key] >= 0 && reply.counts[key] <= 1000)); return reply.counts;
  }
  async session(page) { return this.get(page,'/authentication/session'); }
  async factors(page) { const result = await this.get(page,'/account/totp'); requireTrue(result.ok && Array.isArray(result.body.authenticators)); return result.body.authenticators; }
  async options(page) { const result = await this.get(page,'/account/totp/options'); requireTrue(result.ok && typeof result.body.csrfToken === 'string'); return result.body; }
  async operationResult(page,operation) { const boot = await this.options(page); return this.post(page,'/account/totp/result',operationIds(operation),boot.csrfToken); }
  async goManage(page) { if (new URL(page.url()).pathname !== manage) await page.goto(this.url(manage)); await this.at(page,manage); await page.locator('[data-tunner-screen="U-ACCT-TOTP"]').waitFor({state:'visible'}); }
  async newPage() {
    const context = await this.browser.newContext({baseURL:this.origin,acceptDownloads:false,serviceWorkers:'block'});
    this.ownedContexts.push(context); this.contexts.push(context); const page = await context.newPage(); page.setDefaultTimeout(5000); this.pages.push(page); return page;
  }
  async bootstrap() {
    const status = await this.get(this.page,'/browser-testing/status');
    this.prove('BFF_ISOLATED',status.ok && status.body.mode === (this.identityMode ?? 'FINITE_TOTP_BROWSER_TESTING') && status.body.ready === true && status.body.journeyId === this.journeyId && status.body.runId === this.runId);
    const account = {email:'sensitive-'+randomUUID()+'@example.test',password:'Synthetic unique passphrase '+randomUUID()}; this.remember(account.email,account.password);
    await this.page.goto(this.url('/registration')); await this.page.getByRole('heading',{name:'Create your Tunner account',exact:true}).waitFor();
    requireTrue(await this.page.locator('input[type=date]').count() === 0);
    await this.page.getByLabel('Email address',{exact:true}).fill(account.email); await this.page.getByLabel('Password',{exact:true}).fill(account.password); await this.page.getByLabel('Confirm password',{exact:true}).fill(account.password); await this.page.getByRole('checkbox').check();
    const registered = await this.response(this.page,'/registration/register',() => this.page.getByRole('button',{name:'Continue',exact:true}).click()); requireTrue(registered.status === 202);
    await this.page.getByRole('heading',{name:'Check your email',exact:true}).waitFor();
    let message;
    for (let attempt=0;attempt<80;attempt++) {
      this.active(); const response = await this.page.request.get(this.mailpitOrigin+'/api/v1/messages?limit=100',{timeout:5000,failOnStatusCode:false}); const inbox = await this.read(response);
      if (inbox.ok && Array.isArray(inbox.body.messages)) message = inbox.body.messages.find(value => Array.isArray(value.To) && value.To.some(address => address.Address === account.email));
      if (message) break; await new Promise(resolve => setTimeout(resolve,250));
    }
    requireTrue(object(message) && typeof message.ID === 'string' && /^[A-Za-z0-9-]{1,100}$/.test(message.ID));
    const mail = await this.read(await this.page.request.get(this.mailpitOrigin+'/api/v1/message/'+encodeURIComponent(message.ID),{timeout:5000,failOnStatusCode:false}));
    requireTrue(mail.ok && typeof mail.body.Text === 'string'); const verification = mail.body.Text.match(/code is: (\d{6})/)?.[1]; requireTrue(typeof verification === 'string'); this.remember(verification);
    await this.page.getByLabel('Verification code',{exact:true}).fill(verification); await this.page.getByRole('button',{name:'Verify email',exact:true}).click(); await this.page.getByRole('heading',{name:'Personal Information',exact:true}).waitFor();
    requireTrue(await this.page.getByLabel('Primary email',{exact:true}).inputValue() === account.email);
    await this.page.getByLabel('Full name',{exact:true}).fill('Synthetic Sensitive Test'); await this.page.getByLabel('Display name',{exact:true}).fill('Sensitive Tester'); await this.page.getByLabel('Date of birth (required)',{exact:true}).fill('1990-01-01'); await this.page.getByRole('button',{name:'Continue',exact:true}).click(); await this.page.getByRole('heading',{name:'Your email is verified',exact:true}).waitFor();
    await this.passwordLogin(this.page,account,false); if(this.recoveryPreparation){await this.page.goto(this.url('/account/security'));await this.at(this.page,'/account/security');await this.page.locator('[data-tunner-screen="U-ACCT-SECURITY"]').waitFor({state:'visible'});}else await this.goManage(this.page); await this.accountFonts(this.page); return account;
  }
  async passwordLogin(page,account,expectMfa) {
    await page.goto(this.url('/login')); await page.getByLabel('Email address',{exact:true}).fill(account.email); await page.getByLabel('Password',{exact:true}).fill(account.password);
    const result = await this.response(page,'/authentication/sign-in',() => page.getByRole('button',{name:'Sign in',exact:true}).click());
    if (expectMfa) return this.pending(page,result,'PASSWORD');
    requireTrue(result.ok && result.body.authenticated === true && result.body.status !== 'MFA_REQUIRED'); await this.at(page,'/signed-in'); requireTrue((await this.session(page)).ok); return result;
  }
  async pending(page,result,method) {
    const requestId = method === 'PASSWORD' ? result.sent.idempotencyKey : result.sent.requestId;
    this.prove('REQUEST_BINDING',result.ok && result.body.status === 'MFA_REQUIRED' && result.body.authenticated === false && uuid(requestId) && result.body.requestId === requestId);
    await this.at(page,'/login/verify-authenticator'); const boot = await this.mfaOptions(page); requireTrue(boot.requestId === requestId && boot.status === 'PENDING');
    requireTrue(boot.authenticators.length > 0);
    const mismatched = await this.post(page,'/authentication/totp/complete',{requestId:randomUUID(),authenticatorReference:boot.authenticators[0].authenticatorReference,code:'000000'},boot.csrfToken);
    this.prove('REQUEST_BINDING',mismatched.status === 409 && mismatched.body.error_code === 'VERSION_CONFLICT');
    const noEarly = (await this.session(page)).status === 401 && !(await page.context().cookies()).some(cookie => cookie.name === '__Host-Tunner.User.Session'); requireTrue(noEarly);
    if (this.supports('NO_EARLY_SESSION')) this.observe('NO_EARLY_SESSION',noEarly);
    return {requestId,method};
  }
  async mfaOptions(page) { const result = await this.get(page,'/authentication/totp/options'); requireTrue(result.ok && uuid(result.body.requestId) && typeof result.body.csrfToken === 'string' && Array.isArray(result.body.authenticators)); return result.body; }
  async select(page,factor,proof=false) {
    const container = proof ? page.getByRole('dialog') : page.locator('.totp-mfa');
    const input = container.getByLabel('Verification code',{exact:true});
    if (await input.count()) return input;
    const options = proof ? (await this.get(page,'/authentication/step-up/options')).body : await this.mfaOptions(page);
    const choices = proof ? options.totpAuthenticators : options.authenticators; requireTrue(Array.isArray(choices)); const selected = choices.find(value => value.authenticatorReference === factor.reference); requireTrue(object(selected) && Number.isSafeInteger(selected.displayPosition));
    await container.getByRole('button',{name:new RegExp('^Authenticator app '+selected.displayPosition+'(?:\\s|$)')}).click(); await input.waitFor({state:'visible'}); return input;
  }
  async native(seed,offset) { const code = await this.nativeCode(seed,offset); requireTrue(typeof code === 'string' && /^\d{6}$/.test(code)); this.remember(seed,code); return code; }
  async code(factor) {
    for (const offset of [0,1,-1,2,-2]) { const code = await this.native(factor.seed,offset); if (!factor.used.has(code)) return code; }
    fail();
  }
  async wrongCode(seed) {
    const codes = new Set(); for (const offset of [-2,-1,0,1,2]) codes.add(await this.native(seed,offset));
    let wrong='000000'; for (let number=0;codes.has(wrong)&&number<10;number++) wrong=String(number+1).padStart(6,'0'); requireTrue(!codes.has(wrong)); this.remember(...codes,wrong); return wrong;
  }
  async passwordProof(page,password,operation) {
    const dialog = page.getByRole('dialog'); await dialog.getByRole('button',{name:'Other method',exact:true}).click(); await dialog.getByLabel('Password',{exact:true}).fill(password);
    await this.proofFonts(page);
    const proof = await this.response(page,'/authentication/step-up/password',() => dialog.getByRole('button',{name:'Confirm',exact:true}).click());
    this.prove('OPERATION_BINDING',proof.ok && proof.body.status === 'COMPLETED' && proof.body.operationReference === operation.enrollmentId.replaceAll('-','')); return proof;
  }
  async beginEnrollment(page,password) {
    this.reportStep('JOURNEY_ENROLL_BEGIN');
    await this.goManage(page); const before = await this.factors(page);
    const started = await this.response(page,'/account/totp/enrollment/start',() => page.getByRole('button',{name:'Set up app',exact:true}).click());
    const operation = started.body; this.prove('REQUEST_BINDING',started.ok && uuid(started.sent.requestId) && operation.requestId === started.sent.requestId && uuid(operation.enrollmentId) && uuid(operation.authenticatorReference) && operation.kind === 'ENROLL' && operation.status === 'PENDING_PROOF');
    const disclosureRequest = this.request(page,'/account/totp/enrollment/disclose');
    const disclosureResponse = page.waitForResponse(response => new URL(response.url()).origin === this.origin && new URL(response.url()).pathname === '/account/totp/enrollment/disclose' && response.request().method() === 'POST',{timeout:5000}).then(response => this.read(response)).catch(() => null);
    await this.passwordProof(page,password,operation); await this.at(page,manage+'/setup'); return {before,operation,disclosureRequest,disclosureResponse};
  }
  async disclosureLoss(page,password) {
    await this.control('DROP_DISCLOSURE_ONCE'); const started = await this.beginEnrollment(page,password); const sent = await started.disclosureRequest;
    requireTrue(object(sent) && sent.requestId === started.operation.requestId && sent.enrollmentId === started.operation.enrollmentId);
    await page.getByRole('button',{name:'Check status',exact:true}).waitFor(); await page.getByRole('button',{name:'Check status',exact:true}).click(); await page.getByLabel('Verification code',{exact:true}).waitFor(); await page.reload(); await page.getByLabel('Verification code',{exact:true}).waitFor();
    const boot = await this.options(page), duplicate = await this.post(page,'/account/totp/enrollment/disclose',operationIds(started.operation),boot.csrfToken);
    this.prove('DISCLOSURE_LOSS_NO_REVEAL',duplicate.status === 409 && duplicate.body.error_code === 'DISCLOSURE_UNAVAILABLE' && await page.locator('.manual-key,.totp-qr').count() === 0 && boot.recovery?.enrollmentId === started.operation.enrollmentId && boot.recovery?.disclosureConsumed === true);
    this.observe('ONE_TIME_DISCLOSURE',duplicate.status === 409 && boot.recovery?.disclosureAvailable === false);
    await page.getByRole('button',{name:'Cancel',exact:true}).click(); await this.at(page,manage); requireTrue((await this.factors(page)).every(value => value.state !== 'ACTIVE'));
    this.reportStep('JOURNEY');
  }
  async enroll(page,password,{dropActivation=false}={}) {
    const started = await this.beginEnrollment(page,password); this.reportStep('JOURNEY_ENROLL_DISCLOSURE'); await page.locator('.manual-key code').waitFor({state:'visible'});
    const seed = (await page.locator('.manual-key code').textContent())?.trim(); requireTrue(typeof seed === 'string' && /^[A-Z2-7]{1,128}$/.test(seed)); this.remember(seed);
    const disclosure = await started.disclosureResponse, sent = await started.disclosureRequest;
    this.prove('REQUEST_BINDING',object(sent) && sent.requestId === started.operation.requestId && sent.enrollmentId === started.operation.enrollmentId && disclosure?.ok === true && disclosure.body.requestId === sent.requestId && disclosure.body.enrollmentId === sent.enrollmentId && disclosure.body.manualKey === seed);
    requireTrue(typeof disclosure.body.otpAuthUri === 'string' && new URL(disclosure.body.otpAuthUri).searchParams.get('secret') === seed); this.remember(disclosure.body.otpAuthUri);
    const current = await this.factors(page), factor = {reference:started.operation.authenticatorReference,seed,used:new Set(),operation:started.operation};
    this.prove('INACTIVE_BEFORE_ACTIVATION',current.some(value => value.authenticatorReference === factor.reference && value.state === 'INACTIVE') && current.filter(value => value.state === 'ACTIVE').length === started.before.filter(value => value.state === 'ACTIVE').length);
    const boot = await this.options(page), repeated = await this.post(page,'/account/totp/enrollment/disclose',operationIds(started.operation),boot.csrfToken);
    this.prove('ONE_TIME_DISCLOSURE',repeated.status === 409 && repeated.body.error_code === 'DISCLOSURE_UNAVAILABLE');
    const input = page.getByLabel('Verification code',{exact:true}); await fillPrivate(input,await this.wrongCode(seed)); const wrong = await this.response(page,'/account/totp/enrollment/activate',() => page.getByRole('button',{name:'Verify',exact:true}).click());
    this.prove('WRONG_CODE_REJECTED',wrong.status === 401 && wrong.body.error_code === 'AUTHENTICATION_FAILED' && (await this.factors(page)).find(value => value.authenticatorReference === factor.reference)?.state === 'INACTIVE');
    await input.waitFor({state:'visible'}); requireTrue(await input.inputValue() === '' && await page.locator('.manual-key,.totp-qr').count() === 0);
    const code = await this.code(factor); await fillPrivate(input,code); if (dropActivation) await this.control('DROP_ACTIVATION_ONCE'); const activationRequest = this.request(page,'/account/totp/enrollment/activate');
    await page.getByRole('button',{name:'Verify',exact:true}).click(); const activation = await activationRequest; requireTrue(object(activation)); await this.at(page,manage+'/ready');
    const recovered = await this.operationResult(page,started.operation);
    this.prove('NATIVE_FIRST_CODE_ACTIVE',recovered.ok && sameOperation(started.operation,recovered.body) && recovered.body.status === 'COMPLETED' && recovered.body.activationCompleted === true && (await this.factors(page)).find(value => value.authenticatorReference === factor.reference)?.state === 'ACTIVE');
    factor.used.add(code); factor.activationCode = code;
    this.reportStep('JOURNEY_ENROLL_RESULT_BINDING');
    const boundOptions = await this.options(page), mismatched = await this.post(page,'/account/totp/result',{requestId:randomUUID(),enrollmentId:started.operation.enrollmentId},boundOptions.csrfToken);
    this.prove('REQUEST_BINDING',mismatched.status === 404 && mismatched.body.error_code === 'NOT_FOUND_OR_NOT_DISCLOSABLE');
    this.reportStep('JOURNEY_ENROLL_DUPLICATE');
    const before = await this.control('READ_SAFE_COUNTS'), fresh = await this.options(page), duplicate = await this.post(page,'/account/totp/enrollment/activate',activation,fresh.csrfToken), after = await this.control('READ_SAFE_COUNTS');
    this.prove('RESULT_RECOVERY',duplicate.ok && sameOperation(recovered.body,duplicate.body) && duplicate.body.status === 'COMPLETED' && duplicate.body.activationCompleted === true);
    this.prove('DUPLICATE_COUNTS',sameCounts(before,after)); this.reportStep('JOURNEY_ENROLL_DONE'); await page.getByRole('button',{name:'Done',exact:true}).click(); await this.at(page,manage); this.reportStep('JOURNEY'); return factor;
  }
  async rejectMfa(page,pending,factor,code,expected='CODE_REPLAYED') {
    const boot = await this.mfaOptions(page); requireTrue(boot.requestId === pending.requestId);
    const response = await this.post(page,'/authentication/totp/complete',{requestId:pending.requestId,authenticatorReference:factor.reference,code},boot.csrfToken);
    requireTrue(!response.ok && response.status >= 400 && response.status < 500 && response.body.error_code === expected && (await this.session(page)).status === 401); return response;
  }
  async rejectStaleMfa(page,pending,factor) {
    const input = page.getByLabel('Verification code',{exact:true}), code = await this.native(factor.seed,0); await fillPrivate(input,code);
    const response = await this.response(page,'/authentication/totp/complete',() => page.getByRole('button',{name:'Verify',exact:true}).click());
    this.prove('REQUEST_BINDING',response.sent.requestId === pending.requestId && response.sent.authenticatorReference === factor.reference && response.sent.code === code);
    this.prove('SOURCE_FRESHNESS',response.status === 401 && response.body.error_code === 'AUTHENTICATION_FAILED' && (await this.session(page)).status === 401); return response;
  }
  async completeMfa(page,pending,factor,{code,drop=false}={}) {
    const input = await this.select(page,factor), value = code ?? await this.code(factor); await fillPrivate(input,value); const beforeRecovery = drop ? await this.control('DROP_MFA_ONCE') : null; const outbound = this.request(page,'/authentication/totp/complete');
    await page.getByRole('button',{name:'Verify',exact:true}).click(); const sent = await outbound; requireTrue(object(sent) && sent.requestId === pending.requestId && sent.authenticatorReference === factor.reference && sent.code === value);
    if (drop) {
      await page.getByRole('button',{name:'Check status',exact:true}).waitFor();
      const recovered = await this.response(page,'/authentication/totp/result',() => page.getByRole('button',{name:'Check status',exact:true}).click());
      requireTrue(Object.keys(recovered.sent).join('|') === 'requestId' && recovered.sent.requestId === pending.requestId && recovered.ok && recovered.body.status === 'COMPLETED' && recovered.body.authenticated === true && recovered.body.requestId === pending.requestId);
      pending.recovered = {before:beforeRecovery,requestId:recovered.body.requestId};
    }
    await this.at(page,'/signed-in'); requireTrue((await this.session(page)).ok); factor.used.add(value);
    const nativePredicate = pending.method === 'PASSKEY' ? 'NATIVE_PASSKEY_MFA' : 'NATIVE_PASSWORD_MFA';
    if (this.supports(nativePredicate)) this.observe(nativePredicate,true);
    return {sent,code:value};
  }
  async recoverMfa(page,pending) {
    requireTrue(object(pending.recovered) && pending.recovered.requestId === pending.requestId && (await this.session(page)).ok);
    const before = pending.recovered.before, after = await this.control('READ_SAFE_COUNTS');
    this.prove('RESULT_RECOVERY',after.sessions === before.sessions + 1 && after.mfaSessions === before.mfaSessions + 1 && after.codeAcceptances === before.codeAcceptances + 1);
    this.prove('DUPLICATE_COUNTS',after.ownerCommits === before.ownerCommits && after.operations === before.operations && after.proofs === before.proofs && after.outbox >= before.outbox + 1);
  }
  async beginMutation(page,kind,factor) {
    this.reportStep('JOURNEY_MUTATION_BEGIN');
    await this.goManage(page); const rows = await this.factors(page), active = rows.filter(value => value.state === 'ACTIVE'), index = active.findIndex(value => value.authenticatorReference === factor.reference); requireTrue(index >= 0);
    const target = kind === 'RESET' ? page.locator('.totp-factor').nth(index).getByRole('button',{name:'Review replacement',exact:true}) : active.length === 1 ? page.locator('.totp-panel.danger').getByRole('button',{name:'Remove',exact:true}) : page.locator('.totp-factor').nth(index).getByRole('button',{name:'Remove',exact:true});
    const result = await this.response(page,'/account/totp/'+kind.toLowerCase()+'/start',() => target.click()); const operation = result.body;
    this.prove('REQUEST_BINDING',result.ok && operation.requestId === result.sent.requestId && operation.authenticatorReference === factor.reference && operation.expectedVersion === active[index].version && operation.kind === kind && operation.status === 'PENDING_PROOF');
    const pending = page.waitForResponse(response => new URL(response.url()).origin === this.origin && new URL(response.url()).pathname === '/authentication/step-up/options' && response.request().method() === 'GET',{timeout:5000}); pending.catch(() => {});
    await page.getByRole('dialog').getByRole('button',{name:'Continue',exact:true}).click(); const options = await this.read(await pending);
    this.prove('OPERATION_BINDING',options.ok && options.body.operationReference === operation.enrollmentId.replaceAll('-','') && uuid(options.body.requestId)); this.reportStep('JOURNEY_MUTATION_READY'); return operation;
  }
  async finishMutationPassword(page,kind,factor,password) {
    const operation = await this.beginMutation(page,kind,factor); await this.passwordProof(page,password,operation); await page.locator('.totp-terminal').waitFor();
    const result = await this.operationResult(page,operation); requireTrue(result.ok && sameOperation(operation,result.body) && result.body.status === 'COMPLETED');
    return {operation,result:result.body};
  }
  async finishMutationProof(page,operation,factor,{deny=false}={}) {
    this.reportStep('JOURNEY_PROOF_ENTRY');
    if (deny) await this.control('DENY_OWNER_ONCE');
    await page.getByRole('dialog').getByRole('button',{name:/^Authenticator(?:\s|$)/}).click(); const input = await this.select(page,factor,true), code = await this.code(factor); await fillPrivate(input,code);
    await this.proofFonts(page);
    const issued = await this.response(page,'/authentication/step-up/totp',() => page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click());
    this.reportStep('JOURNEY_PROOF_RESPONSE');
    this.prove('OPERATION_BINDING',uuid(issued.sent.attemptId) && issued.sent.authenticatorReference === factor.reference && typeof issued.sent.requestId === 'string'); factor.used.add(code);
    if (deny) {
      this.reportStep('JOURNEY_PROOF_RECOVERY');
      requireTrue(!issued.ok); await page.getByRole('dialog').getByRole('button',{name:'Continue this verification',exact:true}).waitFor();
      const options = await this.get(page,'/authentication/step-up/options'), recovery = options.body.totpRecovery;
      requireTrue(options.ok && recovery?.status === 'PROOF_ACTIVE' && recovery.requestId === issued.sent.requestId && recovery.attemptId === issued.sent.attemptId && recovery.authenticatorReference === factor.reference);
      return {issued,code,operation,recovery};
    }
    this.prove('NATIVE_OPERATION_PROOF',issued.ok && issued.body.status === 'COMPLETED' && issued.body.operationReference === operation.enrollmentId.replaceAll('-','')); return {issued,code,operation};
  }
  async continueProof(page,held,{expectSuccess=true}={}) {
    this.reportStep('JOURNEY_PROOF_CONTINUE');
    const before = await this.control('READ_SAFE_COUNTS'), result = await this.response(page,'/authentication/step-up/totp/continue',() => page.getByRole('dialog').getByRole('button',{name:'Continue this verification',exact:true}).click()), after = await this.control('READ_SAFE_COUNTS');
    this.reportStep('JOURNEY_PROOF_CONTINUED');
    requireTrue(result.sent.requestId === held.issued.sent.requestId && result.sent.attemptId === held.issued.sent.attemptId && result.sent.authenticatorReference === held.issued.sent.authenticatorReference && !Object.hasOwn(result.sent,'code'));
    if (expectSuccess) {
      this.prove('NATIVE_OPERATION_PROOF',result.ok && result.body.status === 'COMPLETED' && result.body.operationReference === held.operation.enrollmentId.replaceAll('-',''));
      this.prove('OWNER_FAILURE_SAME_PROOF',before.proofs === after.proofs && before.codeAcceptances === after.codeAcceptances && after.ownerCommits === before.ownerCommits + 1);
      this.observe('DUPLICATE_COUNTS',after.operations === before.operations && after.proofs === before.proofs);
    } else this.prove('SOURCE_FRESHNESS',!result.ok && ['STEP_UP_REQUIRED','VERSION_CONFLICT'].includes(result.body.error_code) && before.ownerCommits === after.ownerCommits && before.proofs === after.proofs);
    return result;
  }
  async passkeyDevice(page) {
    this.cdp = await page.context().newCDPSession(page); await this.cdp.send('WebAuthn.enable',{enableUI:false});
    this.device = (await this.cdp.send('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',ctap2Version:'ctap2_1',transport:'usb',hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true,defaultBackupEligibility:false,defaultBackupState:false}})).authenticatorId;
    this.cdp.on('WebAuthn.credentialAdded',() => { this.created++; }); this.cdp.on('WebAuthn.credentialAsserted',() => { this.asserted++; });
  }
  async enrollPasskey(page,password) {
    await page.goto(this.url('/account/security/sign-in-methods')); await page.getByRole('button',{name:'Add passkey',exact:true}).click();
    const start = await this.response(page,'/account/passkeys/enrollment/start',() => page.getByRole('dialog').getByRole('button',{name:'Add passkey',exact:true}).click()); requireTrue(start.ok && start.body.status === 'PENDING');
    await page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true}).click(); await page.getByRole('dialog').getByLabel('Password',{exact:true}).fill(password);
    const proof = await this.response(page,'/authentication/step-up/password',() => page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click()); requireTrue(proof.ok && proof.body.operationReference === start.body.ceremonyId.replaceAll('-',''));
    const created = this.created, completion = await this.response(page,'/account/passkeys/enrollment/complete',() => page.getByRole('dialog').getByRole('button',{name:'Continue setup',exact:true}).click());
    requireTrue(completion.ok && this.created === created + 1 && completion.body.status === 'COMPLETED'); await page.getByRole('dialog').waitFor({state:'hidden'});
  }
  async passkeyLogin(page) {
    await page.goto(this.url('/login')); const before = this.asserted;
    const result = await this.response(page,'/authentication/passkeys/sign-in',() => page.getByRole('button',{name:'Sign in with passkey',exact:true}).click()); requireTrue(this.asserted > before); return this.pending(page,result,'PASSKEY');
  }
  async signOut(page) { await page.goto(this.url('/signed-in')); await page.getByRole('button',{name:'Sign out',exact:true}).click(); await this.at(page,'/login'); requireTrue((await this.session(page)).status === 401); }
  async accountFonts(page) {
    await page.evaluate(() => document.fonts.ready);
    const loaded = await page.evaluate(() => ({chosen:getComputedStyle(document.querySelector('.passkeys-header-label')).fontFamily,faces:[...document.fonts].map(face => ({family:face.family,status:face.status}))}));
    const response = await page.request.get(this.url('/registration/assets/fonts/GeistMono-wght.ttf'),{timeout:5000,failOnStatusCode:false}), bytes = await response.body();
    this.monoVerified = response.ok() && /font|octet-stream/i.test(response.headers()['content-type'] ?? '') && createHash('sha256').update(bytes).digest('hex') === 'd00e590b8eb3a59acc329b2d044fd143ae935090b7da33199ebee27cc7de8196' && loaded.chosen.includes('Geist Mono') && loaded.faces.some(face => face.family === 'Geist Mono' && face.status === 'loaded');
    requireTrue(this.monoVerified);
  }
  async proofFonts(page) {
    await page.evaluate(() => document.fonts.ready);
    const selected = await page.evaluate(() => ({chosen:getComputedStyle(document.querySelector('dialog input')).fontFamily,faces:[...document.fonts].map(face => ({family:face.family,status:face.status})),resources:performance.getEntriesByType('resource').map(resource => resource.name).filter(name => /\/inter-latin-wght-normal-[^/]+\.woff2(?:\?|$)/.test(name))}));
    const resource = selected.resources.find(value => new URL(value).origin === this.origin); requireTrue(typeof resource === 'string');
    const response = await page.request.get(resource,{timeout:5000,failOnStatusCode:false}), bytes = await response.body();
    this.prove('UI_ASSET_TYPOGRAPHY',this.monoVerified === true && response.ok() && /font|octet-stream/i.test(response.headers()['content-type'] ?? '') && createHash('sha256').update(bytes).digest('hex') === '3100e775e8616cd2611beecfa23a4263d7037586789b43f035236a2e6fbd4c62' && selected.chosen.includes('Inter Variable') && selected.faces.some(face => face.family === 'Inter Variable' && face.status === 'loaded'));
  }
  async cancelHeld(page,operation) {
    this.reportStep('JOURNEY_MUTATION_CANCEL');
    const cancelled = async action => {
      const result = await this.response(page,'/account/totp/cancel',action);
      requireTrue(result.ok && sameOperation(operation,result.body) && ['REJECTED','EXPIRED'].includes(result.body.status));
    };
    const dialog = page.getByRole('dialog'); requireTrue(await dialog.count() === 1);
    const close = dialog.getByRole('button',{name:/^(Close|Cancel)$/});
    // Wait for pending inspection to settle before choosing its fixed close behavior.
    await close.click({trial:true});
    if (await dialog.getByRole('button',{name:'Cancel',exact:true}).count()) {
      await cancelled(() => dialog.getByRole('button',{name:'Cancel',exact:true}).click());
    } else {
      await dialog.getByRole('button',{name:'Close',exact:true}).click(); await dialog.waitFor({state:'hidden'});
      await page.locator('.totp-recovery').getByRole('button',{name:'Check status',exact:true}).click();
      await cancelled(() => page.locator('.totp-review-dialog').getByRole('button',{name:'Cancel',exact:true}).click());
    }
    await dialog.waitFor({state:'hidden'}); await page.locator('.totp-recovery').waitFor({state:'hidden'});
    await this.at(page,manage); this.reportStep('JOURNEY_MUTATION_CANCELLED');
  }
  async rejectDisabledProof(page,factor) {
    await page.goto(this.url('/browser-testing/step-up'));
    const pending = page.waitForResponse(response => new URL(response.url()).origin === this.origin && new URL(response.url()).pathname === '/authentication/step-up/options' && response.request().method() === 'GET',{timeout:5000}); pending.catch(() => {});
    await page.getByRole('button',{name:'Verify identity',exact:true}).click(); const options = await this.read(await pending); requireTrue(options.ok && options.body.status === 'PENDING');
    const result = await this.post(page,'/authentication/step-up/totp',{requestId:options.body.requestId,attemptId:randomUUID(),authenticatorReference:factor.reference,code:await this.native(factor.seed,0)},options.body.csrfToken);
    this.prove('DISABLE_REJECTS_OLD_SOURCE',result.status === 401 && result.body.error_code === 'AUTHENTICATION_FAILED' && (await this.session(page)).ok);
    await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();
  }
  async privacy() {
    let safe = true, cookiesSafe = true, hadProtected = false;
    for (const page of this.pages) {
      if (page.isClosed()) continue;
      const state = await browserState(page);
      const data = JSON.stringify(state); safe &&= state.storageReadable === true && state.local.length === 0 && state.session.length === 0 && !data.includes('otpauth://') && [...this.secrets].every(secret => !data.includes(secret));
    }
    for (const context of this.contexts) {
      const cookies = await context.cookies(); for (const cookie of cookies) {
        if (!cookie.name.startsWith('__Host-')) continue; hadProtected = true;
        cookiesSafe &&= cookie.secure && cookie.httpOnly && cookie.path === '/' && ['Strict','Lax'].includes(cookie.sameSite);
        safe &&= !cookie.value.includes('otpauth://') && [...this.secrets].every(secret => !cookie.value.includes(secret));
      }
    }
    this.prove('COOKIE_FLAGS',cookiesSafe && hadProtected); this.prove('NO_URL_OR_STORAGE_SECRET',safe);
  }
  async cleanup() {
    let failed = false;
    if (this.cdp) { try { await this.cdp.send('WebAuthn.disable'); } catch { failed = true; } try { await this.cdp.detach(); } catch { failed = true; } }
    for (const context of this.ownedContexts) { try { await context.close(); } catch { failed = true; } }
    this.secrets.clear(); if (failed) fail();
  }
  finish() {
    let rejected = false;
    for (const id of [...common,...extra[this.journeyId]]) {
      try { this.check(id,this.observed.get(id) === true); } catch { rejected = true; }
    }
    if (rejected) throw new Error('PREDICATE_FAILED');
  }
}

async function enrollmentPasswordPreserve(run) {
  const account = await run.bootstrap(); await run.disclosureLoss(run.page,account.password);
  const a = await run.enroll(run.page,account.password,{dropActivation:true}), b = await run.enroll(run.page,account.password);
  const login = await run.newPage(), pending = await run.passwordLogin(login,account,true);
  await run.rejectMfa(login,pending,a,a.activationCode); run.prove('REPLAY_REJECTED',true);
  const accepted = await run.completeMfa(login,pending,a,{drop:true}); await run.recoverMfa(login,pending);
  const sequence = await run.newPage(), next = await run.passwordLogin(sequence,account,true); await run.rejectMfa(sequence,next,a,a.activationCode); run.prove('REPLAY_SEQUENCE_REJECTED',accepted.code !== a.activationCode); await run.completeMfa(sequence,next,b);
  const first = await run.newPage(), second = await run.newPage(), left = await run.passwordLogin(first,account,true), right = await run.passwordLogin(second,account,true), code = await run.code(b), before = await run.control('READ_SAFE_COUNTS');
  const options = await Promise.all([run.mfaOptions(first),run.mfaOptions(second)]), results = await Promise.all([run.post(first,'/authentication/totp/complete',{requestId:left.requestId,authenticatorReference:b.reference,code},options[0].csrfToken),run.post(second,'/authentication/totp/complete',{requestId:right.requestId,authenticatorReference:b.reference,code},options[1].csrfToken)]), after = await run.control('READ_SAFE_COUNTS');
  const sessions = await Promise.all([run.session(first),run.session(second)]);
  run.prove('CONCURRENT_CODE_SINGLE_ACCEPT',results.filter(value => value.ok && value.body.status === 'COMPLETED').length === 1 && results.filter(value => value.body.error_code === 'CODE_REPLAYED').length === 1 && results.every((value,index) => value.ok ? sessions[index].ok : sessions[index].status === 401) && after.codeAcceptances === before.codeAcceptances + 1 && after.mfaSessions === before.mfaSessions + 1 && after.sessions === before.sessions + 1); b.used.add(code);
  const winning = results[0].ok ? first : second; await winning.goto(run.url('/signed-in')); await run.at(winning,'/signed-in');
  const stale = await run.newPage(), original = await run.passwordLogin(stale,account,true), unchanged = (await run.factors(run.page)).find(value => value.authenticatorReference === b.reference); await run.select(stale,a);
  const removed = await run.finishMutationPassword(run.page,'DISABLE',a,account.password); run.prove('DISABLE_PRESERVES_CONFIGURED_SESSIONS',removed.result.factorSessionEffect === 'PreserveExistingSessions' && removed.result.currentSessionEnds === false && (await run.session(login)).ok && (await run.session(run.page)).ok);
  await run.rejectStaleMfa(stale,original,a);
  await run.page.getByRole('button',{name:'Done',exact:true}).click(); requireTrue(factorIdentity((await run.factors(run.page)).find(value => value.authenticatorReference === b.reference)) === factorIdentity(unchanged));
  await run.finishMutationPassword(run.page,'DISABLE',b,account.password); await run.page.getByRole('button',{name:'Done',exact:true}).click(); const regression = await run.newPage(); await run.passwordLogin(regression,account,false); run.prove('PASSWORD_REGRESSION',true); await run.privacy();
}

async function operationProofCausalRevoke(run) {
  const account = await run.bootstrap(), a = await run.enroll(run.page,account.password), b = await run.enroll(run.page,account.password);
  const causal = await run.newPage(), pending = await run.passwordLogin(causal,account,true); await run.rejectMfa(causal,pending,a,a.activationCode); run.prove('REPLAY_REJECTED',true); await run.completeMfa(causal,pending,a);
  const staleOperation = await run.beginMutation(causal,'DISABLE',a), held = await run.finishMutationProof(causal,staleOperation,b,{deny:true});
  await run.finishMutationPassword(run.page,'DISABLE',b,account.password); await run.page.getByRole('button',{name:'Done',exact:true}).click(); await run.continueProof(causal,held,{expectSuccess:false});
  // The rejected exact original action is explicitly cancelled through its UI.
  await run.cancelHeld(causal,held.operation);
  const unrelated = (await run.factors(run.page)).find(value => value.authenticatorReference === b.reference), operation = await run.beginMutation(causal,'DISABLE',a), failed = await run.finishMutationProof(causal,operation,a,{deny:true});
  const completed = await run.continueProof(causal,failed); requireTrue(completed.body.signInRequired === true); await causal.getByRole('heading',{name:'Sign in again to continue',exact:true}).waitFor();
  const terminal = await run.get(causal,'/account/totp/terminal-options'); requireTrue(terminal.ok && terminal.body.recovery?.enrollmentId === operation.enrollmentId);
  const result = await run.post(causal,'/account/totp/terminal-result',operationIds(operation),terminal.body.csrfToken);
  run.prove('EXPLICIT_CAUSAL_SESSION_EFFECT',result.ok && sameOperation(operation,result.body.operation) && result.body.signInRequired === true && result.body.operation?.factorSessionEffect === 'RevokeFactorSessions' && result.body.operation?.currentSessionEnds === true && (await run.session(causal)).status === 401 && (await run.session(run.page)).ok);
  run.prove('UNRELATED_FACTOR_UNCHANGED',factorIdentity((await run.factors(run.page)).find(value => value.authenticatorReference === b.reference)) === factorIdentity(unrelated));
  requireTrue((await run.get(causal,'/account/totp')).status === 401 && (await run.factors(run.page)).find(value => value.authenticatorReference === a.reference)?.state === 'DISABLED'); await run.rejectDisabledProof(run.page,a);
  const regression = await run.newPage(); await run.passwordLogin(regression,account,false); run.prove('PASSWORD_REGRESSION',true); await run.privacy();
}

async function passkeyMfaReset(run) {
  const account = await run.bootstrap(); await run.passkeyDevice(run.page); await run.enrollPasskey(run.page,account.password);
  const a = await run.enroll(run.page,account.password), b = await run.enroll(run.page,account.password), keeper = await run.newPage(), keeperPending = await run.passwordLogin(keeper,account,true); await run.completeMfa(keeper,keeperPending,b);
  await run.signOut(run.page); const pending = await run.passkeyLogin(run.page), before = (await run.factors(keeper)).find(value => value.authenticatorReference === b.reference); await run.select(run.page,a);
  const reset = await run.finishMutationPassword(keeper,'RESET',a,account.password); requireTrue(reset.result.status === 'COMPLETED');
  await run.rejectStaleMfa(run.page,pending,a);
  const methods = await run.factors(keeper); run.prove('UNRELATED_FACTOR_UNCHANGED',factorIdentity(methods.find(value => value.authenticatorReference === b.reference)) === factorIdentity(before));
  requireTrue(methods.find(value => value.authenticatorReference === a.reference)?.state === 'DISABLED'); await keeper.getByRole('button',{name:'Done',exact:true}).click();
  const replacement = await run.enroll(keeper,account.password); run.prove('RESET_RETIRE_THEN_SEPARATE_ENROLLMENT',replacement.reference !== a.reference && replacement.operation.requestId !== reset.operation.requestId && replacement.operation.enrollmentId !== reset.operation.enrollmentId && (await run.factors(keeper)).find(value => value.authenticatorReference === b.reference)?.state === 'ACTIVE');
  await run.page.reload(); await run.at(run.page,'/login/verify-authenticator'); await run.completeMfa(run.page,pending,b,{drop:true}); await run.recoverMfa(run.page,pending);
  await run.signOut(run.page); const replay = await run.passkeyLogin(run.page); await run.rejectMfa(run.page,replay,b,b.activationCode); run.prove('REPLAY_REJECTED',true); await run.completeMfa(run.page,replay,replacement); run.prove('PASSKEY_REGRESSION',run.created === 1 && run.asserted >= 2);
  const password = await run.newPage(), passwordPending = await run.passwordLogin(password,account,true); await run.completeMfa(password,passwordPending,b); run.prove('PASSWORD_REGRESSION',(await run.session(password)).ok); await run.privacy();
}

const journeys = Object.freeze({
  TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1: enrollmentPasswordPreserve,
  TOTP_OPERATION_PROOF_CAUSAL_REVOKE_V1: operationProofCausalRevoke,
  TOTP_PASSKEY_MFA_RESET_V1: passkeyMfaReset
});

export async function runRealJourney(input) {
  requireTrue(object(input) && Object.hasOwn(journeys,input.journeyId) && typeof input.runId === 'string' && /^[0-9a-f]{32}$/.test(input.runId) && typeof input.check === 'function' && typeof input.nativeCode === 'function' && typeof input.fixtureControl === 'function' && typeof input.reportStep === 'function');
  const run = new Journey(input);
  let journeyFailed = false, flushFailed = false, cleanupFailed = false;
  try { await journeys[input.journeyId](run); } catch { journeyFailed = true; }
  try { run.finish(); } catch { flushFailed = true; }
  try { await run.cleanup(); } catch { cleanupFailed = true; }
  // Preserve the current fixed journey step; finalization never substitutes its frontier.
  if (journeyFailed || flushFailed) throw new Error('PREDICATE_FAILED');
  if (cleanupFailed) throw new Error('CLEANUP_FAILED');
}

// Literal protected public rehearsal. It exercises the same private-fill and
// browser-state helpers on a real public DOM, with no account/BFF/provider call.
export async function runPublicJourneyCanary({page,context,browser,signal}) {
  if (signal?.aborted) fail();
  await page.setContent('<main><h1>Public journey rehearsal</h1><label for="public-enabled">Public enabled code</label><input id="public-enabled"><label for="public-disabled">Public disabled code</label><input id="public-disabled" disabled></main>');
  const enabled = page.getByLabel('Public enabled code',{exact:true}), disabled = page.getByLabel('Public disabled code',{exact:true});
  await fillPrivate(enabled,publicMarker); const publicDom = await enabled.inputValue() === publicMarker; await fillPrivate(enabled,'');
  let helperErrorContained = false;
  try { await fillPrivate(disabled,publicMarker,{timeout:100}); }
  catch (error) { helperErrorContained = error instanceof Error && error.message === 'REAL_JOURNEY_FAILED' && await disabled.inputValue() === ''; }
  const state = await browserState(page); requireTrue(state.local.length === 0 && state.session.length === 0 && !JSON.stringify(state).includes(publicMarker));
  // Public-only synthetic map: exercise the actual finish helper without emitting real checks.
  const emitted = [], journeyId = 'TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1';
  const probe = new Journey({page,context,browser,signal,journeyId,origin:'https://localhost',mailpitOrigin:'http://127.0.0.1',check:(id,value) => { emitted.push([id,value]); if (!value) throw new Error('PREDICATE_FAILED'); }});
  probe.observe('BFF_ISOLATED',false); probe.observe('UI_ASSET_TYPOGRAPHY',true);
  let rejected = false; try { probe.finish(); } catch (error) { rejected = error instanceof Error && error.message === 'PREDICATE_FAILED'; }
  const expected = [...common,...extra[journeyId]];
  const flushed = rejected && emitted.length === expected.length && emitted.every(([id],index) => id === expected[index]) && emitted[0][1] === false && emitted.find(([id]) => id === 'UI_ASSET_TYPOGRAPHY')?.[1] === true;
  requireTrue(flushed);
  return {publicDom:publicDom && flushed,helperErrorContained};
}

// Explicit preparation capability for the separate recovery child. No TOTP dispatcher/default changes.
export function createRecoveryAccountMechanics(input){
 const ids=['RECOVERY_GENERATE_PASSWORD_V1','RECOVERY_REPLACE_PASSWORD_V1','RECOVERY_GENERATE_PASSKEY_V1','RECOVERY_REPLACE_PASSKEY_V1','RECOVERY_GENERATE_TOTP_V1','RECOVERY_REPLACE_TOTP_V1'];
 requireTrue(object(input)&&ids.includes(input.journeyId)&&typeof input.runId==='string'&&/^[a-f0-9]{32}$/.test(input.runId)&&typeof input.remember==='function'&&typeof input.fixtureControl==='function'&&typeof input.nativeCode==='function');
 const run=new Journey({...input,reportStep:()=>{},recoveryPreparation:true,identityMode:'FINITE_RECOVERY_BROWSER_TESTING',safeCountKeys:['ownerCommits','operations','sets','replacedSets','entries','proofs','consumedProofs','sessions','outbox','codeAcceptances'],privateRemember:input.remember,fixtureControl:async action=>{requireTrue(action==='READ_SAFE_COUNTS');return input.fixtureControl(action);}});
 const port={};for(const method of ['bootstrap','read','get','post','response','request','at','session','newPage','passwordLogin'])port[method]=run[method].bind(run);
 return Object.freeze({...port,setupPasskey:async(page,password)=>{await run.passkeyDevice(page);await run.enrollPasskey(page,password);},enrollTotp:run.enroll.bind(run),nextTotpCode:async factor=>{const code=await run.code(factor);factor.used.add(code);return code;},passkeyStats:()=>({created:run.created,asserted:run.asserted}),close:run.cleanup.bind(run),revokePasskey:async(page,password)=>{
  await page.goto(run.url('/account/security/sign-in-methods'));await page.getByRole('button',{name:'Manage',exact:true}).click();
  const started=await run.response(page,'/account/passkeys/revocation/start',()=>page.getByRole('dialog').getByRole('button',{name:'Remove passkey',exact:true}).click());requireTrue(started.ok&&started.body.status==='PENDING');
  await page.getByRole('dialog').getByRole('button',{name:'Other method',exact:true}).click();await fillPrivate(page.getByLabel('Password',{exact:true}),password);
  const proof=await run.response(page,'/authentication/step-up/password',()=>page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click());requireTrue(proof.ok&&proof.body.operationReference===started.body.ceremonyId.replaceAll('-',''));
  await page.getByRole('dialog').waitFor({state:'hidden'});const methods=await run.get(page,'/account/passkeys');requireTrue(methods.ok&&methods.body.credentials.some(value=>value.credentialReference===started.body.credentialReference&&value.state==='REVOKED'));
 }});
}
