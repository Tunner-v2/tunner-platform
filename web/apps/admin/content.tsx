import {createContext,useContext,useEffect,useLayoutEffect,useRef,useState} from 'react';
import type {ReactNode} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FieldError,FormErrorSummary,ModalDialog,PrimaryButton,TextField,ThemeProvider,TunnerBrand,navigatePath,useRouteLocation,useTunnerForm} from '@tunner/ui';
import * as api from './content.api';
import type {Action,Bootstrap,Capabilities,ContentLabel,Definition,Presentation,Preview,Proposal,Publication,WritePath,Comparison} from './content.api';
import {contentRoutes,isContentRoute} from './content.routes';
import {draftSchema,publicationSchema,searchSchema} from './content.validation';
import {ContentUserPreview} from './content.user-preview';
import type {ContentUserPreviewProps} from './content.user-preview';
const k=api.label;
function Text({name}:{name:ContentLabel}){return <ContentText contentKey={k(name)}/>;}
const Labels=createContext<Presentation|undefined>(undefined);
function Option({value,name}:{value:string;name:ContentLabel}){const content=useContext(Labels)!.content[k(name)]!;return <option value={value} data-content-key={content.key} data-content-version={content.versionRef} data-content-publication={content.publicationRef}>{content.text}</option>;}
const has=(cap:Capabilities|undefined,action:Action)=>cap?.availableActions.includes(action)===true;
const stateLabel:Record<Proposal['state'],ContentLabel>={DRAFT:'draftState',VALIDATED:'validatedState',APPROVED:'approvedState',PUBLISHED:'publishedState'};
interface Intent{path:WritePath;body:Readonly<Record<string,unknown>>}
interface Confirmation{kind:'publish'|'rollback';target?:Publication;trigger:HTMLElement|null}
export function ContentApp(){return <ThemeProvider surface="admin" mode="light"><ContentWorkspace/></ThemeProvider>;}
function ContentWorkspace(){
 const location=useRouteLocation(),route=useRef(location.revision);route.current=location.revision;
 const [boot,setBoot]=useState<Bootstrap>();const [readyRevision,setReadyRevision]=useState(-1);const [fatal,setFatal]=useState(false);const [blocked,setBlocked]=useState(false);
 const ready=readyRevision===location.revision;
 const [scope,setScope]=useState(''),[definition,setDefinition]=useState<Definition>(),[variant,setVariant]=useState('');
 const [library,setLibrary]=useState<Definition[]>([]),[nextDefinition,setNextDefinition]=useState<number|null>(null),[definitionOffset,setDefinitionOffset]=useState(0);
 const [search,setSearch]=useState(''),[submittedSearch,setSubmittedSearch]=useState('');const [browseAllowed,setBrowseAllowed]=useState(false);
 const [candidates,setCandidates]=useState<Proposal[]>([]),[selectedId,setSelectedId]=useState(''),[proposal,setProposal]=useState<Proposal>();
 const [definitionCaps,setDefinitionCaps]=useState<Capabilities>(),[proposalCaps,setProposalCaps]=useState<Capabilities>();
 const [history,setHistory]=useState<Publication[]>([]),[nextHistory,setNextHistory]=useState<number|null>(null),[historyOffset,setHistoryOffset]=useState(0);
 const [historyCaps,setHistoryCaps]=useState<Record<string,Capabilities>>({}),[publicationHead,setPublicationHead]=useState<Publication>();
 const [preview,setPreview]=useState<Preview>(),[comparison,setComparison]=useState<Comparison>(),[baseline,setBaseline]=useState('');
 const [contextPreview,setContextPreview]=useState<ContentUserPreviewProps>(),[qualifiedContextPreview,setQualifiedContextPreview]=useState('');
 const [text,setText]=useState(''),[reason,setReason]=useState('');const [notice,setNotice]=useState<ContentLabel>();
 const [busy,setBusy]=useState(false),[qualifiedLibrary,setQualifiedLibrary]=useState(''),[qualifiedTarget,setQualifiedTarget]=useState(''),[qualifiedComparison,setQualifiedComparison]=useState(''),[reload,setReload]=useState(0),[unknown,setUnknown]=useState(false),[confirmation,setConfirmation]=useState<Confirmation>();
 const [rebaseRequired,setRebaseRequired]=useState(false);const dirty=useRef(false),context=useRef('');context.current=JSON.stringify([scope,definition?.key,variant,selectedId]);
 const reads=useRef<AbortController|undefined>(undefined),writes=useRef<AbortController|undefined>(undefined),pending=useRef(false),intent=useRef<Intent|undefined>(undefined),heading=useRef<HTMLHeadingElement>(null);
 const workspace=boot?.workspaces.find(value=>value.scope===scope);const presentation=boot?.presentation;
 const libraryKey=JSON.stringify([location.revision,scope,submittedSearch,definitionOffset,reload,workspace?.profileVersion]);
 const targetKey=JSON.stringify([libraryKey,definition?.key,variant,selectedId,historyOffset]);
 const currentLibrary=useRef(libraryKey),currentTarget=useRef(targetKey);currentLibrary.current=libraryKey;currentTarget.current=targetKey;
 const libraryReady=ready&&(!workspace||qualifiedLibrary===libraryKey),targetRequired=!!workspace&&!!definition&&!!variant;
 const resourceReady=libraryReady&&(!targetRequired||qualifiedTarget===targetKey),reading=ready&&!!workspace&&!resourceReady;
 const comparisonKey=JSON.stringify([targetKey,baseline,proposal?.revision]),currentComparison=useRef(comparisonKey);currentComparison.current=comparisonKey;
 const contextPreviewKey=JSON.stringify([targetKey,preview?.proposal.revision,preview?.proposal.textDigest]),currentContextPreview=useRef(contextPreviewKey);currentContextPreview.current=contextPreviewKey;
 const locked=!resourceReady||blocked||busy||unknown,focusedRevision=useRef(-1);
 function cleanSelection(){dirty.current=false;setRebaseRequired(false);setDefinition(undefined);setVariant('');setCandidates([]);setSelectedId('');setProposal(undefined);setDefinitionCaps(undefined);setProposalCaps(undefined);setPreview(undefined);setComparison(undefined);setBaseline('');setHistory([]);setHistoryCaps({});setPublicationHead(undefined);setText('');setReason('');setHistoryOffset(0);setNextHistory(null);setConfirmation(undefined);}
 function error(error:unknown){
  if(error instanceof api.ContentHttpError&&[401,403].includes(error.status)){setBlocked(true);cleanSelection();setLibrary([]);setNotice('noAccess');return;}
  if(error instanceof api.ContentHttpError&&error.status===409){setProposalCaps(undefined);setNotice('conflict');return;}
  setNotice('unavailable');
 }
 useEffect(()=>{
  if(!isContentRoute(location.pathname)){navigatePath(contentRoutes.workspace.pathname,true);return;}
  if(window.location.search||window.location.hash)navigatePath(location.pathname,true);
  reads.current?.abort();writes.current?.abort();if(pending.current){setUnknown(true);pending.current=false;setBusy(false);}
  const controller=new AbortController(),revision=location.revision;setBlocked(false);setConfirmation(undefined);
  void api.loadWorkspace(controller.signal).then(value=>{if(controller.signal.aborted||route.current!==revision)return;setBoot(value);setFatal(value.presentation===null);setReload(current=>current+1);setReadyRevision(revision);}).catch(()=>{if(!controller.signal.aborted&&route.current===revision){setFatal(true);setReadyRevision(revision);}});
  return()=>controller.abort();
 },[location.revision]);
 useLayoutEffect(()=>{if(resourceReady&&!fatal&&focusedRevision.current!==location.revision){heading.current?.focus();focusedRevision.current=location.revision;}},[resourceReady,fatal,location.revision]);
 useEffect(()=>{
  if(!ready||!workspace||!boot?.csrfToken||blocked)return;
  const controller=new AbortController(),revision=route.current;let active=true;setBrowseAllowed(false);setDefinitionCaps(undefined);setProposalCaps(undefined);
  const valid=()=>active&&!controller.signal.aborted&&route.current===revision&&currentLibrary.current===libraryKey;
  void api.capabilities({scope:workspace.scope,target:'Workspace'},boot.csrfToken,controller.signal).then(async cap=>{
   if(!valid())return;if(cap.profileVersion!==workspace.profileVersion||!has(cap,'Browse')){setNotice('noAccess');return;}
   setBrowseAllowed(true);const page=await api.definitions(workspace.scope,submittedSearch,workspace.maximumPageSize,definitionOffset,boot.csrfToken,controller.signal);
   if(valid()){setLibrary(page.items);setNextDefinition(page.nextOffset);setQualifiedLibrary(libraryKey);}
  }).catch(value=>{if(valid())error(value);});
  return()=>{active=false;controller.abort();};
 },[ready,libraryKey]);
 useEffect(()=>{
  if(!libraryReady||!workspace||!definition||!variant||!boot||!browseAllowed||blocked)return;
  reads.current?.abort();const controller=new AbortController();reads.current=controller;const revision=route.current;setDefinitionCaps(undefined);setProposalCaps(undefined);setPreview(undefined);setComparison(undefined);
  const valid=()=>!controller.signal.aborted&&route.current===revision&&currentTarget.current===targetKey&&currentLibrary.current===libraryKey;
  void api.capabilities({scope:workspace.scope,target:'Definition',key:definition.key,variantReference:variant},boot.csrfToken,controller.signal).then(async cap=>{
   if(!valid())return;if(cap.profileVersion!==workspace.profileVersion)throw new Error('Content context changed');setDefinitionCaps(cap);
   const page=await api.proposals(workspace.scope,definition.key,variant,workspace.maximumPageSize,0,boot.csrfToken,controller.signal);
   if(!valid())return;setCandidates(page.items);
   if(selectedId){const current=page.items.find(item=>item.proposalReference===selectedId);if(current){setProposal(current);const actions=await api.capabilities({scope:workspace.scope,target:'Proposal',proposalReference:current.proposalReference,expectedRevision:current.revision},boot.csrfToken,controller.signal);
    if(!valid())return;if(actions.profileVersion!==workspace.profileVersion)throw new Error('Content context changed');setProposalCaps(actions);
    if(has(actions,'Preview')){const actual=await api.loadPreview(current,boot.csrfToken,controller.signal);if(!valid())return;if(actual.proposal.proposalReference!==current.proposalReference||actual.proposal.revision!==current.revision)throw new Error('Stale preview');setPreview(actual);if(!dirty.current){setText(actual.text);setReason(actual.changeReason??'');}else if(proposal&&current.revision!==proposal.revision){setRebaseRequired(true);setNotice('conflict');}}
   }else{setProposal(undefined);setSelectedId('');setNotice('conflict');}}
   if(has(cap,'History')){const page=await api.history(workspace.scope,definition.key,variant,workspace.maximumPageSize,historyOffset,boot.csrfToken,controller.signal);const permissions=await Promise.allSettled(page.items.map(item=>api.capabilities({scope:workspace.scope,target:'History',publicationReference:item.publicationReference},boot.csrfToken,controller.signal)));if(valid()){setHistory(page.items);setHistoryCaps(Object.fromEntries(permissions.flatMap((result,index)=>result.status==='fulfilled'&&result.value.profileVersion===workspace.profileVersion?[[page.items[index]!.publicationReference,result.value]]:[])));setNextHistory(page.nextOffset);if(historyOffset===0)setPublicationHead(page.items[0]);}}
   else if(valid()){setHistory([]);setHistoryCaps({});setPublicationHead(undefined);setNextHistory(null);}
   if(valid())setQualifiedTarget(targetKey);
  }).catch(value=>{if(valid())error(value);});
  return()=>controller.abort();
 },[libraryReady,browseAllowed,targetKey]);
 useEffect(()=>{
  if(!resourceReady||!boot||!proposal||!baseline||!has(proposalCaps,'Preview')||blocked)return;const controller=new AbortController(),revision=route.current;
  void api.loadComparison(proposal,baseline,boot.csrfToken,controller.signal).then(value=>{if(!controller.signal.aborted&&route.current===revision&&currentComparison.current===comparisonKey){setComparison(value);setQualifiedComparison(comparisonKey);}}).catch(value=>{if(!controller.signal.aborted&&route.current===revision&&currentComparison.current===comparisonKey)error(value);});return()=>controller.abort();
 },[comparisonKey,proposalCaps,resourceReady]);
 useEffect(()=>{
  setContextPreview(undefined);if(!resourceReady||!boot||!preview||!has(proposalCaps,'Preview')||blocked)return;
  const controller=new AbortController(),revision=route.current,selected=context.current;
  void api.loadContextPreview(preview.proposal,boot.csrfToken,controller.signal).then(value=>{if(!controller.signal.aborted&&route.current===revision&&context.current===selected&&currentContextPreview.current===contextPreviewKey){setContextPreview(value);setQualifiedContextPreview(contextPreviewKey);}}).catch(value=>{if(!controller.signal.aborted&&route.current===revision&&context.current===selected&&currentContextPreview.current===contextPreviewKey&&value instanceof api.ContentHttpError&&[401,403].includes(value.status))error(value);});
  return()=>controller.abort();
 },[resourceReady,contextPreviewKey,proposalCaps,boot?.csrfToken,blocked]);
 async function perform(path:WritePath,body:Readonly<Record<string,unknown>>,retry=false){
  if(pending.current||!boot||!ready||blocked||route.current!==location.revision||currentLibrary.current!==libraryKey||currentTarget.current!==targetKey)return;
  if(!retry){
   if(!workspace||!definition||!variant||!resourceReady||busy||unknown)return;
   const action:Action=path==='/content/proposals'?'CreateDraft':path.endsWith('/update')?'UpdateDraft':path.endsWith('/validate')?'Validate':path.endsWith('/approve')?'Approve':path.endsWith('/publish')?'Publish':'Rollback';
   if(action==='CreateDraft'){if(selectedId||!has(definitionCaps,action)||body.scope!==scope||body.key!==definition.key||body.variantReference!==variant)return;}
   else if(action==='Rollback'){const target=confirmation?.target;if(!target||!has(historyCaps[target.publicationReference],action)||body.approvedVersionReference!==target.versionReference||body.supersedesPublicationReference!==publicationHead?.publicationReference)return;}
   else if(!proposal||!preview||!has(proposalCaps,action)||rebaseRequired||(action!=='UpdateDraft'&&dirty.current)||body.proposalReference!==proposal.proposalReference||body.expectedRevision!==proposal.revision)return;
  }
  const command=retry?intent.current:{path,body:{...body,idempotencyKey:crypto.randomUUID()}};if(!command)return;
  intent.current=command;pending.current=true;setBusy(true);setNotice(undefined);setUnknown(false);const controller=new AbortController(),revision=route.current;writes.current=controller;
  try{const result=await api.write(command.path,command.body,boot.csrfToken,controller.signal);if(controller.signal.aborted||route.current!==revision)return;
   intent.current=undefined;dirty.current=false;setRebaseRequired(false);setHistoryOffset(0);setNotice(result.replayed?'replayed':'success');setConfirmation(undefined);
   if('proposalReference' in result.result)setSelectedId(result.result.proposalReference);
   setReload(current=>current+1);
  }catch(value){if(controller.signal.aborted||route.current!==revision)return;
   if(!(value instanceof api.ContentHttpError)||value.status>=500){setUnknown(true);setNotice('unknown');}else{intent.current=undefined;error(value);}
  }finally{if(writes.current===controller){pending.current=false;setBusy(false);writes.current=undefined;}}
 }
 async function command(action:'validate'|'approve'){
  if(!proposal||!has(proposalCaps,action==='validate'?'Validate':'Approve')||locked||!preview||dirty.current||rebaseRequired)return;
  await perform(action==='validate'?'/content/proposals/validate':'/content/proposals/approve',{proposalReference:proposal.proposalReference,expectedRevision:proposal.revision});
 }
 async function requestRollback(publication:Publication,trigger:HTMLElement){
  if(!boot||!workspace||locked||!has(historyCaps[publication.publicationReference],'Rollback'))return;const controller=new AbortController(),revision=route.current,selected=context.current;
  try{const cap=await api.capabilities({scope:workspace.scope,target:'History',publicationReference:publication.publicationReference},boot.csrfToken,controller.signal);if(route.current!==revision||context.current!==selected)return;if(!has(cap,'Rollback')){setNotice('noAccess');return;}setConfirmation({kind:'rollback',target:publication,trigger});}catch(value){if(route.current===revision&&context.current===selected)error(value);}
 }
 function selectScope(value:string){reads.current?.abort();cleanSelection();setLibrary([]);setScope(value);setSearch('');setSubmittedSearch('');setDefinitionOffset(0);setNotice(undefined);}
 function selectDefinition(value:Definition){reads.current?.abort();cleanSelection();setDefinition(value);setNotice(undefined);}
 function selectCandidate(value:string){dirty.current=false;setRebaseRequired(false);setSelectedId(value);setProposal(undefined);setPreview(undefined);setComparison(undefined);setBaseline('');setText('');setReason('');setNotice(undefined);}
 const searchForm=useTunnerForm(searchSchema(workspace?.maximumSearchLength??0),{search});
 if(fatal||ready&&!presentation)return <ContentUnavailable/>;
 if(!presentation)return <main aria-busy="true"/>;
 const review=location.pathname===contentRoutes.review.pathname,historyScreen=location.pathname===contentRoutes.history.pathname;
 const title:ContentLabel=review?'review':historyScreen?'history':'workspace';
 return <ContentProvider locale={presentation.locale} audience="admin-content-workspace" resolve={key=>presentation.content[key]}><Labels.Provider value={presentation}><Shell>
  <main id="content-main" className="content-main" data-tunner-screen={review?contentRoutes.review.id:historyScreen?contentRoutes.history.id:contentRoutes.workspace.id} aria-busy={!ready||reading}>
   <div className="content-page-heading"><div><p className="content-eyebrow"><Text name="content"/>{workspace&&<> / {workspace.surface}</>}</p><h1 ref={heading} tabIndex={-1}><Text name={title}/></h1><p className="content-muted"><Text name={review?'validationSummary':historyScreen?'historyHelp':'subtitle'}/></p></div>
    <nav className="content-actions" aria-label={presentation.content[k('content')]!.text}>{Object.values(contentRoutes).map(value=><a key={value.id} href={value.pathname} aria-current={location.pathname===value.pathname?'page':undefined} onClick={event=>{event.preventDefault();if(!busy)navigatePath(value.pathname);}}><Text name={value===contentRoutes.workspace?'workspace':value===contentRoutes.review?'review':'history'}/></a>)}</nav>
   </div>
   {!ready&&<p role="status"><Text name="loading"/></p>}
   {notice&&<div className={'content-notice content-notice-'+notice} role={notice==='success'||notice==='replayed'?'status':'alert'}><Text name={notice}/>{unknown&&intent.current?<PrimaryButton labelKey={k('retry')} disabled={!ready||busy} onClick={()=>void perform(intent.current!.path,intent.current!.body,true)}/>:!busy&&<PrimaryButton labelKey={k('refresh')} appearance="outline" onClick={()=>{setNotice(undefined);setReload(current=>current+1);}}/>}</div>}
   {ready&&!blocked&&!historyScreen&&!review&&<div className="content-workspace">
    <section className="content-panel content-library" aria-labelledby="content-library-heading"><div className="content-library-top"><h2 id="content-library-heading"><Text name="definition"/></h2>
     <Select id="content-scope" label="scope" value={scope} disabled={!ready||busy||unknown} onChange={selectScope}><Option value="" name="selectScope"/>{boot?.workspaces.map(item=><option key={item.scope} value={item.scope}>{item.surface}</option>)}</Select>
     <form noValidate onSubmit={event=>void searchForm.handleSubmit(event,()=>{setSubmittedSearch(search);setDefinitionOffset(0);})}><TextField id="content-search" name="search" labelKey={k('search')} helpKey={k('searchHelp')} value={search} onChange={event=>setSearch(event.target.value)} onBlur={event=>searchForm.onBlur('search',event)} errorKey={searchForm.errors.search} maxLength={workspace?.maximumSearchLength??0} disabled={!workspace||locked}/><PrimaryButton type="submit" labelKey={k('search')} appearance="outline" disabled={!workspace||locked}/></form>
    </div><ul>{(workspace&&libraryReady?library:[]).map(item=><li key={item.key}><button type="button" className={definition?.key===item.key?'active':''} onClick={()=>selectDefinition(item)} disabled={!libraryReady||busy||unknown}><strong>{item.purpose}</strong><span>{item.key}</span></button></li>)}</ul>{workspace&&library.length===0&&!reading&&<p className="content-small"><Text name="noMatches"/></p>}{workspace&&libraryReady&&nextDefinition!==null&&<PrimaryButton labelKey={k('more')} appearance="outline" disabled={locked} onClick={()=>setDefinitionOffset(nextDefinition)}/>}</section>
    <div className="content-stack">{!definition||!workspace?<section className="content-panel"><p><Text name="noSelection"/></p></section>:<>
     <section className="content-panel"><div className="content-editor-title"><div><h2>{definition.purpose}</h2><p className="content-key">{definition.key}</p></div>{proposal&&<span className="content-tag"><Text name={stateLabel[proposal.state]}/> · {proposal.revision}</span>}</div>
      <Select id="content-variant" label="context" value={variant} disabled={!libraryReady||busy||unknown} onChange={value=>{setVariant(value);selectCandidate('');}}><Option value="" name="selectVariant"/>{workspace.variants.map(item=><option key={item.reference} value={item.reference}>{item.locale} · {Object.values(item.safeDimensions).join(' / ')}</option>)}</Select>
      {variant&&<Select id="content-candidate" label="candidate" value={selectedId} disabled={locked} onChange={selectCandidate}><Option value="" name={has(definitionCaps,'CreateDraft')?'newDraft':'selectCandidate'}/>{candidates.map(item=><option key={item.proposalReference} value={item.proposalReference}>{presentation.content[k(stateLabel[item.state])]!.text} · {item.revision} · {item.proposalReference.slice(-8)}</option>)}</Select>}
      <DraftEditor key={scope+':'+definition.key+':'+variant+':'+selectedId+':'+(proposal?.revision??0)} text={text} reason={reason} maximum={workspace.maximumTextLength} locked={locked||rebaseRequired||!variant||selectedId!==''&&!preview||!(proposal?has(proposalCaps,'UpdateDraft'):has(definitionCaps,'CreateDraft'))} onText={value=>{dirty.current=true;setText(value);}} onReason={value=>{dirty.current=true;setReason(value);}} onSave={async values=>{if(proposal){if(!has(proposalCaps,'UpdateDraft')||rebaseRequired)return;await perform('/content/proposals/update',{proposalReference:proposal.proposalReference,expectedRevision:proposal.revision,text:values.text,changeReason:values.changeReason});}else{if(!has(definitionCaps,'CreateDraft'))return;await perform('/content/proposals',{scope,key:definition.key,variantReference:variant,text:values.text,changeReason:values.changeReason});}}}/>
      <div className="content-editor-footer"><p className="content-small"><Text name="wordingHelp"/></p><PrimaryButton labelKey={k('compare')} disabled={locked||!preview} onClick={()=>navigatePath(contentRoutes.review.pathname)}/></div>
     </section><section className="content-panel" aria-labelledby="content-preview-heading"><div className="content-panel-heading"><h2 id="content-preview-heading"><Text name="preview"/></h2><span className="content-tag neutral"><Text name="candidate"/></span></div>{dirty.current&&<p className="content-small"><Text name="unsaved"/></p>}{resourceReady&&contextPreview&&qualifiedContextPreview===contextPreviewKey&&preview&&contextPreview.draft.proposal.proposalReference===preview.proposal.proposalReference&&contextPreview.draft.proposal.revision===preview.proposal.revision?<ContentUserPreview {...contextPreview}/>:<p className="content-small"><Text name="previewUnavailable"/></p>}</section>
    </>}</div></div>}
   {ready&&!blocked&&review&&(resourceReady&&proposal&&preview&&workspace&&definition?<>
    <div className="content-two-column"><section className="content-panel"><h2><Text name="compare"/></h2><p className="content-key">{definition.key}</p>
     <Select id="content-baseline" label="baseline" value={baseline} disabled={locked||rebaseRequired} onChange={setBaseline}><Option value="" name="choose"/>{history.map(item=><option key={item.publicationReference} value={item.versionReference}>{item.versionReference.slice(-8)} · {date(item.effectiveInterval.from,presentation.locale)}</option>)}</Select>
     <div className="content-diff"><div><b><Text name="current"/></b><p>{rebaseRequired?preview.text:(qualifiedComparison===comparisonKey?comparison?.beforeText:undefined)??(history.length===0?presentation.content[k('empty')]!.text:presentation.content[k('choose')]!.text)}</p></div><div><b><Text name={dirty.current?'unsaved':'candidate'}/></b><p>{dirty.current?text:preview.text}</p></div></div>
     {rebaseRequired&&<div className="content-notice"><Text name="conflict"/><PrimaryButton labelKey={k('useReviewedVersion')} disabled={locked} onClick={()=>{setRebaseRequired(false);navigatePath(contentRoutes.workspace.pathname);}}/></div>}
     <dl className="content-facts"><div><dt><Text name="reason"/></dt><dd>{preview.changeReason??presentation.content[k('unavailable')]!.text}</dd></div><div><dt><Text name="context"/></dt><dd>{workspace.surface} / {workspace.variants.find(v=>v.reference===variant)?.locale}</dd></div><div><dt><Text name="version"/></dt><dd>{proposal.revision}</dd></div></dl>
    </section><section className="content-panel"><h2><Text name="approval"/></h2><dl className="content-facts"><div><dt><Text name="candidate"/></dt><dd><Text name={stateLabel[proposal.state]}/></dd></div><div><dt><Text name="approval"/></dt><dd><Text name={definition.requiresApproval?'needsApproval':'candidate'}/></dd></div></dl>{(workspace.separateAuthorApprover||workspace.separateAuthorPublisher||workspace.separateApproverPublisher)&&<div className="content-notice"><Text name="sod"/></div>}
     <div className="content-actions"><PrimaryButton labelKey={k('validate')} disabled={locked||dirty.current||rebaseRequired||!has(proposalCaps,'Validate')} onClick={()=>void command('validate')}/><PrimaryButton labelKey={k('approve')} disabled={locked||dirty.current||rebaseRequired||!has(proposalCaps,'Approve')} onClick={()=>void command('approve')}/><PrimaryButton labelKey={k('publish')} disabled={locked||dirty.current||rebaseRequired||!has(proposalCaps,'Publish')} onClick={()=>setConfirmation({kind:'publish',trigger:document.activeElement instanceof HTMLElement?document.activeElement:null})}/></div>
    </section></div>
   </>:<section className="content-panel"><p><Text name="needsContext"/></p><a href={contentRoutes.workspace.pathname} onClick={event=>{event.preventDefault();navigatePath(contentRoutes.workspace.pathname);}}><Text name="workspace"/></a></section>)}
   {ready&&!blocked&&(review||historyScreen)&&<section className="content-panel content-history"><h2><Text name="history"/></h2><p className="content-small"><Text name="historyHelp"/></p>{!workspace||!definition||!resourceReady?<p><Text name="needsContext"/></p>:<><div className="content-table-wrap"><table><thead><tr><th><Text name="version"/></th><th><Text name="effective"/></th><th><Text name="reason"/></th><th><Text name="review"/></th></tr></thead><tbody>{history.map(item=><tr key={item.publicationReference}><td><code>{item.versionReference.slice(-8)}</code><details><summary><Text name="publication"/></summary><p className="content-key">{item.publicationReference}</p></details></td><td>{date(item.effectiveInterval.from,presentation.locale)}<br/>{item.effectiveInterval.until?date(item.effectiveInterval.until,presentation.locale):<Text name="endNone"/>}</td><td>{item.changeReason??<Text name="unavailable"/>}</td><td><button type="button" className="content-text-action" disabled={locked||!publicationHead||!has(historyCaps[item.publicationReference],'Rollback')} onClick={event=>void requestRollback(item,event.currentTarget)}><Text name="restore"/></button></td></tr>)}</tbody></table></div>{history.length===0&&<p><Text name="empty"/></p>}{nextHistory!==null&&<PrimaryButton labelKey={k('more')} appearance="outline" disabled={locked} onClick={()=>setHistoryOffset(nextHistory)}/>}</>}</section>}
   {ready&&resourceReady&&confirmation&&boot&&<PublicationDialog confirmation={confirmation} busy={busy} onClose={()=>{if(!busy)setConfirmation(undefined);}} onConfirm={async effectiveInterval=>{
    if(confirmation.kind==='publish'){if(!proposal||!has(proposalCaps,'Publish')||dirty.current||rebaseRequired)return;await perform('/content/proposals/publish',{proposalReference:proposal.proposalReference,expectedRevision:proposal.revision,effectiveInterval,supersedesPublicationReference:publicationHead?.publicationReference??null});}
    else{const original=confirmation.target;if(!original||!publicationHead)return;await perform('/content/rollback',{approvedVersionReference:original.versionReference,supersedesPublicationReference:publicationHead.publicationReference,effectiveInterval});}
   }}/>}
  </main></Shell></Labels.Provider></ContentProvider>;
}
function Select({id,label,value,disabled,onChange,children,name,errorKey,onBlur}:{id:string;label:ContentLabel;value:string;disabled:boolean;onChange:(value:string)=>void;children:ReactNode;name?:string;errorKey?:string|undefined;onBlur?:()=>void}){return <div className="tn-text-field"><label htmlFor={id}><Text name={label}/></label><select className="tn-input" id={id} name={name} value={value} disabled={disabled} onBlur={onBlur} aria-invalid={errorKey?true:undefined} aria-describedby={errorKey?id+'-error':undefined} onChange={event=>onChange(event.target.value)}>{children}</select>{errorKey&&<FieldError id={id+'-error'} contentKey={errorKey}/>}</div>;}
function DraftEditor({text,reason,maximum,locked,onText,onReason,onSave}:{text:string;reason:string;maximum:number;locked:boolean;onText:(value:string)=>void;onReason:(value:string)=>void;onSave:(values:{text:string;changeReason:string})=>Promise<void>}){
 const form=useTunnerForm(draftSchema(maximum),{text,changeReason:reason});
 return <form noValidate onSubmit={event=>void form.handleSubmit(event,onSave)}>{form.hasErrors&&<FormErrorSummary contentKey={k('validationSummary')}/>}<div className="tn-text-field"><label htmlFor="content-wording"><Text name="wording"/></label><textarea id="content-wording" name="text" className="tn-input" value={text} maxLength={maximum} disabled={locked||form.submitPending} aria-invalid={form.errors.text?true:undefined} aria-describedby={form.errors.text?'content-wording-error':'content-wording-help'} onChange={event=>onText(event.target.value)} onBlur={event=>form.onBlur('text',event)}/><span id="content-wording-help" className="tn-field-help"><Text name="wordingHelp"/></span>{form.errors.text&&<FieldError id="content-wording-error" contentKey={form.errors.text}/>}</div>
  <TextField id="content-reason" name="changeReason" labelKey={k('reason')} helpKey={k('reasonHelp')} value={reason} maxLength={maximum} errorKey={form.errors.changeReason} disabled={locked||form.submitPending} onChange={event=>onReason(event.target.value)} onBlur={event=>form.onBlur('changeReason',event)}/><div className="content-actions"><PrimaryButton type="submit" labelKey={k('save')} disabled={locked||form.submitPending} aria-busy={form.submitPending}/></div></form>;
}
function PublicationDialog({confirmation,busy,onClose,onConfirm}:{confirmation:Confirmation;busy:boolean;onClose:()=>void;onConfirm:(interval:api.EffectiveInterval)=>Promise<void>}){
 const [from,setFrom]=useState(''),[until,setUntil]=useState(''),[startBoundary,setStart]=useState(''),[endMode,setEndMode]=useState(''),[endBoundary,setEndBoundary]=useState('');
 const form=useTunnerForm(publicationSchema,{from,until,startBoundary:startBoundary as 'inclusive'|'exclusive',endMode:endMode as 'none'|'at',endBoundary});const title=useRef<HTMLHeadingElement>(null);useEffect(()=>title.current?.focus(),[]);
 return <ModalDialog labelledBy="content-confirm-title" describedBy="content-confirm-description" busy={busy||form.submitPending} returnFocusTo={confirmation.trigger} onRequestClose={onClose} className="content-confirm"><h2 ref={title} tabIndex={-1} id="content-confirm-title"><Text name={confirmation.kind==='publish'?'publish':'restore'}/></h2><p id="content-confirm-description"><Text name="prospectiveOnly"/></p>
  <form noValidate onSubmit={event=>void form.handleSubmit(event,async values=>onConfirm({from:new Date(values.from+'Z').toISOString(),includesFrom:values.startBoundary==='inclusive',until:values.endMode==='at'?new Date(values.until+'Z').toISOString():null,includesUntil:values.endMode==='at'&&values.endBoundary==='inclusive'}))}>
   {form.hasErrors&&<FormErrorSummary contentKey={k('validationSummary')}/>}<TextField id="content-effective-from" name="from" type="datetime-local" labelKey={k('scheduledFrom')} helpKey={k('utc')} value={from} errorKey={form.errors.from} onBlur={event=>form.onBlur('from',event)} onChange={event=>setFrom(event.target.value)} disabled={busy}/>
   <Select id="content-start-boundary" name="startBoundary" errorKey={form.errors.startBoundary} onBlur={()=>form.onBlur('startBoundary')} label="startBoundary" value={startBoundary} disabled={busy} onChange={setStart}><Option value="" name="choose"/><Option value="inclusive" name="inclusive"/><Option value="exclusive" name="exclusive"/></Select>
   <Select id="content-end-mode" name="endMode" errorKey={form.errors.endMode} onBlur={()=>form.onBlur('endMode')} label="endMode" value={endMode} disabled={busy} onChange={setEndMode}><Option value="" name="choose"/><Option value="none" name="endNone"/><Option value="at" name="endAt"/></Select>
   {endMode==='at'&&<><TextField id="content-effective-until" name="until" type="datetime-local" labelKey={k('scheduledUntil')} helpKey={k('utc')} value={until} errorKey={form.errors.until} onBlur={event=>form.onBlur('until',event)} onChange={event=>setUntil(event.target.value)} disabled={busy}/><Select id="content-end-boundary" name="endBoundary" errorKey={form.errors.endBoundary} onBlur={()=>form.onBlur('endBoundary')} label="endBoundary" value={endBoundary} disabled={busy} onChange={setEndBoundary}><Option value="" name="choose"/><Option value="inclusive" name="inclusive"/><Option value="exclusive" name="exclusive"/></Select></>}
   <div className="content-actions"><PrimaryButton labelKey={k('cancel')} appearance="outline" disabled={busy||form.submitPending} onClick={onClose}/><PrimaryButton type="submit" labelKey={k('confirm')} disabled={busy||form.submitPending} aria-busy={busy||form.submitPending}/></div>
  </form></ModalDialog>;
}
function Shell({children}:{children:ReactNode}){const [menu,setMenu]=useState(false);return <div className="admin-content"><a className="content-skip" href="#content-main"><Text name="skip"/></a><div className="content-shell"><aside className={'content-sidebar'+(menu?' mobile-open':'')}><div className="content-brand"><TunnerBrand contentKey={k('brand')}/><p><Text name="operations"/></p></div><nav><a href={contentRoutes.workspace.pathname} className="active" onClick={event=>{event.preventDefault();navigatePath(contentRoutes.workspace.pathname);setMenu(false);}}><Text name="content"/></a></nav><div className="content-side-note"><strong><Text name="workspace"/></strong><p><Text name="boundary"/></p></div></aside><div className="content-body"><header className="content-header"><div><strong><Text name="title"/></strong><p><Text name="subtitle"/></p></div><button type="button" className="content-menu" aria-expanded={menu} onClick={()=>setMenu(!menu)}><Text name="menu"/></button></header><section className="content-boundary" aria-labelledby="content-boundary-label"><span id="content-boundary-label"><Text name="boundary"/></span></section>{children}</div></div></div>;}
function date(value:string,locale:string){try{return new Date(value).toLocaleString(locale,{dateStyle:'medium',timeStyle:'short',timeZone:'UTC'});}catch{return value;}}
