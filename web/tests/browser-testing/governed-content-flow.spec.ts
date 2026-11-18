import {test,expect,type Page,type Response} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync} from 'node:fs';
import {randomUUID} from 'node:crypto';
import type {Proposal,Publication} from '../../apps/admin/content.api';
import {scopeKey,type Scope,type Publication as IdentityPublication,type Proposal as IdentityProposal,type Workspace as IdentityWorkspace} from '../../apps/admin/configuration.api';
import {configurationFamilies,configurationPath} from '../../apps/admin/configuration.routes';

// Real isolated hosts only: no request interception, credentials or saved browser state.
test.use({ignoreHTTPSErrors:false});
const state=JSON.parse(readFileSync(new URL('../../../artifacts/browser-testing/runtime.json',import.meta.url),'utf8').replace(/^\uFEFF/,'')) as {ApplicationPort:number;AdminPort?:number;Mode:string;GovernedContent?:boolean;SyntheticAdmin?:boolean;SyntheticIdentityConfiguration?:boolean};
const scope='synthetic.local.identity.login',variant='synthetic-local-en',key='login.heading';
const userOrigin='https://localhost:'+state.ApplicationPort,adminOrigin='https://localhost:'+state.AdminPort;
const reference=(value:string)=>value.replaceAll('-','').toLowerCase();
const heading=(page:Page)=>page.locator('.authentication-intro h1 [data-content-key="login.heading"]');

async function actor(page:Page,name:'author'|'approver'|'publisher'){
 await page.goto(adminOrigin+'/browser-testing/admin');
 const signed=page.waitForResponse(response=>new URL(response.url()).pathname==='/browser-testing/admin/session'&&response.request().method()==='POST'&&response.status()===204);
 await page.getByRole('button',{name:'Use synthetic '+name,exact:true}).click();
 expect((await signed).status()).toBe(204);
 await expect(page).toHaveURL(adminOrigin+'/admin/content');
 await expect(page.getByRole('heading',{level:1,name:'Content workspace',exact:true})).toBeVisible();
}
async function select(page:Page,proposalReference?:string){
 await page.goto(adminOrigin+'/admin/content');
 await page.getByLabel('Surface',{exact:true}).selectOption(scope);
 await page.getByLabel('Search',{exact:true}).fill(key);
 await page.getByRole('button',{name:'Search',exact:true}).click();
 await page.locator('.content-library button').filter({has:page.locator('span').filter({hasText:/^login\.heading$/})}).click();
 await page.getByLabel('Context',{exact:true}).selectOption(variant);
 const candidates=page.getByLabel('Candidate',{exact:true});await expect(candidates).toBeEnabled();
 if(proposalReference)await candidates.selectOption(proposalReference);
 await expect(page.locator('main[aria-busy="false"]')).toBeVisible();
}
async function result<T>(response:Promise<Response>):Promise<T>{const actual=await response;expect(actual.status()).toBe(200);return (await actual.json() as {result:T}).result;}
const command=(page:Page,path:string)=>page.waitForResponse(response=>new URL(response.url()).pathname===path&&response.request().method()==='POST');
async function history(page:Page):Promise<Publication[]>{
 // CSRF stays inside the page. Only immutable public wording/publication metadata leaves it.
 return page.evaluate(async target=>{
  const workspace=await fetch('/content/workspace',{credentials:'same-origin',cache:'no-store'});if(!workspace.ok)throw new Error('Content workspace unavailable');
  const boot=await workspace.json() as {csrfToken:string;workspaces:{scope:string;maximumPageSize:number}[]},selected=boot.workspaces.find(item=>item.scope===target.scope);if(!selected)throw new Error('Content scope unavailable');
  const response=await fetch('/content/history',{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify({...target,pageSize:selected.maximumPageSize,offset:0})});
  if(!response.ok)throw new Error('Content history unavailable');return (await response.json() as {items:Publication[]}).items;
 },{scope,key,variantReference:variant});
}
async function forbidden(page:Page,path:'/content/proposals/approve'|'/content/proposals/publish',proposal:Proposal,head:Publication){
 const status=await page.evaluate(async target=>{
  const workspace=await fetch('/content/workspace',{credentials:'same-origin',cache:'no-store'});if(!workspace.ok)return workspace.status;
  const boot=await workspace.json() as {csrfToken:string};
  const body={idempotencyKey:crypto.randomUUID(),proposalReference:target.proposalReference,expectedRevision:target.expectedRevision,...(target.path.endsWith('/publish')?{effectiveInterval:{from:new Date(Date.now()+10_000).toISOString(),includesFrom:true,until:null,includesUntil:false},supersedesPublicationReference:target.head}: {})};
  return (await fetch(target.path,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify(body)})).status;
 },{path,proposalReference:proposal.proposalReference,expectedRevision:proposal.revision,head:head.publicationReference});
 expect(status).toBe(403);
}
async function interval(page:Page){
 const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();
 // A test-selected future instant, not an owner policy/default or altered host clock.
 await dialog.getByLabel('Effective from',{exact:true}).fill(new Date(Date.now()+10_000).toISOString().slice(0,19));
 await dialog.getByLabel('Start boundary',{exact:true}).selectOption('inclusive');
 await dialog.getByLabel('End condition',{exact:true}).selectOption('none');
}
async function userPublication(page:Page,text:string,publication:Publication){
 await expect.poll(async()=>{await page.goto(userOrigin+'/login');return heading(page).textContent();},{timeout:20_000}).toBe(text);
 const actual=heading(page);await expect(actual).toHaveAttribute('data-content-key',key);await expect(actual).toHaveAttribute('data-content-variant',variant);
 expect(reference((await actual.getAttribute('data-content-publication'))!)).toBe(reference(publication.publicationReference));
 expect(reference((await actual.getAttribute('data-content-version'))!)).toBe(reference(publication.versionReference));
 expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});
}

async function identityActor(page:Page,name:'author'|'approver'|'publisher'){
 await page.goto(adminOrigin+'/browser-testing/admin');
 const admitted=page.waitForResponse(response=>new URL(response.url()).pathname==='/browser-testing/admin/session'&&response.request().method()==='POST'&&response.status()===204);
 await page.getByRole('button',{name:'Use Identity '+name,exact:true}).click();expect((await admitted).status()).toBe(204);
 await expect(page).toHaveURL(adminOrigin+configurationPath('Authentication'));
 await expect(page.getByRole('heading',{level:1,name:'Password & session policy',exact:true})).toBeVisible();
}
async function identityRead<T>(page:Page,path:string,body?:Record<string,unknown>):Promise<T>{
 // The real browser owns its cookies and CSRF. Never export either into a report or assertion.
 const response=await page.evaluate(async target=>{
  const workspace=await fetch('/identity/configuration/workspace',{credentials:'same-origin',cache:'no-store'});
  if(!workspace.ok)return {status:workspace.status,value:null};const boot=await workspace.json() as {csrfToken:string};
  const result=await fetch('/identity/configuration'+target.path,{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify(target.body)});
  return {status:result.status,value:result.ok?await result.json():null};
 },{path,body:body??{}});expect(response.status).toBe(200);return response.value as T;
}
async function identityDenied(page:Page,path:string,body:Record<string,unknown>){
 const status=await page.evaluate(async target=>{
  const workspace=await fetch('/identity/configuration/workspace',{credentials:'same-origin',cache:'no-store'});if(!workspace.ok)return workspace.status;
  const boot=await workspace.json() as {csrfToken:string};return (await fetch('/identity/configuration'+target.path,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-Tunner-CSRF':boot.csrfToken},body:JSON.stringify(target.body)})).status;
 },{path,body});expect(status).toBe(403);
}
async function identitySelect(page:Page,target:Scope,proposal?:IdentityProposal){
 await page.goto(adminOrigin+configurationPath(target.family));await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(target));
 const candidates=page.getByLabel('Saved candidate',{exact:true});await expect(candidates).toBeEnabled();
 if(proposal){await page.getByLabel('Search candidates',{exact:true}).fill(proposal.policyVersionReference);await page.getByRole('button',{name:'Search candidates',exact:true}).click();await expect(candidates.locator('option[value="'+proposal.proposalReference+'"]')).toHaveCount(1);await candidates.selectOption(proposal.proposalReference);}
 await expect(page.locator('main[aria-busy="false"]')).toBeVisible();
 if(proposal)await expect(page.locator('[name="version"]')).toHaveValue(proposal.policyVersionReference);
}
async function identityHistory(page:Page,target:Scope){return (await identityRead<{items:IdentityPublication[]}>(page,'/history',{scope:target,pageSize:5,offset:0})).items;}
async function identityInterval(page:Page,from:string){
 const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.getByLabel('Available from · UTC',{exact:true}).fill(from);
 await dialog.getByLabel('Start boundary',{exact:true}).selectOption('inclusive');await dialog.getByLabel('End condition',{exact:true}).selectOption('none');
}

test('Synthetic Identity sixteen governed workspaces preserve separate actors and complete approved typed publication and prospective restore',async({browser},info)=>{
 test.setTimeout(180_000);expect(state.SyntheticIdentityConfiguration).toBe(true);expect(state.GovernedContent).toBe(true);expect(state.SyntheticAdmin).toBe(true);
 const contexts=await Promise.all(Array.from({length:4},()=>browser.newContext({ignoreHTTPSErrors:false,viewport:{width:1440,height:1024}})));
 try{
  const [author,approver,publisher,user]=await Promise.all(contexts.map(context=>context.newPage()));if(!author||!approver||!publisher||!user)throw new Error('Four isolated browser contexts required');
  await user.goto(userOrigin+'/login');const login=await heading(user).evaluate(node=>({text:node.textContent,version:node.getAttribute('data-content-version'),publication:node.getAttribute('data-content-publication')}));expect(login.text).toBeTruthy();
  await author.goto(adminOrigin+'/browser-testing/admin');await expect(author.locator('[data-actor]')).toHaveCount(6);await expect(author.locator('[data-identity="true"]')).toHaveCount(3);await expect(author.locator('[data-actor="author"],[data-actor="approver"],[data-actor="publisher"]')).toHaveCount(3);
  for(const width of [1440,768,320]){await author.setViewportSize({width,height:1024});expect(await author.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);for(const button of await author.locator('[data-actor]').all())await expect(button).toBeEnabled();if(width===768){expect((await new AxeBuilder({page:author}).analyze()).violations).toEqual([]);await author.screenshot({path:info.outputPath('synthetic-identity-bootstrap-768.png'),fullPage:true});}}
  await author.setViewportSize({width:1440,height:1024});await identityActor(author,'author');await identityActor(approver,'approver');await identityActor(publisher,'publisher');
  const bootstrap=await author.evaluate(async()=>{const response=await fetch('/identity/configuration/workspace',{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw new Error('Identity workspace unavailable');const {workspaces,presentation}=await response.json() as {workspaces:IdentityWorkspace[];presentation:{content:Record<string,{key:string;versionReference:string;publicationReference:string;variantReference:string;text:string}>}|null};return {workspaces,presentation};});
  expect(bootstrap.workspaces.map(item=>item.scope.family).sort()).toEqual([...configurationFamilies].sort());expect(new Set(bootstrap.workspaces.map(item=>scopeKey(item.scope))).size).toBe(16);expect(bootstrap.presentation).not.toBeNull();
  const chrome=Object.entries(bootstrap.presentation!.content);expect(chrome.length).toBeGreaterThan(0);for(const [key,value] of chrome){expect(value.key).toBe(key);expect(value.versionReference).toMatch(/^[0-9a-f-]{32,36}$/i);expect(value.publicationReference).toMatch(/^[0-9a-f-]{32,36}$/i);expect(value.variantReference).toBe('synthetic-local-en');expect(value.text.length).toBeGreaterThan(0);}
  for(const workspace of bootstrap.workspaces){
   expect(workspace).toMatchObject({separateAuthorApprover:true,separateAuthorPublisher:true,separateApproverPublisher:true});await identitySelect(author,workspace.scope);
   const contentKey='admin.identity.configuration.'+workspace.scope.family,published=bootstrap.presentation!.content[contentKey]!;const title=author.locator('h1 [data-content-key="'+contentKey+'"]');await expect(title).toHaveText(published.text);expect(reference((await title.getAttribute('data-content-publication'))!)).toBe(reference(published.publicationReference));expect(reference((await title.getAttribute('data-content-version'))!)).toBe(reference(published.versionReference));
   const cap=await identityRead<{scope:Scope;availableActions:string[]}>(author,'/capabilities',{scope:workspace.scope,target:'Workspace'});expect(scopeKey(cap.scope)).toBe(scopeKey(workspace.scope));expect(cap.availableActions).toContain('Browse');expect(cap.availableActions).toContain('CreateDraft');
   const page=await identityRead<{items:IdentityProposal[]}>(author,'/proposals/search',{scope:workspace.scope,pageSize:workspace.maximumPageSize,offset:0});expect(page.items.length).toBeLessThanOrEqual(workspace.maximumPageSize);for(const proposal of page.items)expect(scopeKey(proposal.scope)).toBe(scopeKey(workspace.scope));
   const history=await identityRead<{items:IdentityPublication[]}>(author,'/history',{scope:workspace.scope,pageSize:workspace.maximumPageSize,offset:0});for(const item of history.items)expect(scopeKey(item.scope)).toBe(scopeKey(workspace.scope));
  }
  const target=bootstrap.workspaces.find(item=>item.scope.family==='SessionListing')!.scope,initialHead=(await identityHistory(publisher,target))[0];
  let head=initialHead;const versions:IdentityPublication[]=[];
  for(const [index,rows] of [3,4].entries()){
   await identitySelect(author,target);const version='fixture-budget-'+randomUUID();await expect(author.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();
   await author.locator('[name="version"]').fill(version);await author.locator('[name="maximumRows"]').fill(String(rows));await author.locator('[name="maximumRequestBytes"]').fill('256');await author.locator('[name="from"]').fill('2026-01-01T00:00:00Z');await author.locator('[name="startBoundary"]').selectOption('inclusive');await author.locator('[name="endMode"]').selectOption('none');await author.locator('[name="evidence.0"]').fill('fixture-synthetic-owner-evidence');
   const createdResponse=command(author,'/identity/configuration/session-listing/draft');await author.getByRole('button',{name:'Save draft',exact:true}).click();const raw=await createdResponse;expect(raw.status()).toBe(200);const receipt=await raw.json() as {result:IdentityProposal;replayed:boolean;operationId:string};const draft=receipt.result;expect(draft.state).toBe('DRAFT');expect(receipt.replayed).toBe(false);
   const originalBody=raw.request().postDataJSON() as Record<string,unknown>;expect(originalBody).toMatchObject({candidate:{metadata:{versionId:version,status:'ACTIVE',scope:'USER_BFF_SESSION_LISTING',approvalEvidenceReferences:['fixture-synthetic-owner-evidence']},maximumRows:rows,maximumRequestBytes:256}});const replay=await identityRead<{result:IdentityProposal;replayed:boolean;operationId:string}>(author,'/session-listing/draft',originalBody);expect(replay.replayed).toBe(true);expect(replay.operationId).toBe(receipt.operationId);expect(replay.result).toEqual(draft);
   await expect(author.getByRole('button',{name:'Validate candidate',exact:true})).toBeEnabled();const validating=result<IdentityProposal>(command(author,'/identity/configuration/proposals/validate'));await author.getByRole('button',{name:'Validate candidate',exact:true}).click();const validated=await validating;expect(validated.state).toBe('VALIDATED');
   await identityDenied(author,'/proposals/approve',{idempotencyKey:randomUUID(),proposalReference:validated.proposalReference,expectedRevision:validated.revision});
   await identitySelect(approver,target,validated);await approver.getByRole('link',{name:'Review configuration',exact:true}).click();await expect(approver.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();
   const approving=result<IdentityProposal>(command(approver,'/identity/configuration/proposals/approve'));await approver.getByRole('button',{name:'Approve candidate',exact:true}).click();const approved=await approving;expect(approved.state).toBe('APPROVED');expect(approved.proposalReference).toBe(draft.proposalReference);expect(approved.candidateDigest).toBe(validated.candidateDigest);expect(scopeKey(approved.scope)).toBe(scopeKey(target));
   const from=new Date(Math.max(Date.now()+10_000,head?Date.parse(head.effectiveInterval.from)+1000:0)).toISOString();
   await identityDenied(approver,'/proposals/publish',{idempotencyKey:randomUUID(),proposalReference:approved.proposalReference,expectedRevision:approved.revision,effectiveInterval:{from,includesFrom:true,until:null,includesUntil:false},supersedesPublicationReference:head?.publicationReference??null});
   await identitySelect(publisher,target,approved);await expect(publisher.getByRole('button',{name:'Save draft',exact:true})).toBeDisabled();await publisher.getByRole('link',{name:'Review configuration',exact:true}).click();
   if(head)await publisher.getByLabel('Compare with published version',{exact:true}).selectOption(head.publicationReference);await expect(publisher.locator('.configuration-diff [data-field="maximumRows"]')).toContainText(String(rows));
   expect((await identityHistory(publisher,target))[0]?.publicationReference??null).toBe(head?.publicationReference??null);
   await expect(publisher.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await publisher.getByRole('button',{name:'Publish configuration',exact:true}).click();await identityInterval(publisher,from);
   const publishing=result<IdentityPublication>(command(publisher,'/identity/configuration/proposals/publish'));await publisher.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();const published=await publishing;
   expect(published).toMatchObject({policyVersionReference:version,approverReference:'fixture-security-approver',publisherReference:'fixture-security-publisher',approvalEvidenceReference:'fixture-identity-admin-evidence',supersedesPublicationReference:head?.publicationReference??null});expect(published.versionReference).toBeTruthy();versions.push(published);head=published;
   if(index===1){expect((await new AxeBuilder({page:publisher}).analyze()).violations).toEqual([]);await publisher.screenshot({path:info.outputPath('synthetic-identity-published-review-1440.png'),fullPage:true});}
  }
  const first=versions[0]!,second=versions[1]!;await publisher.getByRole('link',{name:'Publication history',exact:true}).click();await expect(publisher.locator('main[aria-busy="false"] .content-history')).toHaveCount(1);
  const preview=await identityRead<{publication:IdentityPublication;sessionListing:{maximumRows:number;maximumRequestBytes:number}}>(publisher,'/history/preview',{scope:target,publicationReference:first.publicationReference});expect(preview.publication).toEqual(first);expect(preview.sessionListing).toMatchObject({maximumRows:3,maximumRequestBytes:256});
  const row=publisher.locator('article.configuration-history').filter({hasText:first.publicationReference});await expect(row).toHaveCount(1);await row.getByRole('button',{name:'Republish approved version',exact:true}).click();await publisher.getByRole('dialog').locator('summary').click();await expect(publisher.getByRole('dialog').locator('[data-field="maximumRows"]')).toContainText('3');
  await identityInterval(publisher,new Date(Math.max(Date.now()+10_000,Date.parse(second.effectiveInterval.from)+1000)).toISOString());expect((await identityHistory(publisher,target))[0]?.publicationReference).toBe(second.publicationReference);
  const restoring=result<IdentityPublication>(command(publisher,'/identity/configuration/rollback'));await publisher.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();const restored=await restoring;
  expect(restored).toMatchObject({versionReference:first.versionReference,policyVersionReference:first.policyVersionReference,approverReference:first.approverReference,approvalEvidenceReference:first.approvalEvidenceReference,publisherReference:'fixture-security-publisher',supersedesPublicationReference:second.publicationReference});expect(restored.publicationReference).not.toBe(first.publicationReference);
  await expect(publisher.getByRole('dialog')).toHaveCount(0);await expect(publisher.locator('main[aria-busy="false"]')).toBeVisible();const retained=await identityHistory(publisher,target);expect(retained[0]?.publicationReference).toBe(restored.publicationReference);for(const item of [...versions,restored])expect(retained.some(actual=>actual.publicationReference===item.publicationReference&&actual.versionReference===item.versionReference)).toBe(true);
  await publisher.screenshot({path:info.outputPath('synthetic-identity-restored-history-1440.png'),fullPage:true});await user.reload();expect(await heading(user).evaluate(node=>({text:node.textContent,version:node.getAttribute('data-content-version'),publication:node.getAttribute('data-content-publication')}))).toEqual(login);
  for(const page of [author,approver,publisher,user])expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});
 }finally{await Promise.all(contexts.map(context=>context.close()));}
});

test('Synthetic Admin distinct actors publish governed Login wording and prospectively restore its original version',async({browser},info)=>{
 test.setTimeout(120_000);
 expect(state.Mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');expect(state.GovernedContent).toBe(true);expect(state.SyntheticAdmin).toBe(true);
 expect(Number.isInteger(state.AdminPort)&&state.AdminPort!==state.ApplicationPort).toBe(true);
 const contexts=await Promise.all(Array.from({length:4},()=>browser.newContext({ignoreHTTPSErrors:false,viewport:{width:1440,height:1024}})));
 try{
  const [author,approver,publisher,user]=await Promise.all(contexts.map(context=>context.newPage()));
  if(!author||!approver||!publisher||!user)throw new Error('Four isolated browser contexts required');
  await user.goto(userOrigin+'/login');const originalText=await heading(user).textContent(),originalPublication=await heading(user).getAttribute('data-content-publication'),originalVersion=await heading(user).getAttribute('data-content-version');
  expect(originalText).toBeTruthy();expect(originalPublication).toMatch(/^[0-9a-f-]{32,36}$/i);expect(originalVersion).toMatch(/^[0-9a-f-]{32,36}$/i);
  await author.goto(adminOrigin+'/browser-testing/admin');await expect(author.getByRole('heading',{name:'Isolated Admin fixture',exact:true})).toBeVisible();
  await author.screenshot({path:info.outputPath('synthetic-admin-bootstrap-1440.png'),fullPage:true});
  await author.setViewportSize({width:320,height:800});expect(await author.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect((await new AxeBuilder({page:author}).analyze()).violations).toEqual([]);
  await author.screenshot({path:info.outputPath('synthetic-admin-bootstrap-320.png'),fullPage:true});await author.setViewportSize({width:1440,height:1024});
  await actor(author,'author');const before=await history(author),original=before[0];if(!original)throw new Error('Original qualified Login publication required');
  expect(reference(original.publicationReference)).toBe(reference(originalPublication!));expect(reference(original.versionReference)).toBe(reference(originalVersion!));expect(original.text).toBe(originalText);
  await select(author);await expect(author.getByLabel('Wording',{exact:true})).toHaveValue('');
  const changed='Synthetic governed Login '+randomUUID();await author.getByLabel('Wording',{exact:true}).fill(changed);await author.getByLabel('Change reason',{exact:true}).fill('Isolated governed Content lifecycle qualification; restore original wording afterward.');
  const created=result<Proposal>(command(author,'/content/proposals'));await author.getByRole('button',{name:'Save draft',exact:true}).click();const draft=await created;expect(draft.state).toBe('DRAFT');
  await expect(author.getByLabel('Wording',{exact:true})).toHaveValue(changed);await author.screenshot({path:info.outputPath('governed-login-author-draft.png'),fullPage:true});await author.getByRole('button',{name:'Preview & compare',exact:true}).click();
  await expect(author.getByRole('button',{name:'Validate',exact:true})).toBeEnabled();const validatedResult=result<Proposal>(command(author,'/content/proposals/validate'));await author.getByRole('button',{name:'Validate',exact:true}).click();const validated=await validatedResult;expect(validated.state).toBe('VALIDATED');expect(validated.proposalReference).toBe(draft.proposalReference);
  await author.screenshot({path:info.outputPath('governed-login-author-validated.png'),fullPage:true});
  await forbidden(author,'/content/proposals/approve',validated,original);
  await actor(approver,'approver');await select(approver,validated.proposalReference);await expect(approver.getByLabel('Wording',{exact:true})).toHaveValue(changed);await approver.getByRole('button',{name:'Preview & compare',exact:true}).click();await expect(approver.getByRole('button',{name:'Approve',exact:true})).toBeEnabled();
  const approvedResult=result<Proposal>(command(approver,'/content/proposals/approve'));await approver.getByRole('button',{name:'Approve',exact:true}).click();const approved=await approvedResult;expect(approved.state).toBe('APPROVED');expect(approved.proposalReference).toBe(draft.proposalReference);
  await forbidden(approver,'/content/proposals/publish',approved,original);
  await actor(publisher,'publisher');await select(publisher,approved.proposalReference);await expect(publisher.getByLabel('Wording',{exact:true})).toHaveValue(changed);await expect(publisher.getByRole('button',{name:'Save draft',exact:true})).toBeDisabled();await publisher.getByRole('button',{name:'Preview & compare',exact:true}).click();
  await publisher.getByLabel('Compare with published version',{exact:true}).selectOption(original.versionReference);await expect(publisher.locator('.content-diff')).toContainText(originalText!);await expect(publisher.locator('.content-diff')).toContainText(changed);await expect(publisher.getByRole('button',{name:'Publish',exact:true})).toBeEnabled();
  expect((await new AxeBuilder({page:publisher}).analyze()).violations).toEqual([]);await publisher.screenshot({path:info.outputPath('governed-login-publisher-review.png'),fullPage:true});
  // Refuse to adopt an intervening publication as this test's superseded head.
  expect((await history(publisher))[0]?.publicationReference).toBe(original.publicationReference);
  await publisher.getByRole('button',{name:'Publish',exact:true}).click();await interval(publisher);const publishedResult=result<Publication>(command(publisher,'/content/proposals/publish'));await publisher.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();const published=await publishedResult;
  expect(published.supersedesPublicationReference).toBe(original.publicationReference);expect(published.approverReference).toBe('synthetic-local-approver');expect(published.publisherReference).toBe('synthetic-local-publisher');expect(published.text).toBe(changed);
  await userPublication(user,changed,published);expect((await new AxeBuilder({page:user}).analyze()).violations).toEqual([]);await user.screenshot({path:info.outputPath('governed-login-published-user.png'),fullPage:true});
  await publisher.getByRole('link',{name:'Version history',exact:true}).click();await expect(publisher.locator('main[aria-busy="false"]')).toBeVisible();const prior=await history(publisher);expect(prior[0]?.publicationReference).toBe(published.publicationReference);expect(prior.some(item=>item.publicationReference===original.publicationReference&&item.versionReference===original.versionReference)).toBe(true);
  const originalRow=publisher.locator('.content-history tbody tr').filter({hasText:original.publicationReference});await expect(originalRow).toHaveCount(1);await originalRow.getByRole('button',{name:'Restore this version',exact:true}).click();await interval(publisher);
  expect((await history(publisher))[0]?.publicationReference).toBe(published.publicationReference);const restoredResult=result<Publication>(command(publisher,'/content/rollback'));await publisher.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();const restored=await restoredResult;
  expect(restored.versionReference).toBe(original.versionReference);expect(restored.publicationReference).not.toBe(original.publicationReference);expect(restored.supersedesPublicationReference).toBe(published.publicationReference);expect(restored.publisherReference).toBe('synthetic-local-publisher');expect(restored.text).toBe(originalText);
  await userPublication(user,originalText!,restored);await user.screenshot({path:info.outputPath('governed-login-restored-user.png'),fullPage:true});
  await publisher.reload();await select(publisher,approved.proposalReference);await publisher.getByRole('link',{name:'Version history',exact:true}).click();await expect(publisher.locator('main[aria-busy="false"]')).toBeVisible();const after=await history(publisher);
  for(const publication of [original,published,restored]){expect(after.some(item=>item.publicationReference===publication.publicationReference&&item.versionReference===publication.versionReference&&item.text===publication.text)).toBe(true);await expect(publisher.locator('.content-history tbody tr').filter({hasText:publication.publicationReference})).toHaveCount(1);}
  expect((await new AxeBuilder({page:publisher}).analyze()).violations).toEqual([]);await publisher.screenshot({path:info.outputPath('governed-login-retained-history.png'),fullPage:true});
  const ended=author.waitForResponse(response=>new URL(response.url()).pathname==='/browser-testing/admin/session'&&response.status()===202);
  await actor(author,'publisher');expect((await ended).status()).toBe(202);
  for(const page of [author,approver,publisher,user])expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'',local:0,session:0});
 }finally{await Promise.all(contexts.map(context=>context.close()));}
});

test('Synthetic Admin bootstrap preserves canonical keyboard history and tablet reflow without Content writes',async({page,context},info)=>{
 test.setTimeout(90_000);
 expect(state.Mode).toBe('ISOLATED_LOCAL_BROWSER_TESTING');expect(state.GovernedContent).toBe(true);expect(state.SyntheticAdmin).toBe(true);
 let actorPosts=0,contentWrites=0;
 page.on('request',request=>{if(request.method()!=='POST')return;const path=new URL(request.url()).pathname;if(path==='/browser-testing/admin/session')actorPosts++;if(/^\/content\/(?:proposals(?:\/(?:update|validate|approve|publish))?|rollback)$/.test(path))contentWrites++;});
 const bootstrap=adminOrigin+'/browser-testing/admin',buttons=page.locator('[data-actor="author"],[data-actor="approver"],[data-actor="publisher"]');
 const initial=await page.goto(bootstrap);expect(initial?.status()).toBe(200);await expect(page).toHaveURL(bootstrap);
 await expect(page.getByRole('heading',{level:1,name:'Isolated Admin fixture',exact:true})).toBeVisible();
 await expect(page.locator('[data-tunner-surface="admin"][data-tunner-theme="light"]')).toBeVisible();await page.evaluate(()=>document.fonts.ready.then(()=>undefined));
 for(const viewport of [{width:1440,height:1024},{width:768,height:1024},{width:320,height:800}]){
  await page.setViewportSize(viewport);await expect(buttons).toHaveCount(3);
  for(const button of await buttons.all())await expect(button).toBeEnabled();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect(await buttons.evaluateAll(items=>items.every(item=>{const bounds=item.getBoundingClientRect();return bounds.width>0&&bounds.left>=-0.5&&bounds.right<=innerWidth+0.5;}))).toBe(true);
  if(viewport.width===768){expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath('synthetic-admin-bootstrap-768.png'),fullPage:true});}
 }
 expect(actorPosts).toBe(0);expect(contentWrites).toBe(0);
 // A real stale-CSRF failure in this fresh context; no intercepted request, secret output or host change.
 await page.setViewportSize({width:768,height:1024});await context.clearCookies();
 const rejected=page.waitForResponse(response=>new URL(response.url()).pathname==='/browser-testing/admin/session'&&response.request().method()==='POST');
 await page.getByRole('button',{name:'Use synthetic author',exact:true}).focus();await page.keyboard.press('Enter');expect((await rejected).status()).toBe(400);
 await expect(page.getByRole('status')).toHaveText('The isolated session could not start. Reload this page and try again.');
 await expect(page).toHaveURL(bootstrap);for(const button of await buttons.all())await expect(button).toBeEnabled();expect(actorPosts).toBe(1);
 await page.screenshot({path:info.outputPath('synthetic-admin-bootstrap-error-768.png'),fullPage:true});
 const refreshed=await page.reload();expect(refreshed?.status()).toBe(200);await expect(page.getByRole('status')).toBeEmpty();expect(actorPosts).toBe(1);
 await page.keyboard.press('Tab');await expect(page.getByRole('link',{name:'Skip to content',exact:true})).toBeFocused();await page.keyboard.press('Enter');
 await expect(page).toHaveURL(bootstrap+'#fixture-main');await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Use synthetic author',exact:true})).toBeFocused();
 const admitted=page.waitForResponse(response=>new URL(response.url()).pathname==='/browser-testing/admin/session'&&response.request().method()==='POST'&&response.status()===204);
 await page.keyboard.press('Enter');expect((await admitted).status()).toBe(204);await expect(page).toHaveURL(adminOrigin+'/admin/content');
 await expect(page.getByRole('heading',{level:1,name:'Content workspace',exact:true})).toBeVisible();expect(actorPosts).toBe(2);expect(contentWrites).toBe(0);
 await page.reload();await expect(page).toHaveURL(adminOrigin+'/admin/content');await expect(page.getByRole('heading',{level:1,name:'Content workspace',exact:true})).toBeVisible();expect(actorPosts).toBe(2);
 await page.goBack();await expect(page).toHaveURL(bootstrap+'#fixture-main');await expect(page.getByRole('heading',{level:1,name:'Isolated Admin fixture',exact:true})).toBeVisible();
 for(const button of await buttons.all())await expect(button).toBeEnabled();expect(actorPosts).toBe(2);
 await page.goForward();await expect(page).toHaveURL(adminOrigin+'/admin/content');await expect(page.getByRole('heading',{level:1,name:'Content workspace',exact:true})).toBeVisible();expect(actorPosts).toBe(2);
 await page.goBack();await expect(page).toHaveURL(bootstrap+'#fixture-main');const final=await page.reload();expect(final?.status()).toBe(200);
 await expect(page.getByRole('heading',{level:1,name:'Isolated Admin fixture',exact:true})).toBeVisible();for(const button of await buttons.all())await expect(button).toBeEnabled();
 expect(actorPosts).toBe(2);expect(contentWrites).toBe(0);expect(await page.evaluate(()=>({query:location.search,hash:location.hash,local:localStorage.length,session:sessionStorage.length}))).toEqual({query:'',hash:'#fixture-main',local:0,session:0});
});
