import {useEffect,useState} from 'react';
import {ContentProvider,ContentText,PrimaryButton} from '@tunner/ui';
import {loadTotpMethods,loadTotpOptions} from './totp.api';
import type {TotpBootstrap,TotpMethods} from './totp.api';
import {totpRoutes} from './totp.routes';

/** Safe method composition only. The TOTP owner still guards its canonical screen and every operation. */
export function TotpMethodRow({disabled,locale,available}:{disabled:boolean;locale:string;available:boolean}){
 const [state,setState]=useState<{boot:TotpBootstrap;methods:TotpMethods}>();
 useEffect(()=>{if(!available){setState(undefined);return;}const controller=new AbortController();void Promise.all([loadTotpOptions(controller.signal),loadTotpMethods(controller.signal)]).then(([boot,methods])=>{if(!controller.signal.aborted&&boot.locale===locale)setState({boot,methods});}).catch(()=>{/* An unavailable future/disabled owner never becomes a fabricated active method. */});return()=>controller.abort();},[locale,available]);
 if(!available||!state)return null;const active=state.methods.authenticators.some(factor=>factor.state==='ACTIVE');
 return <ContentProvider locale={state.boot.locale} audience="account-totp-method" resolve={key=>state.boot.content[key]}><div className="passkeys-method-row"><div><p className="passkeys-method-title"><ContentText contentKey="totp.methods.label"/></p><p className="passkeys-method-detail"><ContentText contentKey={active?'totp.methods.description':'totp.methods.inactiveDescription'}/></p></div>{active&&<span className="passkeys-pill"><ContentText contentKey="totp.status.active"/></span>}{disabled?<PrimaryButton labelKey="totp.methods.manage" appearance="outline" disabled/>:<a className="tn-primary-button" data-appearance="outline" href={totpRoutes.manage.pathname}><ContentText contentKey="totp.methods.manage"/></a>}</div></ContentProvider>;
}
