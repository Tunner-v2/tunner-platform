import {test,expect,type Page,type Response} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync} from 'node:fs';
import {randomUUID} from 'node:crypto';
import type {Proposal,Publication} from '../../apps/admin/content.api';

// Real isolated hosts only: no request interception, credentials or saved browser state.
test.use({ignoreHTTPSErrors:false});
const state=JSON.parse(readFileSync(new URL('../../../artifacts/browser-testing/runtime.json',import.meta.url),'utf8').replace(/^\uFEFF/,'')) as {ApplicationPort:number;AdminPort?:number;Mode:string;GovernedContent?:boolean;SyntheticAdmin?:boolean};
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
 const bootstrap=adminOrigin+'/browser-testing/admin',buttons=page.locator('[data-actor]');
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
