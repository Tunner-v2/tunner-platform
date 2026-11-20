import {z} from '@tunner/ui';
import {PasskeyHttpError,requestJson,requirePublished} from './passkeys.api';
import {SessionHttpError} from './sessions.api';

/** Separate Device publication; no listing, single-session or bulk-session keys are added. */
export const deviceContentKeys=[
 'account.devices.title','account.devices.description','account.devices.loading','account.devices.empty',
 'account.devices.unavailable','account.devices.changed','account.devices.unknown','account.devices.check',
 'account.devices.review.title','account.devices.review.description','account.devices.confirm','account.devices.cancel',
 'account.devices.completed','account.devices.trust.trusted','account.devices.trust.untrusted','account.devices.trust.revoked',
 'account.devices.sessionUnchanged','account.devices.observedInstallation',
 'account.devices.eyebrow','account.devices.details','account.devices.details.description','account.devices.label',
 'account.devices.collection','account.devices.collection.description','account.devices.about','account.devices.trust.heading',
 'account.devices.trust.description','account.devices.trust.notAuthentication','account.devices.current','account.devices.current.heading',
 'account.devices.firstObserved','account.devices.lastObserved','account.devices.reference','account.devices.browserFamily',
 'account.devices.browserUnavailable','account.devices.recordedTrust','account.devices.effectiveTrust','account.devices.checkedAt',
 'account.devices.refresh','account.devices.loadMore','account.devices.allLoaded','account.devices.moreAvailable',
 'account.devices.viewDetails','account.devices.back','account.devices.open','account.devices.sessionReview',
 'account.devices.review.verification','account.devices.preparing','account.devices.checking','account.devices.result.description',
 'account.devices.unknown.description','account.devices.changedOriginal.description','account.devices.close','account.devices.signOut',
 'account.devices.signOutPending','account.devices.signOutUnknown','account.devices.checkSignIn',
 'account.devices.header.accountCenter','account.devices.header.search','account.devices.header.notifications','account.devices.header.account','account.devices.header.menu',
 'account.devices.navigation.account','account.devices.navigation.overview','account.devices.navigation.profile','account.devices.navigation.security','account.devices.navigation.sessions',
 'account.devices.navigation.billing','account.devices.navigation.notifications','account.devices.navigation.products','account.devices.navigation.activity',
 'account.devices.navigation.help','account.devices.navigation.helpDescription','account.devices.navigation.contactSupport','brand.tunner',
 'account.devices.information','account.devices.currentObservation','account.devices.trust.separate',
] as const;
const root='/account/devices';
const reference=z.uuid().refine(value=>value!=='00000000-0000-0000-0000-000000000000');
const version=z.string().regex(/^[1-9][0-9]*$/).refine(value=>BigInt(value)<=9223372036854775807n);
const removableTrustVersion=version.refine(value=>BigInt(value)<9223372036854775807n);
const timestamp=z.iso.datetime({offset:true});
const text=z.string().refine(value=>value.trim().length>0);
const trustState=z.enum(['TRUSTED','UNTRUSTED','REVOKED']);
const publication=z.strictObject({key:text,variantRef:text,versionRef:text,publicationRef:text,locale:text,state:z.literal('PUBLISHED'),text});
const options=z.strictObject({requestId:reference,locale:text,content:z.record(z.string(),publication)});
const item=z.strictObject({reference,version,trustVersion:version,observedForCurrentSession:z.boolean(),firstObservedAt:timestamp,lastObservedAt:timestamp,characteristics:z.record(text,text),recordedTrustState:trustState,effectiveTrustState:trustState,evaluatedAt:timestamp});
const page=z.strictObject({devices:z.array(item),hasMore:z.boolean(),continuation:text.nullable()});
const details=z.strictObject({device:item});
const listCommand=z.strictObject({continuation:text.optional()});
const prepareCommand=z.strictObject({requestId:reference,reference,expectedVersion:version,expectedTrustVersion:removableTrustVersion,confirmed:z.literal(true)});
const target=z.strictObject({reference,version,trustVersion:removableTrustVersion});
const context=z.strictObject({requestId:reference,operationReference:text,status:z.enum(['PENDING','COMPLETED','CHANGED']),target,sessionEffect:z.literal('PRESERVE_EXISTING_SESSIONS'),expiresAt:timestamp});
const result=z.strictObject({requestId:reference,operationReference:text,status:z.literal('REVOKED'),target:z.strictObject({reference,version,priorTrustVersion:version,trustVersion:version}),revokedAt:timestamp,sessionEffect:z.literal('PRESERVE_EXISTING_SESSIONS')});
export type DeviceOptions=z.infer<typeof options>;
export type DeviceItem=z.infer<typeof item>;
export type DevicePage=z.infer<typeof page>;
export type DeviceTrustRemovalRequest=z.infer<typeof prepareCommand>;
export type DeviceTrustRemovalContext=z.infer<typeof context>;
export type DeviceTrustRemovalResult=z.infer<typeof result>;

function safeError(error:unknown):SessionHttpError{
 if(error instanceof SessionHttpError)return error;
 if(error instanceof PasskeyHttpError){
  if(error.status===401||error.status===403)return new SessionHttpError('authentication');
  if(error.status===404||error.status===409)return new SessionHttpError('changed');
 }
 return new SessionHttpError('unavailable');
}
export function parseDeviceOptions(value:unknown):DeviceOptions{
 try{const parsed=options.parse(value);new Intl.DateTimeFormat(parsed.locale);requirePublished(parsed.content,parsed.locale,deviceContentKeys);return {...parsed,content:Object.fromEntries(deviceContentKeys.map(key=>[key,parsed.content[key]!]))};}
 catch{throw new SessionHttpError('unavailable');}
}
/** Options create a protected draft identity only. They never select or prepare a removal. */
export async function loadDeviceOptions(signal:AbortSignal):Promise<DeviceOptions|undefined>{
 try{return parseDeviceOptions(await requestJson(root+'/options',{signal}));}catch(error){if(error instanceof PasskeyHttpError&&error.status===404)return undefined;throw safeError(error);}
}
export function parseDevicePage(value:unknown):DevicePage{
 try{const parsed=page.parse(value);if(parsed.hasMore!==(parsed.continuation!==null)||parsed.hasMore&&parsed.devices.length===0||new Set(parsed.devices.map(device=>device.reference)).size!==parsed.devices.length||parsed.devices.filter(device=>device.observedForCurrentSession).length>1)throw new Error('Inconsistent device page');return parsed;}
 catch{throw new SessionHttpError('unavailable');}
}
export async function readDevicePage(csrfToken:string,continuation:string|undefined,signal:AbortSignal):Promise<DevicePage>{
 try{const body=listCommand.parse(continuation===undefined?{}:{continuation});return parseDevicePage(await requestJson(root+'/list',{csrfToken,body,signal}));}catch(error){throw safeError(error);}
}
export async function readDeviceDetails(csrfToken:string,deviceReference:string,signal:AbortSignal):Promise<DeviceItem>{
 try{reference.parse(deviceReference);const parsed=details.parse(await requestJson(root+'/details',{csrfToken,body:{reference:deviceReference},signal}));if(parsed.device.reference!==deviceReference)throw new Error('Selected device changed');return parsed.device;}catch(error){throw safeError(error);}
}
export function parseDeviceTrustRemovalContext(value:unknown,expected?:DeviceTrustRemovalRequest|DeviceTrustRemovalContext):DeviceTrustRemovalContext{
 try{
  const parsed=context.parse(value);if(expected){
   if(parsed.requestId!==expected.requestId)throw new Error('Original request changed');
   if('target' in expected){
    const original=context.parse(expected);if(parsed.operationReference!==original.operationReference||parsed.expiresAt!==original.expiresAt||parsed.target.reference!==original.target.reference||parsed.target.version!==original.target.version||parsed.target.trustVersion!==original.target.trustVersion)throw new Error('Original device changed');
   }else{
    const original=prepareCommand.parse(expected);if(parsed.target.reference!==original.reference||parsed.target.version!==original.expectedVersion||parsed.target.trustVersion!==original.expectedTrustVersion)throw new Error('Confirmed selection changed');
   }
  }
  return parsed;
 }catch{throw new SessionHttpError('unavailable');}
}
/** The caller must supply confirmed:true after explicit confirmation; options do not invoke this. */
export async function prepareDeviceTrustRemoval(csrfToken:string,request:DeviceTrustRemovalRequest,signal:AbortSignal):Promise<DeviceTrustRemovalContext>{
 try{const body=prepareCommand.parse(request);return parseDeviceTrustRemovalContext(await requestJson(root+'/prepare',{csrfToken,body,signal}),body);}catch(error){throw safeError(error);}
}
/** Absence says only that no original is currently disclosable. It never triggers preparation. */
export async function readDeviceTrustRemovalContext(signal:AbortSignal):Promise<DeviceTrustRemovalContext|undefined>{
 try{return parseDeviceTrustRemovalContext(await requestJson(root+'/context',{signal}));}catch(error){if(error instanceof PasskeyHttpError&&error.status===404)return undefined;throw safeError(error);}
}
export function parseDeviceTrustRemovalResult(value:unknown,original:DeviceTrustRemovalContext):DeviceTrustRemovalResult{
 try{
  const expected=parseDeviceTrustRemovalContext(original),parsed=result.parse(value);
  if(parsed.requestId!==expected.requestId||parsed.operationReference!==expected.operationReference||parsed.target.reference!==expected.target.reference||parsed.target.version!==expected.target.version||parsed.target.priorTrustVersion!==expected.target.trustVersion||BigInt(parsed.target.trustVersion)!==BigInt(expected.target.trustVersion)+1n)throw new Error('Original causal receipt changed');
  return parsed;
 }catch{throw new SessionHttpError('unavailable');}
}
/** Exact-original result read only. Uncertainty never retries preparation or obtains a new draft. */
export async function readDeviceTrustRemovalResult(csrfToken:string,original:DeviceTrustRemovalContext,signal:AbortSignal):Promise<DeviceTrustRemovalResult>{
 try{const expected=parseDeviceTrustRemovalContext(original);return parseDeviceTrustRemovalResult(await requestJson(root+'/result',{csrfToken,body:{requestId:expected.requestId},signal}),expected);}catch(error){throw safeError(error);}
}
