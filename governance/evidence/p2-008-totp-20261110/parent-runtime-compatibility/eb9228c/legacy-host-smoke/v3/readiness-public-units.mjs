// Public-only helper regression. No network, app, account, container or certificate store is used.
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {EventEmitter} from 'node:events';
const source = await readFile(new URL('./run-reviewed-smoke.mjs',import.meta.url),'utf8');
const functionText = source.slice(source.indexOf('async function hostReady('),source.indexOf('async function stopHost('));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const raw = Buffer.from('PUBLIC_NONFUNCTIONAL_CERTIFICATE_BYTES');
const pin = hash(raw).toUpperCase();
const instanceId = '11111111-2222-4333-8444-555555555555';
const fresh = () => ({attempts:0,responseObserved:false,pinMatched:false,http200Observed:false,jsonParsed:false,normalModeObserved:false,expectedInstanceObserved:false,readyObserved:false,metadataMatched:false,requestErrorObserved:false,responseErrorObserved:false,timeoutObserved:false,oversizeRejected:false});
const cases = [
  {id:'DETACHED_SOCKET_AT_END',pin,body:{mode:'ISOLATED_LOCAL_BROWSER_TESTING',instanceId,ready:true},expected:true},
  {id:'ALREADY_CONNECTED_SOCKET',pin,body:{mode:'ISOLATED_LOCAL_BROWSER_TESTING',instanceId,ready:true},expected:true},
  {id:'WRONG_PIN_REJECTED_BEFORE_BODY',pin:'0'.repeat(64),body:{mode:'ISOLATED_LOCAL_BROWSER_TESTING',instanceId,ready:true},expected:false},
  {id:'WRONG_INSTANCE_REJECTED',pin,body:{mode:'ISOLATED_LOCAL_BROWSER_TESTING',instanceId:'00000000-0000-4000-8000-000000000000',ready:true},expected:false},
  {id:'OVERSIZE_STATUS_REJECTED',pin,body:'x'.repeat(4097),expected:false}
];
const results = [];
for (const fixture of cases) {
  const state = fresh(); let freshSocket = false; let bodySubscribed = false; let listenerAttempted = false;
  const https = {get(_url,options,callback) {
    freshSocket = options.agent === false;
    const request = new EventEmitter(); request.destroy = () => {};
    const originalOn = request.on.bind(request);
    request.on = (name,listener) => { if (name === 'socket') listenerAttempted = true; return originalOn(name,listener); };
    queueMicrotask(() => {
      const response = new EventEmitter(); response.statusCode = 200;
      response.socket = {connecting:false,encrypted:true,getPeerCertificate:() => ({raw})}; response.destroy = () => {};
      const original = response.on.bind(response); response.on = (name,listener) => { if (name === 'data') bodySubscribed = true; return original(name,listener); };
      callback(response);
      response.emit('data',Buffer.from(typeof fixture.body === 'string' ? fixture.body : JSON.stringify(fixture.body)));
      response.socket = null; // Node may detach before end; pin verification must already be complete.
      response.emit('end');
    });
    return request;
  }};
  let observed = false; let threw = false;
  try { const ready = new Function('https','digest','instanceId','readiness','return '+functionText)(https,hash,instanceId,state); observed = await ready('https://localhost:24443',fixture.pin); } catch { threw = true; }
  const passed = !threw && observed === fixture.expected && freshSocket && !listenerAttempted &&
    (fixture.id !== 'WRONG_PIN_REJECTED_BEFORE_BODY' || !bodySubscribed && !state.jsonParsed) &&
    (fixture.id !== 'OVERSIZE_STATUS_REJECTED' || state.oversizeRejected && !state.jsonParsed);
  results.push({id:fixture.id,passed});
}
const passed = results.every(value => value.passed);
const receipt = {schemaVersion:1,type:'PUBLIC_READINESS_HELPER_UNITS',passed,total:results.length,results,helperSha256:hash(Buffer.from(source)),boundary:'Public socket lifecycle mocks only. No actual HTTPS/app/ordinary journey or readiness PASS claim.'};
await writeFile(new URL('./readiness-public-unit-receipt.json',import.meta.url),JSON.stringify(receipt,null,2)+'\n');
process.stdout.write(JSON.stringify(receipt)+'\n'); process.exitCode = passed ? 0 : 1;
