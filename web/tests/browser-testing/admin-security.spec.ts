import {expect,test} from '@playwright/test';
import {spawn} from 'node:child_process';
import type {ChildProcess} from 'node:child_process';
import {existsSync,lstatSync,mkdirSync,mkdtempSync,readFileSync,readdirSync,realpathSync,rmSync,writeFileSync} from 'node:fs';
import {request as httpsRequest} from 'node:https';
import {createServer} from 'node:net';
import type {Server} from 'node:net';
import {basename,join,relative,resolve,sep} from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomUUID} from 'node:crypto';

// Real trusted TLS and in-memory credential handling only. Never use Playwright's API error/header dump for these requests.
test.use({ignoreHTTPSErrors:false});
const repository=fileURLToPath(new URL('../../../',import.meta.url));
const runtime=JSON.parse(readFileSync(join(repository,'artifacts/browser-testing/runtime.json'),'utf8').replace(/^\uFEFF/,'')) as {
  Mode:string;ApplicationPort:number;AdminPort:number;PostgresPort:number;SmtpPort:number;CertificateThumbprint:string;GovernedContent:boolean;SyntheticAdmin:boolean;
};
const adminOrigin='https://localhost:'+runtime.AdminPort;
const userOrigin='https://localhost:'+runtime.ApplicationPort;
const adminCookie='__Host-Tunner.Admin.Session';
const userCookie='__Host-Tunner.User.Session';
const csrfHeader='X-Tunner-CSRF';
type LocalResponse={status:number;headers:Headers;body:string};

class CookieJar{
  private readonly values=new Map<string,string>();
  accept(headers:Headers){for(const header of headers.getSetCookie()){
    const pair=header.split(';',1)[0]??'';const split=pair.indexOf('=');
    if(split>0)this.values.set(pair.slice(0,split),pair.slice(split+1));
  }}
  header(){return [...this.values].map(([name,value])=>name+'='+value).join('; ');}
  pair(name:string){const value=this.values.get(name);return value===undefined?'':name+'='+value;}
  renamed(from:string,to:string){const value=this.values.get(from);return value===undefined?'':to+'='+value;}
}
function requireFixture(){
  expect(runtime.Mode==='ISOLATED_LOCAL_BROWSER_TESTING'&&runtime.GovernedContent===true&&runtime.SyntheticAdmin===true,'explicit live isolated fixture required').toBe(true);
  expect(process.env.NODE_USE_SYSTEM_CA==='1'||process.env.NODE_OPTIONS?.includes('--use-system-ca')===true,'trusted system CA mode required').toBe(true);
}
async function local(url:string,options:RequestInit={}):Promise<LocalResponse>{
  try{
    const response=await fetch(url,{...options,redirect:'manual',signal:AbortSignal.timeout(10000)});
    return {status:response.status,headers:response.headers,body:await response.text()};
  }catch{throw new Error('Isolated fixture HTTPS request failed.');}
}
function issued(headers:Headers){return headers.getSetCookie().some(value=>value.startsWith(adminCookie+'='));}
async function bootstrap(jar:CookieJar){
  const response=await local(adminOrigin+'/browser-testing/admin',{headers:{Cookie:jar.header()}});
  expect(response.status).toBe(200);expect(issued(response.headers),'GET must not issue an Admin session').toBe(false);jar.accept(response.headers);
  const token=response.body.match(/name="tunner-csrf" content="([^"]+)"/)?.[1];
  expect(Boolean(token),'bootstrap must issue only a CSRF token').toBe(true);
  return token!;
}
async function session(jar:CookieJar,token:string,body:string,origin:string|null=adminOrigin,type='application/json'){
  const headers:Record<string,string>={Cookie:jar.header(),'Content-Type':type};
  if(origin!==null)headers.Origin=origin;if(token)headers[csrfHeader]=token;
  return local(adminOrigin+'/browser-testing/admin/session',{method:'POST',headers,body});
}

test('Synthetic Admin bootstrap and actor admission reject unsafe Origin, CSRF and bounded JSON without issuing authority',async()=>{
  test.setTimeout(90000);requireFixture();const jar=new CookieJar();const token=await bootstrap(jar);
  const anonymous=await local(adminOrigin+'/content/workspace',{headers:{Cookie:jar.header()}});expect(anonymous.status).toBe(401);
  const foreign=await local(userOrigin+'/authentication/options');expect(foreign.status).toBe(200);
  let foreignToken='';try{const body=JSON.parse(foreign.body) as {csrfToken?:unknown};if(typeof body.csrfToken==='string')foreignToken=body.csrfToken;}catch{throw new Error('User bootstrap metadata was unavailable.');}
  expect(Boolean(foreignToken)).toBe(true);
  const invalid:Array<{body:string;token?:string;origin?:string|null;type?:string;status:number}>=[
    {body:'{"actor":"author"}',origin:null,status:403},
    {body:'{"actor":"author"}',origin:userOrigin,status:403},
    {body:'{"actor":"author"}',token:'',status:400},
    {body:'{"actor":"author"}',token:foreignToken,status:400},
    {body:'{"actor":"author"}',type:'text/plain',status:415},
    {body:'{"actor":"unknown"}',status:400},
    {body:'{"actor":"Author"}',status:400},
    {body:'{"actor":"author","extra":true}',status:400},
    {body:'{"actor":"author","actor":"publisher"}',status:400},
    {body:'{"actor":7}',status:400},
    {body:'["author"]',status:400},
    {body:'{"actor":"'+ 'a'.repeat(4096)+'"}',status:413}
  ];
  for(const row of invalid){
    const response=await session(jar,row.token??token,row.body,row.origin===null?null:row.origin??adminOrigin,row.type??'application/json');
    expect(response.status,'unsafe actor request rejected with bounded status').toBe(row.status);
    expect(issued(response.headers),'rejection must not issue an Admin authentication cookie').toBe(false);
  }
  const valid=await session(jar,token,'{"actor":"author"}');expect(valid.status).toBe(204);expect(issued(valid.headers)).toBe(true);jar.accept(valid.headers);
  expect((await local(adminOrigin+'/content/workspace',{headers:{Cookie:jar.header()}})).status).toBe(200);
});

async function wrongHostStatus(){
  return new Promise<number>((resolveStatus,reject)=>{
    const request=httpsRequest(adminOrigin+'/browser-testing/admin',{headers:{Host:'127.0.0.1:'+runtime.AdminPort},timeout:10000},response=>{
      response.resume();response.once('end',()=>resolveStatus(response.statusCode??0));
    });
    request.once('error',()=>reject(new Error('Public fixture Host-boundary request failed.')));
    request.once('timeout',()=>request.destroy(new Error('Public fixture Host-boundary timeout.')));request.end();
  });
}
test('Synthetic Admin cookie remains surface-bound and actor switching invalidates the previous server session',async()=>{
  test.setTimeout(90000);requireFixture();const jar=new CookieJar();let token=await bootstrap(jar);
  const signed=await session(jar,token,'{"actor":"author"}');expect(signed.status).toBe(204);jar.accept(signed.headers);
  const previous=jar.pair(adminCookie);expect(Boolean(previous)).toBe(true);
  expect((await local(userOrigin+'/authentication/session',{headers:{Cookie:previous}})).status).toBe(401);
  expect((await local(userOrigin+'/authentication/session',{headers:{Cookie:jar.renamed(adminCookie,userCookie)}})).status).toBe(401);
  token=await bootstrap(jar);const denied=await session(jar,'invalid-csrf','{"actor":"approver"}');expect(denied.status).toBe(400);expect(issued(denied.headers)).toBe(false);
  expect((await local(adminOrigin+'/content/workspace',{headers:{Cookie:previous}})).status).toBe(200);
  const ended=await session(jar,token,'{"actor":"approver"}');expect(ended.status).toBe(202);jar.accept(ended.headers);
  expect((await local(adminOrigin+'/content/workspace',{headers:{Cookie:previous}})).status).toBe(401);
  token=await bootstrap(jar);const switched=await session(jar,token,'{"actor":"approver"}');expect(switched.status).toBe(204);jar.accept(switched.headers);
  expect((await local(adminOrigin+'/content/workspace',{headers:{Cookie:previous}})).status).toBe(401);
  expect((await local(adminOrigin+'/content/workspace',{headers:{Cookie:jar.header()}})).status).toBe(200);
  expect(await wrongHostStatus()).toBe(403);
  expect((await local(adminOrigin+'/browser-testing/admin?unexpected=1')).status).toBe(400);
  for(const path of ['/browser-testing/admin/theme/fixture.js','/browser-testing/admin/theme/fixture.css.map','/browser-testing/admin/theme/../Program.cs']){
    const response=await local(adminOrigin+path);expect(response.status>=400,'arbitrary fixture assets must not be served').toBe(true);
  }
});

type OwnedChild={child:ChildProcess;closed:Promise<number|null>;isClosed:()=>boolean};
function startChild(assembly:string,config:string,environment:'Production'|'Development'):OwnedChild{
  let closed=false;
  const child=spawn('dotnet',[assembly,'--local-browser-testing',config],{cwd:repository,windowsHide:true,stdio:'ignore',
    env:{...process.env,ASPNETCORE_ENVIRONMENT:environment,DOTNET_ENVIRONMENT:environment}});
  const terminal=new Promise<number|null>((resolveCode,reject)=>{
    child.once('error',()=>{closed=true;reject(new Error('Owned public fixture process could not start.'));});
    child.once('close',code=>{closed=true;resolveCode(code);});
  });
  void terminal.catch(()=>undefined); // Retain the terminal error for the bounded owner wait without an unhandled rejection during startup polling.
  return {child,closed:terminal,isClosed:()=>closed};
}
async function bounded<T>(value:Promise<T>,milliseconds:number):Promise<T>{
  let timer:NodeJS.Timeout|undefined;
  try{return await Promise.race([value,new Promise<never>((_,reject)=>{timer=setTimeout(()=>reject(new Error('Owned public fixture process exceeded its bound.')),milliseconds);})]);}
  finally{if(timer!==undefined)clearTimeout(timer);}
}
async function reserve(){
  const server=createServer(socket=>socket.destroy());await new Promise<void>((resolveReady,reject)=>{server.once('error',()=>reject(new Error('Owned loopback reservation failed.')));server.listen(0,'127.0.0.1',resolveReady);});
  const address=server.address();if(address===null||typeof address==='string')throw new Error('Owned loopback port unavailable.');
  return {server,port:address.port};
}
async function closeServer(server:Server){await new Promise<void>((resolveClosed,reject)=>server.close(error=>error?reject(new Error('Owned loopback reservation could not close.')):resolveClosed()));}
async function stopOwned(child:OwnedChild){
  if(!child.isClosed())child.child.kill();await bounded(child.closed,10000);expect(child.isClosed(),'owned child must be closed before scratch cleanup').toBe(true);
}

test('Synthetic Admin Production is refused before config IO and legacy governed mode does not activate a saved Admin port',async()=>{
  test.setTimeout(150000);requireFixture();
  const assembly=resolve(repository,'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll');
  const assemblyRelative=relative(repository,assembly);expect(!assemblyRelative.startsWith('..')&&basename(assembly)==='Tunner.BrowserTesting.dll'&&existsSync(assembly),'same compiled public fixture tool required').toBe(true);
  const parent=resolve(repository,'artifacts/browser-testing');mkdirSync(parent,{recursive:true});const scratch=mkdtempSync(join(parent,'admin-security-'));
  const canonicalParent=realpathSync(parent);const ownedRelative=relative(canonicalParent,realpathSync(scratch));expect(ownedRelative.startsWith('admin-security-')&&!ownedRelative.includes(sep)&&!lstatSync(scratch).isSymbolicLink()).toBe(true);
  let child:OwnedChild|undefined;let adminReservation:Awaited<ReturnType<typeof reserve>>|undefined;let userReservation:Awaited<ReturnType<typeof reserve>>|undefined;
  try{
    const sentinel=join(scratch,'production-refusal.json');const invalidConfig='not a readable fixture configuration';writeFileSync(sentinel,invalidConfig);
    child=startChild(assembly,sentinel,'Production');expect(await bounded(child.closed,10000)).toBe(2);
    expect(readFileSync(sentinel,'utf8')===invalidConfig,'Production leaves the sentinel unchanged').toBe(true);
    expect(readdirSync(scratch).length,'Production creates no startup/error markers or keys').toBe(1);child=undefined;
    adminReservation=await reserve();userReservation=await reserve();const userPort=userReservation.port;let adminConnections=0;
    adminReservation.server.on('connection',()=>adminConnections++);await closeServer(userReservation.server);userReservation=undefined;
    const config=join(scratch,'legacy-runtime.json');writeFileSync(config,JSON.stringify({Mode:runtime.Mode,InstanceId:randomUUID(),ApplicationPort:userPort,
      PostgresPort:runtime.PostgresPort,SmtpPort:runtime.SmtpPort,CertificateThumbprint:runtime.CertificateThumbprint,GovernedContent:true,AdminPort:adminReservation.port}));
    child=startChild(assembly,config,'Development');const origin='https://localhost:'+userPort;const deadline=Date.now()+90000;let ready=false;
    while(Date.now()<deadline&&!child.isClosed()){
      try{
        const response=await local(origin+'/browser-testing/status');
        if(response.status===200){const status=JSON.parse(response.body) as {ready?:unknown;governedContent?:unknown};if(status.ready===true&&status.governedContent===true){ready=true;break;}}
      }catch{ /* Startup only: no request/body/cookie diagnostics. */ }
      await new Promise(resolvePoll=>setTimeout(resolvePoll,250));
    }
    expect(ready,'legacy governed User host must become ready while the selected Admin port stays reserved').toBe(true);
    const options=await local(origin+'/authentication/options');expect(options.status).toBe(200);
    const metadata=JSON.parse(options.body) as {content?:Record<string,{publicationRef?:unknown}>};
    expect(Object.values(metadata.content??{}).length===62&&Object.values(metadata.content??{}).every(text=>typeof text.publicationRef==='string'),'legacy mode keeps all real governed Login publications').toBe(true);
    expect(adminReservation.server.listening,'legacy default must not bind the reserved Admin port').toBe(true);expect(adminConnections).toBe(0);
  }finally{
    let closed=child===undefined;
    try{if(child!==undefined){await stopOwned(child);closed=true;}}
    finally{
      if(userReservation!==undefined)await closeServer(userReservation.server);if(adminReservation!==undefined)await closeServer(adminReservation.server);
      // Never delete DP/network key files until the exact child handle is terminal, and never leave the new owned directory.
      if(closed&&realpathSync(scratch)===resolve(canonicalParent,ownedRelative)&&ownedRelative.startsWith('admin-security-')&&!ownedRelative.includes(sep)&&!lstatSync(scratch).isSymbolicLink())rmSync(scratch,{recursive:true,force:true});
    }
  }
});
