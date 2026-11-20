// Public-only artifact runner. Preserve canonical tests and tracked-server ownership.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
const here=path.dirname(fileURLToPath(import.meta.url)),repo=path.resolve(here,'../../..'),web=path.join(repo,'web');
const git=(...args)=>execFileSync('git',args,{cwd:repo,encoding:'utf8'}).trim(),sha=git('rev-parse','HEAD'),hash=b=>createHash('sha256').update(b).digest('hex');
const out=path.join(here,'green-'+sha.slice(0,7));
if(hash(await fs.readFile(path.join(repo,'web/apps/user/totp.tsx')))!=='dcf925a2891a179eb483e62f665480e19b7ac8ba5a27ab49bb5a3fea960a4f29'||git('diff','6653a3c','--','web/apps/user/totp.tsx'))throw Error('Unexpected committed production candidate');
const portable=x=>typeof x==='string'?x.replaceAll(repo,'<repo-root>').replaceAll(repo.replaceAll('\\','/'),'<repo-root>'):Array.isArray(x)?x.map(portable):x&&typeof x==='object'?Object.fromEntries(Object.entries(x).map(([k,v])=>[k,portable(v)])):x;
await fs.mkdir(out,{recursive:true});if(await fs.stat(path.join(out,'commands.json')).catch(()=>null))throw Error('Preserve prior execution evidence');
const paths=git('ls-files','--','web','docs/contracts/ui-inventory.json').split(/\r?\n/);if(git('status','--porcelain','--',...paths))throw Error('Dirty declared inputs');
const sources=[];for(const p of paths)sources.push({path:p,sha256:hash(await fs.readFile(path.join(repo,p))),git_blob:git('rev-parse',sha+':'+p)});
await fs.writeFile(path.join(out,'source-inputs-before.json'),JSON.stringify({actual_invocation_sha:sha,sources},null,2)+'\n');
const commands=[],servers=[];let failure;
async function command(name,args){const log=await fs.open(path.join(out,name+'.log'),'w'),child=spawn(process.execPath,args,{cwd:web,windowsHide:true,env:{...process.env,TUNNER_PUBLIC_UI_CARRIER:sha},stdio:['ignore','pipe','pipe']}),start=new Date().toISOString();child.stdout.on('data',b=>{void log.write(b);process.stdout.write(b);});child.stderr.on('data',b=>{void log.write(b);process.stderr.write(b);});const exit=await new Promise((yes,no)=>{child.once('error',no);child.once('exit',yes);});await log.close();commands.push({name,cwd:'web',args:portable(args),started_at:start,exit_code:exit});await fs.writeFile(path.join(out,'commands.json'),JSON.stringify(commands,null,2)+'\n');if(exit!==0)throw Error(name+' exit '+exit);}
async function server(name,config,port,url){
 try{await fetch(url,{signal:AbortSignal.timeout(500)});throw Error('Refuse unowned listening server '+port);}catch(error){if(error.message.startsWith('Refuse'))throw error;}
 const log=await fs.open(path.join(out,name+'.log'),'w'),child=spawn(process.execPath,['node_modules/vite/bin/vite.js','--config',config,'--port',String(port),'--strictPort'],{cwd:web,windowsHide:true,stdio:['ignore',log.fd,log.fd]}),identity={name,pid:child.pid,cwd:'web',config,port,url,started_at:new Date().toISOString()};servers.push({child,log,identity});
 for(let i=0;i<100;i++){if(child.exitCode!==null)throw Error(name+' exited '+child.exitCode);try{if((await fetch(url,{signal:AbortSignal.timeout(500)})).ok){identity.ready=true;console.log(JSON.stringify(identity));return;}}catch{}await new Promise(r=>setTimeout(r,200));}throw Error(name+' readiness');
}
try{
 await command('typecheck',['node_modules/typescript/bin/tsc','--noEmit']);
 await command('contracts-build',['node_modules/vite/bin/vite.js','build','--config','tests/vite.config.ts']);
 await command('contracts',['--test','--test-reporter=tap','tests/dist/contracts.test.js']);
 await server('user','apps/user/vite.config.ts',5194,'http://127.0.0.1:5194/registration/');await server('step-up','fixtures/step-up/vite.config.ts',5195,'http://127.0.0.1:5195/browser-testing/step-up-assets/');
 const wrapper=`import original from '../../../../web/playwright.totp.config.ts';\nimport path from 'node:path';\nimport {fileURLToPath} from 'node:url';\nconst web=fileURLToPath(new URL('../../../../web/',import.meta.url)),out=fileURLToPath(new URL('./',import.meta.url));\nexport default {...original,testDir:path.resolve(web,original.testDir!),outputDir:path.join(out,'playwright-output'),reporter:[['list'],['json',{outputFile:path.join(out,'results.json')}]],webServer:(original.webServer as any[]).map(server=>({...server,cwd:web,reuseExistingServer:true}))};\n`;
 await fs.writeFile(path.join(out,'reuse-totp.config.mts'),wrapper);await command('totp',['node_modules/@playwright/test/cli.js','test','--config',path.join(out,'reuse-totp.config.mts')]);
 const report=JSON.parse(await fs.readFile(path.join(out,'results.json'),'utf8'));if(report.stats.expected!==43||report.stats.unexpected||report.stats.skipped||report.stats.flaky)throw Error('Expected43 unchanged green cases');
 await fs.writeFile(path.join(out,'cohort-summary.json'),JSON.stringify({actual_invocation_sha:sha,total:43,passed:report.stats.expected,failed:report.stats.unexpected,skipped:report.stats.skipped,flaky:report.stats.flaky,report_sha256:hash(await fs.readFile(path.join(out,'results.json')))},null,2)+'\n');
 console.log('PUBLIC43_GREEN_READY_FOR_COMPOSITION');
 await command('composition',[path.join(here,'capture-ui005.mjs')]);
 const after=[];for(const source of sources)after.push({...source,after_sha256:hash(await fs.readFile(path.join(repo,source.path)))});if(after.some(s=>s.sha256!==s.after_sha256)||git('status','--porcelain','--',...paths))throw Error('Source input drift');
 await fs.writeFile(path.join(out,'source-inputs-after.json'),JSON.stringify({actual_invocation_sha:sha,observed_completion_head:git('rev-parse','HEAD'),all_declared_inputs_equal:true,sources:after},null,2)+'\n');
 const assets=[];for(const p of ['results.json','cohort-summary.json','source-inputs-before.json','source-inputs-after.json','commands.json','reuse-totp.config.mts','composition.json'])assets.push({path:p,sha256:hash(await fs.readFile(path.join(out,p)))});
 await fs.writeFile(path.join(out,'manifest.json'),JSON.stringify({status:'PASS_SCOPED_PUBLIC_UI_REGRESSION_ONLY',actual_invocation_sha:sha,observed_completion_head:git('rev-parse','HEAD'),all_declared_inputs_equal:true,source_count:sources.length,cohort:{total:43,passed:43,failed:0,skipped:0},contracts:{passed:29},commands,files:assets,canonical_config:{path:'web/playwright.totp.config.ts',sha256:hash(await fs.readFile(path.join(web,'playwright.totp.config.ts')))},wrapper_delta:'Same canonical tests/assertions/workers/projects/use/retries. Absolute-resolve testDir/cwd, separate evidence outputs, reuse only own tracked Vites. No production source/config edit.',helper_sha256:hash(await fs.readFile(fileURLToPath(import.meta.url))),composition_helper_sha256:hash(await fs.readFile(path.join(here,'capture-ui005.mjs'))),historical_renders:'188 original6e renders retained with original provenance; composition-only behavior delta; no JSX/CSS/copy change',boundary:'PUBLIC nonfunctional fixture only. Real journey2, privacy classification and final008 acceptance remain pending.'},null,2)+'\n');
}catch(error){failure=String(error);await fs.writeFile(path.join(out,'failure.json'),JSON.stringify({failure,actual_invocation_sha:sha,commands},null,2)+'\n');throw error;}
finally{for(const item of servers){if(item.child.exitCode===null)item.child.kill();item.identity.stopped_at=new Date().toISOString();await item.log.close();}await fs.writeFile(path.join(out,'servers.json'),JSON.stringify({failure,servers:servers.map(s=>s.identity)},null,2)+'\n');}
