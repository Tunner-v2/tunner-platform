export const configurationFamilies=['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent','Passkey','TotpContent','PasskeyContent','StepUpContent','RecoveryCodes','SessionListing'] as const;
export type ConfigurationFamily=typeof configurationFamilies[number];
const segments:Record<ConfigurationFamily,string>={Authentication:'authentication',Assurance:'assurance',AuthenticationContent:'authentication-content',Registration:'registration',Totp:'totp',RegistrationContent:'registration-content',Passkey:'passkey',TotpContent:'totp-content',PasskeyContent:'passkey-content',StepUpContent:'step-up-content',RecoveryCodes:'recovery-codes',SessionListing:'session-listing'};
export type ConfigurationView='edit'|'review'|'history';
export function configurationPath(family:ConfigurationFamily,view:ConfigurationView='edit'){return '/identity/settings/'+segments[family]+(view==='edit'?'':'/'+view);}
export function configurationRoute(path:string):{family:ConfigurationFamily;view:ConfigurationView}|undefined{
 for(const family of configurationFamilies)for(const view of ['edit','review','history'] as const)if(path===configurationPath(family,view))return {family,view};return undefined;
}
