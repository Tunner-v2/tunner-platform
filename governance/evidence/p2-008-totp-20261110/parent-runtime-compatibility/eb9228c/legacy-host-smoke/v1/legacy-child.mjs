// Artifact-only protected raw Playwright child. Never import the test runner.
import {createRequire} from 'node:module';
import {dirname, join} from 'node:path';
import {randomUUID} from 'node:crypto';
import {lstat, readdir, open} from 'node:fs/promises';
import {scanSurface, validSurfacePath} from '../../../web/tests/totp/sensitive/contract.mjs';

const ids = ['BROWSER_ENGINE','NORMAL_HOST_ISOLATED','REGISTRATION_CREATED','EMAIL_VERIFIED','PROFILE_COMPLETED','PASSWORD_SIGN_IN','SIGNED_IN_RELOAD','GENERIC_STEP_UP_BOUND','GENERIC_STEP_UP_COMPLETED','COMPLETION_RECOVERY','COOKIE_FLAGS','PRIVATE_BROWSER_STATE','LOGOUT_GUARDS'];
let browser; let context; let control; let surface; let fatal = false; let browserVersion = null; let surfaceClean = false; let cleanupCompleted = false;
let stage = 'START'; const checks = Object.create(null); const secrets = [];
const abort = new AbortController();
const fail = () => { throw new Error('SMOKE_FAILED'); };
const need = value => { if (value !== true || abort.signal.aborted) fail(); };
const check = (id, value) => { need(ids.includes(id) && !Object.hasOwn(checks,id) && typeof value === 'boolean'); checks[id] = value; need(value); };
const exact = (value, keys) => value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).sort().join('|') === [...keys].sort().join('|');
const origin = (value, protocol, host) => { try { const url = new URL(value); return url.origin === value && url.protocol === protocol && url.hostname === host && /^\d{4,5}$/.test(url.port); } catch { return false; } };
const send = value => new Promise(resolve => { try { if (process.connected) process.send(value, resolve); else resolve(); } catch { resolve(); } });
const bounded = (promise, ms = 5000) => { let timer; return Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('SMOKE_FAILED')), ms); })]).finally(() => clearTimeout(timer)); };
const onFatal = () => { fatal = true; abort.abort(); };
process.on('uncaughtException', onFatal); process.on('unhandledRejection', onFatal);
const initial = new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('SMOKE_FAILED')),10000);
  process.on('message', value => {
    if (exact(value,['version','type']) && value.version === 1 && value.type === 'cancel') { abort.abort(); return; }
    if (!control && exact(value,['version','type','origin','mailpitOrigin','instanceId','surface']) && value.version === 1 && value.type === 'initialize' &&
        origin(value.origin,'https:','localhost') && origin(value.mailpitOrigin,'http:','127.0.0.1') && /^[a-f0-9-]{36}$/.test(value.instanceId) && validSurfacePath(value.surface)) {
      control = value; surface = value.surface; clearTimeout(timer); resolve(); return;
    }
    onFatal();
  });
});
async function read(response) {
  const text = await response.text(); need(Buffer.byteLength(text) <= 1048576);
  let body; try { body = JSON.parse(text); } catch { fail(); }
  need(body !== null && typeof body === 'object' && !Array.isArray(body)); return {status: response.status(), ok: response.ok(), body};
}
async function postResponse(page, path, action) {
  const pending = page.waitForResponse(response => new URL(response.url()).origin === control.origin && new URL(response.url()).pathname === path && response.request().method() === 'POST', {timeout:10000});
  pending.catch(() => {}); await action(); return read(await pending);
}
async function at(page, path) { await page.waitForURL(value => value.origin === control.origin && value.pathname === path && !value.search && !value.hash, {timeout:10000}); }
async function privateState(page) {
  return page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0 && location.search === '' && location.hash === '' && window.name === '' && !document.cookie.includes('__Host-Tunner.User.Session') && !document.cookie.includes('__Host-Tunner.StepUp'));
}
async function scanPrivateFiles(directory) {
  // The values never leave this child. Inspect browser residue only after context/browser close.
  const markers = secrets.flatMap(value => [Buffer.from(value),Buffer.from(value,'utf16le'),Buffer.from(encodeURIComponent(value))]);
  let files = 0; let size = 0;
  async function visit(path, depth) {
    need(depth <= 25);
    for (const name of await readdir(path)) {
      const full = join(path,name); const stat = await lstat(full); need(!stat.isSymbolicLink());
      if (stat.isDirectory()) { await visit(full,depth+1); continue; }
      need(stat.isFile() && ++files <= 10000 && stat.size <= 64*1024*1024 && (size += stat.size) <= 256*1024*1024);
      let file;
      try {
        file = await open(full,'r'); const chunk = Buffer.alloc(65536); let tail = Buffer.alloc(0);
        for (;;) { const {bytesRead} = await file.read(chunk); if (!bytesRead) break; const data = Buffer.concat([tail,chunk.subarray(0,bytesRead)]); need(!markers.some(marker => marker.length && data.includes(marker))); tail = Buffer.from(data.subarray(Math.max(0,data.length-1024))); }
      } finally { await file?.close(); }
    }
  }
  try { await visit(directory,0); return true; } finally { for (const marker of markers) marker.fill(0); }
}
try {
  await initial; need(process.argv.length === 2); stage = 'BROWSER';
  for (const key of Object.keys(process.env)) if (/^(DEBUG|PWDEBUG|NODE_OPTIONS|NODE_V8_COVERAGE|NODE_INSPECT_RESUME_ON_START)$/i.test(key) || /^(PLAYWRIGHT|PW_TEST).*(DEBUG|LOG|TRACE|REPORT|SCREENSHOT|VIDEO|HAR)/i.test(key)) delete process.env[key];
  process.env.TMP = surface; process.env.TEMP = surface; process.env.TMPDIR = surface;
  const require = createRequire(new URL('../../../web/package.json',import.meta.url));
  const from = dirname(require.resolve('@playwright/test/package.json'));
  const metadata = require.resolve('playwright/package.json',{paths:[from]}); need(require(metadata).version === '1.62.1');
  const {chromium} = require(require.resolve('playwright',{paths:[from]}));
  browser = await chromium.launch({headless:true,timeout:10000,downloadsPath:join(surface,'downloads'),tracesDir:join(surface,'traces')});
  browserVersion = browser.version();
  const core = require.resolve('playwright-core/package.json',{paths:[dirname(metadata)]});
  const expected = require(join(dirname(core),'browsers.json')).browsers.find(value => value.name === 'chromium-headless-shell')?.browserVersion;
  check('BROWSER_ENGINE',browserVersion === expected && /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(browserVersion));
  context = await browser.newContext({acceptDownloads:false,serviceWorkers:'block'});
  const page = await context.newPage(); page.setDefaultTimeout(10000);
  stage = 'REGISTRATION';
  const status = await read(await page.request.get(control.origin+'/browser-testing/status',{timeout:5000}));
  check('NORMAL_HOST_ISOLATED',status.ok && status.body.mode === 'ISOLATED_LOCAL_BROWSER_TESTING' && status.body.instanceId === control.instanceId && status.body.ready === true && !Object.hasOwn(status.body,'journeyId'));
  const email = 'legacy-'+randomUUID()+'@example.test'; const password = 'Synthetic isolated passphrase '+randomUUID(); secrets.push(email,password);
  await page.goto(control.origin+'/registration'); await page.getByRole('heading',{name:'Create your Tunner account',exact:true}).waitFor(); need(await page.locator('input[type=date]').count() === 0);
  await page.getByLabel('Email address',{exact:true}).fill(email); await page.getByLabel('Password',{exact:true}).fill(password); await page.getByLabel('Confirm password',{exact:true}).fill(password); await page.getByRole('checkbox').check();
  const registered = await postResponse(page,'/registration/register',() => page.getByRole('button',{name:'Continue',exact:true}).click()); check('REGISTRATION_CREATED',registered.status === 202);
  await page.getByRole('heading',{name:'Check your email',exact:true}).waitFor();
  let message;
  for (let attempt=0;attempt<80;attempt++) {
    need(true); const inbox = await read(await page.request.get(control.mailpitOrigin+'/api/v1/messages?limit=10',{timeout:3000}));
    if (inbox.ok && Array.isArray(inbox.body.messages)) message = inbox.body.messages.find(value => value.To?.some(address => address.Address === email));
    if (message) break; await new Promise(resolve => setTimeout(resolve,250));
  }
  need(typeof message?.ID === 'string' && /^[A-Za-z0-9-]{1,100}$/.test(message.ID));
  const mail = await read(await page.request.get(control.mailpitOrigin+'/api/v1/message/'+encodeURIComponent(message.ID),{timeout:5000}));
  const code = typeof mail.body.Text === 'string' ? mail.body.Text.match(/code is: (\d{6})/)?.[1] : null; need(mail.ok && typeof code === 'string'); secrets.push(code);
  await page.getByLabel('Verification code',{exact:true}).fill(code); await page.getByRole('button',{name:'Verify email',exact:true}).click(); await page.getByRole('heading',{name:'Personal Information',exact:true}).waitFor();
  check('EMAIL_VERIFIED',await page.getByLabel('Primary email',{exact:true}).inputValue() === email);
  await page.getByLabel('Full name',{exact:true}).fill('Synthetic Legacy Test'); await page.getByLabel('Display name',{exact:true}).fill('Legacy Tester'); await page.getByLabel('Date of birth (required)',{exact:true}).fill('1990-01-01'); await page.getByRole('button',{name:'Continue',exact:true}).click(); await page.getByRole('heading',{name:'Your email is verified',exact:true}).waitFor(); check('PROFILE_COMPLETED',true);
  stage = 'AUTHENTICATION'; await page.goto(control.origin+'/login'); await page.getByRole('heading',{name:'Welcome back',exact:true}).waitFor();
  await page.getByLabel('Email address',{exact:true}).fill(email); await page.getByLabel('Password',{exact:true}).fill(password);
  const signed = await postResponse(page,'/authentication/sign-in',() => page.getByRole('button',{name:'Sign in',exact:true}).click()); await at(page,'/signed-in'); check('PASSWORD_SIGN_IN',signed.status === 200 && signed.body.authenticated === true && signed.body.status !== 'MFA_REQUIRED');
  await page.reload(); await page.getByRole('heading',{name:'You’re signed in',exact:true}).waitFor(); check('SIGNED_IN_RELOAD',true);
  stage = 'STEP_UP'; await page.goto(control.origin+'/browser-testing/step-up'); await page.getByRole('heading',{name:'Identity verification',exact:true}).waitFor();
  const originalUrl = page.url(); const optionsPending = page.waitForResponse(value => new URL(value.url()).origin === control.origin && new URL(value.url()).pathname === '/authentication/step-up/options',{timeout:10000}); optionsPending.catch(() => {});
  await page.getByRole('button',{name:'Verify identity',exact:true}).click(); const options = await read(await optionsPending);
  check('GENERIC_STEP_UP_BOUND',options.ok && options.body.status === 'PENDING' && typeof options.body.operationReference === 'string' && /^LOCAL-VERIFICATION-[a-f0-9]{32}$/.test(options.body.operationReference));
  await page.getByRole('dialog').waitFor(); await page.getByRole('button',{name:'Other method',exact:true}).click(); await page.getByLabel('Password',{exact:true}).fill(password);
  const proof = await postResponse(page,'/authentication/step-up/password',() => page.getByRole('button',{name:'Confirm',exact:true}).click());
  await page.getByRole('dialog').waitFor({state:'hidden'}); await page.getByRole('status').filter({hasText:'Verification completed'}).waitFor();
  check('GENERIC_STEP_UP_COMPLETED',proof.status === 200 && exact(proof.body,['status','signInRequired','operationReference']) && proof.body.status === 'COMPLETED' && proof.body.signInRequired === false && proof.body.operationReference === options.body.operationReference && page.url() === originalUrl);
  let repeatedPosts = 0; page.on('request',value => { if (new URL(value.url()).pathname === '/authentication/step-up/password' && value.method() === 'POST') repeatedPosts++; });
  await page.reload(); await page.getByRole('button',{name:'Verify identity',exact:true}).click(); await page.getByRole('status').filter({hasText:'Verification completed'}).waitFor(); check('COMPLETION_RECOVERY',repeatedPosts === 0 && await page.getByRole('dialog').count() === 0);
  const cookies = await context.cookies(); const user = cookies.find(value => value.name === '__Host-Tunner.User.Session'); const step = cookies.find(value => value.name === '__Host-Tunner.StepUp');
  check('COOKIE_FLAGS',user?.httpOnly === true && user.secure === true && step?.httpOnly === true && step.secure === true && step.sameSite === 'Strict'); check('PRIVATE_BROWSER_STATE',await privateState(page));
  stage = 'LOGOUT'; await page.goto(control.origin+'/signed-in'); await page.getByRole('button',{name:'Sign out',exact:true}).click(); await at(page,'/login'); await page.goto(control.origin+'/signed-in'); await at(page,'/login'); await page.goto(control.origin+'/browser-testing/step-up'); await at(page,'/login'); check('LOGOUT_GUARDS',true);
  stage = 'COMPLETE';
} catch { if (stage === 'COMPLETE') stage = 'BOUNDARY'; }
finally {
  let clean = true;
  try { if (context) await bounded(context.close()); } catch { clean = false; }
  try { if (browser) await bounded(browser.close()); } catch { clean = false; }
  try { surfaceClean = Boolean(surface) && (await scanSurface(surface)).clean && await scanPrivateFiles(surface); } catch { surfaceClean = false; clean = false; }
  cleanupCompleted = clean;
}
const passed = stage === 'COMPLETE' && !fatal && !abort.signal.aborted && cleanupCompleted && surfaceClean && ids.every(id => checks[id] === true) && Object.keys(checks).length === ids.length;
await bounded(send({version:1,type:'legacySmokeResult',passed,stage,checks,browserVersion,surfaceClean,cleanupCompleted}),1000).catch(() => {});
process.disconnect?.(); process.exitCode = passed ? 0 : 1;
