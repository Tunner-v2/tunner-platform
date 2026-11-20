import {useState} from 'react';
import type {ReactNode} from 'react';
import {ContentText,PrimaryButton,TunnerBrand} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';

export const accountShellContentSuffixes=['header.accountCenter','header.search','header.notifications','header.account','header.menu','navigation.account','navigation.overview','navigation.profile','navigation.security','navigation.sessions','navigation.billing','navigation.notifications','navigation.products','navigation.activity','navigation.help','navigation.helpDescription','navigation.contactSupport'] as const;
interface Props{contentPrefix:string;content:Record<string,PublishedContent>;navigationId:string;accountActionsId:string;blocked:boolean;onSignOut:()=>void;onSessionsNavigate?:()=>void;className?:string;children:ReactNode}
/** The existing Account header/navigation, with publication ownership supplied by each capability. */
export function AccountShell({contentPrefix,content,navigationId,accountActionsId,blocked,onSignOut,onSessionsNavigate,className='',children}:Props){
 const [menu,setMenu]=useState(false),[accountMenu,setAccountMenu]=useState(false),key=(suffix:string)=>contentPrefix+suffix;
 return <div className={'passkeys-page sessions-page '+className}>
  <header className="passkeys-header"><TunnerBrand contentKey="brand.tunner"/><span className="passkeys-header-label"><ContentText contentKey={key('header.accountCenter')}/></span><div className="passkeys-search" aria-hidden="true"><ContentText contentKey={key('header.search')}/></div><span className="passkeys-notifications" role="img" aria-label={content[key('header.notifications')]!.text}>•</span><button type="button" className="passkeys-owner" aria-expanded={accountMenu} aria-controls={accountActionsId} onClick={()=>setAccountMenu(value=>!value)}><ContentText contentKey={key('header.account')}/><span aria-hidden="true">⌄</span></button><button type="button" className="passkeys-menu" aria-label={content[key('header.menu')]!.text} aria-expanded={menu} aria-controls={navigationId} onClick={()=>setMenu(value=>!value)}><ContentText contentKey={key('header.menu')}/></button>
   {accountMenu&&<div className="sessions-account-menu" id={accountActionsId}><PrimaryButton appearance="outline" labelKey={key('signOut')} disabled={blocked} onClick={onSignOut}/></div>}
  </header>
  <div className="passkeys-shell"><nav className={'passkeys-sidebar'+(menu?' open':'')} id={navigationId} aria-label={content[key('header.menu')]!.text}><p className="passkeys-nav-label"><ContentText contentKey={key('navigation.account')}/></p>{(['overview','profile','security','sessions','billing','notifications','products','activity'] as const).map(name=>name==='sessions'?<a key={name} className="passkeys-nav-item active" aria-current="page" href="/account/sessions" onClick={event=>{if(onSessionsNavigate){event.preventDefault();onSessionsNavigate();}}}><ContentText contentKey={key('navigation.'+name)}/></a>:name==='security'?<a key={name} className="passkeys-nav-item" href="/account/security"><ContentText contentKey={key('navigation.security')}/></a>:<span key={name} className="passkeys-nav-item"><ContentText contentKey={key('navigation.'+name)}/></span>)}<div className="passkeys-nav-help"><p className="passkeys-nav-label"><ContentText contentKey={key('navigation.help')}/></p><p><ContentText contentKey={key('navigation.helpDescription')}/></p><PrimaryButton appearance="outline" labelKey={key('navigation.contactSupport')} disabled/></div><div className="sessions-mobile-signout"><PrimaryButton appearance="outline" labelKey={key('signOut')} disabled={blocked} onClick={onSignOut}/></div></nav>
   {children}
  </div>
 </div>;
}
