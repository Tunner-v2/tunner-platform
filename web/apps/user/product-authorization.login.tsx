import {ContentProvider,ContentText,z} from '@tunner/ui';
import type {Ref} from 'react';
import {productAuthorizationContentValues,requireProductAuthorizationPublished} from './product-authorization.api';
import './product-authorization.css';

export const productAuthorizationLoginKeys=['brand.tunner','authorization.heading','authorization.loginDescription','authorization.loginPermissions','authorization.loginSummary','authorization.boundary'] as const;
const text=z.string().min(1);
const published=z.object({key:text,variantRef:text,versionRef:text,publicationRef:text,locale:text,state:z.literal('PUBLISHED'),text});
const loginContext=z.object({productName:text,locale:text,content:z.record(z.string(),published),scopeDescriptions:z.record(z.string(),published)}).strict();
export type ProductAuthorizationLoginContext=z.infer<typeof loginContext>;
/** This context is qualified separately for the anonymous original request; never use authenticated review options. */
export function parseProductAuthorizationLoginContext(raw:unknown):ProductAuthorizationLoginContext|undefined{
  if(raw===undefined||raw===null)return undefined;
  const context=loginContext.parse(raw);
  if(/[\u0000-\u001f\u007f]/.test(context.productName))throw new Error('Product sign-in presentation is unavailable.');
  requireProductAuthorizationPublished(context.content,context.locale,productAuthorizationLoginKeys);
  requireProductAuthorizationPublished(context.scopeDescriptions,context.locale,Object.keys(context.scopeDescriptions));
  for(const key of productAuthorizationLoginKeys)productAuthorizationContentValues(context.content,key,context.productName);
  return context;
}
function Copy({context,contentKey}:{context:ProductAuthorizationLoginContext;contentKey:string}){const values=productAuthorizationContentValues(context.content,contentKey,context.productName);return values?<ContentText contentKey={contentKey} values={values}/>:<ContentText contentKey={contentKey}/>;}
export function ProductAuthorizationLoginIntro({context,headingRef}:{context:ProductAuthorizationLoginContext;headingRef?:Ref<HTMLHeadingElement>}){
  return <ContentProvider locale={context.locale} audience="product-authorization-login" resolve={key=>context.content[key]}>
    <div className="authentication-intro"><h1 ref={headingRef} tabIndex={-1}><Copy context={context} contentKey="authorization.heading"/></h1><p><Copy context={context} contentKey="authorization.loginDescription"/></p></div>
    <aside className="product-authorization-sharing" data-tunner-component="U-AUTH-PRODUCT-ORIGIN"><strong><Copy context={context} contentKey="authorization.loginPermissions"/></strong><ContentProvider locale={context.locale} audience="product-authorization-login-scopes" resolve={key=>context.scopeDescriptions[key]}><ul>{Object.keys(context.scopeDescriptions).map(key=><li key={key}><ContentText contentKey={key}/></li>)}</ul></ContentProvider><p><Copy context={context} contentKey="authorization.loginSummary"/></p></aside>
  </ContentProvider>;
}
export function ProductAuthorizationLoginBoundary({context}:{context:ProductAuthorizationLoginContext}){
  return <ContentProvider locale={context.locale} audience="product-authorization-login" resolve={key=>context.content[key]}><p className="product-authorization-boundary"><Copy context={context} contentKey="authorization.boundary"/></p></ContentProvider>;
}
