import {useEffect,useRef} from 'react';
import type {ReactNode} from 'react';
import {ContentProvider,ContentText,FormErrorSummary,PrimaryButton,navigatePath} from '@tunner/ui';
import type {SessionItem,SessionOptions} from './sessions.api';
import {sessionRoutes} from './sessions.routes';
import {AccountShell} from './account-shell';
import {formatAccountTime} from './account-format';
import type {DeviceOptions} from './devices.api';
import {DeviceNavigationCard} from './devices.ui';

export type SessionViewPhase='loading'|'ready'|'error'|'changed';
export type SessionSignOutPhase='idle'|'busy'|'unknown';
export interface SessionsViewProps{
 options:SessionOptions;phase:SessionViewPhase;detail:boolean;revision:number;items:readonly SessionItem[];selected:SessionItem|undefined;
 hasMore:boolean;moreBusy:boolean;signOutPhase:SessionSignOutPhase;
 revocationAvailable:boolean;revocationBlocked:boolean;onRevoke:(item:SessionItem)=>void;children?:ReactNode;
 deviceOptions?:DeviceOptions|undefined;
 onRefresh:()=>void;onLoadMore:()=>void;onSelect:(item:SessionItem)=>void;onSignOut:()=>void;onCheckSignIn:()=>void;
}
const key=(suffix:string)=>'account.sessions.'+suffix;
const stateKey=(state:SessionItem['effectiveState'])=>key('state.'+({ACTIVE:'active',REVOKED:'revoked',EXPIRED:'expired'} as const)[state]);
export const formatSessionTime=formatAccountTime;
function SessionTime({value,locale}:{value:string;locale:string}){return <time dateTime={value}>{formatSessionTime(value,locale)}</time>;}
function SessionLabel({item}:{item:SessionItem}){return <ContentText contentKey={key('label')} values={{reference:item.reference.slice(-8)}}/>;}
function Checked({item,locale}:{item:SessionItem;locale:string}){return <ContentText contentKey={key('checkedAt')} values={{time:formatSessionTime(item.evaluatedAt,locale)}}/>;}
function ScopeNote(){return <div className="passkeys-helpbox"><strong><ContentText contentKey={key('productHeading')}/></strong><p><ContentText contentKey={key('productBoundary')}/></p></div>;}
function SessionRow({item,locale,onSelect}:{item:SessionItem;locale:string;onSelect:(item:SessionItem)=>void}){
 return <article className="sessions-row" data-session-reference={item.reference}>
  <div><h3><SessionLabel item={item}/></h3><p><ContentText contentKey={key('lastActivity')}/> <SessionTime value={item.lastSeenAt} locale={locale}/></p><p><ContentText contentKey={key('savedStatus')}/>: <ContentText contentKey={stateKey(item.recordedState)}/></p><p className="sessions-state-line"><ContentText contentKey={key('checkedStatus')}/>: <span className={'sessions-pill '+(item.effectiveState==='ACTIVE'?'active':'neutral')}><ContentText contentKey={stateKey(item.effectiveState)}/></span></p><p className="sessions-checked"><Checked item={item} locale={locale}/></p></div>
  <div className="sessions-link"><PrimaryButton appearance="outline" labelKey={key('information')} onClick={()=>onSelect(item)}/></div>
 </article>;
}
function SessionDetails({item,locale,canRevoke,blocked,onRevoke}:{item:SessionItem;locale:string;canRevoke:boolean;blocked:boolean;onRevoke:(item:SessionItem)=>void}){
 return <><div className="sessions-link sessions-back"><span className="sessions-back-arrow" aria-hidden="true">←</span><PrimaryButton appearance="outline" labelKey={key('back')} onClick={()=>navigatePath(sessionRoutes.list.pathname)}/></div>
  <section className="passkeys-card sessions-detail-hero"><div><h2><SessionLabel item={item}/></h2><p className="sessions-muted"><ContentText contentKey={key('lastActivity')}/> <SessionTime value={item.lastSeenAt} locale={locale}/></p></div><span className="sessions-pill"><ContentText contentKey={key(item.isCurrent?'current':'other')}/></span></section>
  <div className="sessions-grid"><section className="passkeys-card"><h2><ContentText contentKey={key('information')}/></h2><dl className="sessions-facts">
   <div><dt><ContentText contentKey={key('reference')}/></dt><dd className="sessions-full-reference">{item.reference}</dd></div>
   <div><dt><ContentText contentKey={key('started')}/></dt><dd><SessionTime value={item.authenticatedAt} locale={locale}/></dd></div>
   <div><dt><ContentText contentKey={key('lastActivity')}/></dt><dd><SessionTime value={item.lastSeenAt} locale={locale}/></dd></div>
   <div><dt><ContentText contentKey={key('expires')}/></dt><dd><SessionTime value={item.expiresAt} locale={locale}/><small><ContentText contentKey={key('expiresHelp')}/></small></dd></div>
   <div><dt><ContentText contentKey={key('savedStatus')}/></dt><dd><ContentText contentKey={stateKey(item.recordedState)}/></dd></div>
   <div><dt><ContentText contentKey={key('checkedStatus')}/></dt><dd><ContentText contentKey={stateKey(item.effectiveState)}/><small><Checked item={item} locale={locale}/></small></dd></div>
   <div><dt><ContentText contentKey={key('deviceLocationTrust')}/></dt><dd><ContentText contentKey={key('unavailable')}/></dd></div>
  </dl></section><section className="passkeys-card"><h2><ContentText contentKey={key('actions')}/></h2><p className="sessions-muted"><ContentText contentKey={key(item.isCurrent?'currentActionHelp':canRevoke?item.effectiveState==='ACTIVE'?'revoke.actionHelp':'revoke.inactiveHelp':'otherActionHelp')}/></p>{canRevoke&&!item.isCurrent&&item.effectiveState==='ACTIVE'&&<div className="sessions-security-action passkeys-danger-wrap sessions-revoke-action"><PrimaryButton labelKey={key('revoke.action')} disabled={blocked} onClick={()=>onRevoke(item)}/></div>}<div className="sessions-security-action"><PrimaryButton appearance="outline" labelKey={key('reviewSecurity')} onClick={()=>window.location.assign('/account/security')}/></div><ScopeNote/></section></div>
 </>;
}
function SessionList({items,locale,hasMore,moreBusy,onSelect,onLoadMore,onRefresh}:{items:readonly SessionItem[];locale:string;hasMore:boolean;moreBusy:boolean;onSelect:(item:SessionItem)=>void;onLoadMore:()=>void;onRefresh:()=>void}){
 const current=items.find(item=>item.isCurrent),others=items.filter(item=>!item.isCurrent);
 return <>{current&&<section className="passkeys-summary sessions-current"><div><p className="passkeys-eyebrow"><ContentText contentKey={key('current')}/></p><h2><SessionLabel item={current}/></h2><p><ContentText contentKey={key('lastActivity')}/> <SessionTime value={current.lastSeenAt} locale={locale}/></p><span className="sessions-pill sessions-current-badge"><ContentText contentKey={key('currentBadge')}/></span></div><div className="sessions-current-help"><p><ContentText contentKey={key('currentHelp')}/></p><span className="sessions-link"><PrimaryButton appearance="outline" labelKey={key('currentDetails')} onClick={()=>onSelect(current)}/></span></div></section>}
  <div className="sessions-notice"><strong><ContentText contentKey={key('unavailableHeading')}/></strong><p><ContentText contentKey={key('unavailableDescription')}/></p></div>
  <div className="sessions-grid"><section className="passkeys-card"><h2><ContentText contentKey={key('collection')}/></h2><p className="sessions-muted"><ContentText contentKey={key('collectionDescription')}/></p>
   {others.map(item=><SessionRow key={item.reference} item={item} locale={locale} onSelect={onSelect}/>)}
   {!others.length&&<div className="sessions-empty" role="status"><h3><ContentText contentKey={key('empty')}/></h3><p><ContentText contentKey={key('emptyDescription')}/></p></div>}
   <div className="sessions-pagination"><p className="sessions-muted" role="status"><ContentText contentKey={key(hasMore?'moreAvailable':'allLoaded')}/></p>{hasMore&&<PrimaryButton appearance="outline" labelKey={key('loadMore')} disabled={moreBusy} aria-busy={moreBusy} onClick={onLoadMore}/>}</div>
   <span className="sessions-link"><PrimaryButton appearance="outline" labelKey={key('refresh')} disabled={moreBusy} onClick={onRefresh}/></span>
  </section><section className="passkeys-card"><h2><ContentText contentKey={key('about')}/></h2><p className="sessions-muted"><ContentText contentKey={key('referenceHelp')}/></p><div className="passkeys-helpbox"><strong><ContentText contentKey={key('accessHeading')}/></strong><p><ContentText contentKey={key('accessDescription')}/></p></div><p className="sessions-muted"><ContentText contentKey={key('savedStatusHelp')}/></p><p className="sessions-muted sessions-product-note"><ContentText contentKey={key('productBoundary')}/></p></section></div>
  <section className="sessions-notice sessions-bottom"><div><h2><ContentText contentKey={key('securityHeading')}/></h2><p><ContentText contentKey={key('securityDescription')}/></p></div><PrimaryButton appearance="outline" labelKey={key('openSecurity')} onClick={()=>window.location.assign('/account/security')}/></section>
 </>;
}
export function SessionsView(props:SessionsViewProps){
 const {options,phase,detail,revision,selected,signOutPhase}=props;
 const heading=useRef<HTMLHeadingElement>(null);
 useEffect(()=>{if(phase!=='loading')heading.current?.focus();},[phase,detail,revision]);
 const blocked=phase==='loading'||props.moreBusy||props.revocationBlocked||signOutPhase!=='idle';
 return <ContentProvider locale={options.locale} audience="account-sessions" resolve={name=>options.content[name]}><AccountShell contentPrefix="account.sessions." content={options.content} navigationId="sessions-navigation" accountActionsId="sessions-account-actions" blocked={blocked} onSignOut={props.onSignOut} onSessionsNavigate={()=>navigatePath(sessionRoutes.list.pathname)}>
   <main className="passkeys-main" data-tunner-screen={detail?sessionRoutes.details.id:sessionRoutes.list.id} aria-busy={phase==='loading'||props.moreBusy||signOutPhase==='busy'}><p className="passkeys-eyebrow"><ContentText contentKey={key('eyebrow')}/></p><h1 ref={heading} tabIndex={-1}><ContentText contentKey={key(detail?'details':'title')}/></h1><p className="passkeys-subtitle"><ContentText contentKey={key('description')}/></p>
    {signOutPhase==='busy'&&<p className="sessions-notice" role="status"><ContentText contentKey={key('signOutPending')}/></p>}
    {signOutPhase==='unknown'?<section className="passkeys-card sessions-state-panel"><FormErrorSummary contentKey={key('signOutUnknown')}/><PrimaryButton labelKey={key('checkSignIn')} disabled={props.moreBusy} onClick={props.onCheckSignIn}/></section>:phase==='loading'?<section className="passkeys-card sessions-state-panel" role="status"><h2><ContentText contentKey={key('loading')}/></h2><div className="sessions-skeleton" aria-hidden="true"/><div className="sessions-skeleton" aria-hidden="true"/></section>:phase==='error'||phase==='changed'?<section className="passkeys-card sessions-state-panel"><FormErrorSummary contentKey={key(phase==='changed'?'changed':'error')}/><PrimaryButton appearance="outline" labelKey={key('refresh')} onClick={props.onRefresh}/></section>:detail&&selected?<SessionDetails item={selected} locale={options.locale} canRevoke={props.revocationAvailable} blocked={blocked} onRevoke={props.onRevoke}/>:<SessionList items={props.items} locale={options.locale} hasMore={props.hasMore} moreBusy={blocked} onSelect={props.onSelect} onLoadMore={props.onLoadMore} onRefresh={props.onRefresh}/>}
    {!detail&&phase==='ready'&&props.deviceOptions&&<DeviceNavigationCard options={props.deviceOptions}/>}{props.children}
   </main>
 </AccountShell></ContentProvider>;
}
