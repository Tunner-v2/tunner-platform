import {useId} from 'react';
import {ContentProvider,ContentText,ContentUnavailable,FormErrorSummary,PrimaryButton,TextField,ThemeProvider,TunnerBrand} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';
import {contentProvenance} from '../../packages/ui/src/content';
import '@tunner/ui/styles.css';
import '../user/authentication.css';
import './content.user-preview.css';

export interface ContentUserPreviewProps {
  published:{locale:string;content:Record<string,PublishedContent>;termsUri:string;privacyUri:string;supportUri:string|null};
  draft:{proposal:{proposalReference:string;revision:number;key:string};text:string};
  screen:'LOGIN'|'SIGNED_IN';
}

const assets={
  indigo:new URL('../user/public/assets/login-bg-indigo.svg?no-inline',import.meta.url).href,
  violet:new URL('../user/public/assets/login-bg-violet.svg?no-inline',import.meta.url).href,
  google:new URL('../user/public/assets/login-google.svg?no-inline',import.meta.url).href,
  eye:new URL('../user/public/assets/login-eye-off.svg?no-inline',import.meta.url).href,
  ellipse:new URL('../user/public/assets/ready-imgEllipse1.svg?no-inline',import.meta.url).href,
  check:new URL('../user/public/assets/ready-approved-check.svg?no-inline',import.meta.url).href,
};
const loginKeys=['login.heading','login.description','login.google','login.microsoft','login.unavailable','login.separator',
  'login.email.label','login.email.placeholder','login.password.label','login.password.placeholder','login.remember',
  'login.forgot','login.submit','login.create.description','login.create','login.show','login.hide',
  'authentication.error.email','authentication.error.password','authentication.error.highlighted',
  'authentication.error.credentials','authentication.error.limited'] as const;
const signedInKeys=['signedIn.heading','signedIn.description','signedIn.note.heading','signedIn.note.description',
  'signedIn.signOut','signedIn.security','signedIn.caption'] as const;
const commonKeys=['brand.tunner','authentication.footer.privacy','authentication.footer.terms',
  'authentication.footer.support','authentication.working','authentication.error.unavailable'] as const;
const requiredKeys:readonly string[]=[...loginKeys,...signedInKeys,...commonKeys];
const present=(value:unknown):value is string=>typeof value==='string'&&value.trim().length>0;
function safeLink(value:string):boolean {
  try{const uri=new URL(value);return uri.protocol==='https:'&&!uri.username&&!uri.password;}catch{return false;}
}

/** An isolated rendering of approved User presentation. No authentication controller or active navigation. */
export function ContentUserPreview({published,draft,screen}:ContentUserPreviewProps){
  const prefix=useId();
  const key=draft.proposal.key;
  const applicable:readonly string[]=[...commonKeys,...(screen==='LOGIN'?loginKeys:signedInKeys)];
  const valid=present(published.locale)&&present(draft.proposal.proposalReference)&&
    Number.isSafeInteger(draft.proposal.revision)&&draft.proposal.revision>0&&typeof draft.text==='string'&&
    (screen==='LOGIN'||screen==='SIGNED_IN')&&applicable.includes(key)&&
    !(key==='authentication.footer.support'&&published.supportUri===null)&&
    safeLink(published.termsUri)&&safeLink(published.privacyUri)&&(published.supportUri===null||safeLink(published.supportUri))&&
    requiredKeys.every(name=>{
      const item=published.content[name];
      return item?.key===name&&item.locale===published.locale&&item.state==='PUBLISHED'&&
        [item.variantRef,item.versionRef,item.publicationRef,item.text].every(present);
    });
  if(!valid)return <ContentUnavailable/>;
  const provenance={
    'data-content-state':'DRAFT','data-content-key':key,
    'data-proposal-reference':draft.proposal.proposalReference,'data-proposal-revision':draft.proposal.revision,
    lang:published.locale,
  } as const;
  const text=(name:string)=>name===key?<span {...provenance}>{draft.text}</span>:<ContentText contentKey={name}/>;
  const plain=(name:string)=>name===key?draft.text:published.content[name]!.text;
  function action(name:string){
    return name===key?<button type="button" disabled className="tn-primary-button" data-presentation="standard">{text(name)}</button>:
      <PrimaryButton labelKey={name} disabled/>;
  }
  function field(kind:'email'|'password'){
    const label=`login.${kind}.label`,placeholder=`login.${kind}.placeholder`,error=`authentication.error.${kind}`;
    const invalid=key===error||key==='authentication.error.highlighted',id=`${prefix}-${kind}`;
    if(![label,placeholder,error].includes(key))return <TextField id={id} labelKey={label} placeholderKey={placeholder}
      type="text" value="" readOnly disabled autoComplete="off" errorKey={invalid?error:undefined}/>;
    // Shared controls require published labels. Draft slots use the identical approved field variant without falsifying publication.
    return <div className="tn-text-field" data-presentation="standard">
      <label htmlFor={id}>{text(label)}</label>
      <input id={id} type="text" className="tn-input" value="" readOnly disabled autoComplete="off"
        placeholder={plain(placeholder)} aria-invalid={invalid||undefined} aria-describedby={invalid?`${id}-error`:undefined}
        {...(key===placeholder?provenance:contentProvenance(published.content[placeholder]!))}/>
      {invalid&&<span className="tn-field-error" id={`${id}-error`}>{text(error)}</span>}
    </div>;
  }
  const validation=['authentication.error.email','authentication.error.password','authentication.error.highlighted'].includes(key);
  const error=['authentication.error.credentials','authentication.error.limited',
    'authentication.error.unavailable'].includes(key)?key:undefined;
  const pending=key==='authentication.working';
  const reveal=key==='login.hide'?'login.hide':'login.show';
  return <section className="content-user-preview" data-preview-screen={screen} data-preview-readonly="true">
    <p className="tn-field-help"><strong {...provenance}>DRAFT</strong> · {key}</p>
    <ThemeProvider surface="user" mode="light"><ContentProvider locale={published.locale} audience="authentication" resolve={name=>published.content[name]}>
      <div className="authentication-page" data-preview-only="true">
        <img className="authentication-glow indigo" src={assets.indigo} alt=""/>
        <img className="authentication-glow violet" src={assets.violet} alt=""/>
        <header className="authentication-header">{key==='brand.tunner'?<span className="tn-brand">{text('brand.tunner')}</span>:<TunnerBrand contentKey="brand.tunner"/>}</header>
        <div className="authentication-stage">
          <section className={'authentication-card'+(screen==='SIGNED_IN'?' signed-in':'')}>
            {screen==='SIGNED_IN'?<>
              <div className="authentication-check" aria-hidden="true"><img src={assets.ellipse} alt=""/><img className="approved-check" src={assets.check} alt=""/></div>
              <div className="authentication-intro"><h1>{text('signedIn.heading')}</h1><p>{text('signedIn.description')}</p></div>
              <aside className="authentication-note"><strong>{text('signedIn.note.heading')}</strong><p>{text('signedIn.note.description')}</p></aside>
              {action('signedIn.signOut')}
              <a className="tn-primary-button authentication-security" data-presentation="standard" data-appearance="outline" aria-disabled="true" tabIndex={-1}>{text('signedIn.security')}</a>
              <p className="authentication-caption">{text('signedIn.caption')}</p>
            </>:<>
              <div className="authentication-intro"><h1>{text('login.heading')}</h1><p>{text('login.description')}</p></div>
              <div><div className="authentication-providers">
                <button type="button" disabled title={plain('login.unavailable')} {...(key==='login.unavailable'?provenance:contentProvenance(published.content['login.unavailable']!))}>{text('login.google')}<img src={assets.google} alt=""/></button>
                <button type="button" disabled title={plain('login.unavailable')} {...(key==='login.unavailable'?provenance:contentProvenance(published.content['login.unavailable']!))}>{text('login.microsoft')}</button>
              </div><div className="authentication-separator">{text('login.separator')}</div></div>
              <form noValidate autoComplete="off" onSubmit={event=>event.preventDefault()}>
                <div className="authentication-fields">
                  {field('email')}
                  <div className="authentication-password">{field('password')}<button className="authentication-reveal" type="button" disabled
                    aria-label={plain(reveal)} aria-pressed={key==='login.hide'} {...(key===reveal?provenance:contentProvenance(published.content[reveal]!))}><img src={assets.eye} alt=""/></button></div>
                  <div className="authentication-options"><label><input type="checkbox" checked={false} readOnly disabled autoComplete="off"/>{text('login.remember')}</label>
                    <button type="button" disabled title={plain('login.unavailable')} {...(key==='login.unavailable'?provenance:contentProvenance(published.content['login.unavailable']!))}>{text('login.forgot')}</button></div>
                </div>
                <div className="authentication-actions">{action('login.submit')}<p className="authentication-create">{text('login.create.description')} <a aria-disabled="true" tabIndex={-1}>{text('login.create')}</a></p></div>
                {validation&&(key==='authentication.error.highlighted'?<p className="tn-form-error-summary" data-preview-only-state="validation">{text('authentication.error.highlighted')}</p>:<FormErrorSummary contentKey="authentication.error.highlighted"/>)}
              </form>
            </>}
            {error&&<p className="tn-form-error-summary" data-preview-only-state="error">{text(error)}</p>}
            {pending&&<p data-preview-only-state="pending">{text('authentication.working')}</p>}
          </section>
        </div>
        <footer className="authentication-footer"><a aria-disabled="true" tabIndex={-1}>{text('authentication.footer.privacy')}</a><span aria-hidden="true">•</span>
          <a aria-disabled="true" tabIndex={-1}>{text('authentication.footer.terms')}</a>{published.supportUri!==null&&<><span aria-hidden="true">•</span><a aria-disabled="true" tabIndex={-1}>{text('authentication.footer.support')}</a></>}</footer>
      </div>
    </ContentProvider></ThemeProvider>
  </section>;
}
