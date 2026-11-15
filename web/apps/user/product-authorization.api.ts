import {z} from '@tunner/ui';
import type {PublishedContent} from '@tunner/ui';

export const productAuthorizationRoute={id:'U-AUTH-08',pathname:'/product-authorization'} as const;
const text=z.string().trim().min(1);
const fact=text.refine(value=>!/[\u0000-\u001f\u007f]/.test(value));
const published=z.object({key:text,variantRef:text,versionRef:text,publicationRef:text,locale:text,state:z.literal('PUBLISHED'),text:z.string().min(1)});
const publications=z.record(z.string(),published);
const https=text.refine(value=>{try{const uri=new URL(value);return uri.protocol==='https:'&&!uri.username&&!uri.password;}catch{return false;}});
const registeredOrigin=https.refine(value=>new URL(value).origin===value);
export const productAuthorizationKeys=['brand.tunner','authorization.heading','authorization.description','authorization.product','authorization.account','authorization.permissions','authorization.assurance','authorization.callback','authorization.allow','authorization.deny','authorization.switchAccount','authorization.working','authorization.changed','authorization.unavailable','authorization.proofRequired','authorization.loginRequired','authorization.boundary','authorization.footer.privacy','authorization.footer.terms','authorization.footer.support'] as const;
const reviewSchema=z.object({reference:text,csrfToken:text,canAllow:z.boolean(),interactionStatus:z.enum(['Satisfied','RequiresSignIn','RequiresFreshProof','Denied']),locale:text,content:publications,productName:fact,accountLabel:fact,registeredReturnOrigin:registeredOrigin,requestedAssuranceLabel:fact.nullable(),issuedClaimNames:z.array(fact),scopeDescriptions:publications,termsUri:https,privacyUri:https,supportUri:https.nullable()});
export type ProductAuthorizationOptions=z.infer<typeof reviewSchema>;
export type ProductAuthorizationDecision='ALLOW'|'DENY'|'SWITCH_ACCOUNT';

export function requireProductAuthorizationPublished(content:Record<string,PublishedContent>,locale:string,keys:readonly string[]){
  for(const key of keys){const value=content[key];if(!value||value.key!==key||value.locale!==locale||value.state!=='PUBLISHED'||![value.text,value.variantRef,value.versionRef,value.publicationRef].every(item=>item.trim().length>0))throw new Error('Required Product authorization publication is unavailable.');}
}
export function parseProductAuthorizationOptions(raw:unknown):ProductAuthorizationOptions{
  const options=reviewSchema.parse(raw);
  if(options.canAllow!==(options.interactionStatus==='Satisfied'))throw new Error('Inconsistent Product authorization interaction.');
  requireProductAuthorizationPublished(options.content,options.locale,productAuthorizationKeys);
  requireProductAuthorizationPublished(options.scopeDescriptions,options.locale,Object.keys(options.scopeDescriptions));
  for(const key of ['authorization.heading','authorization.permissions','authorization.boundary'])productAuthorizationContentValues(options.content,key,options.productName);
  return options;
}
export class ProductAuthorizationHttpError extends Error{constructor(readonly status:number,readonly code:string){super('The Product authorization request could not be completed.');}}
async function request(path:string,signal:AbortSignal,command?:{csrfToken:string;reference:string;decision:ProductAuthorizationDecision}):Promise<unknown>{
  const response=await fetch(path,{method:command?'POST':'GET',credentials:'same-origin',cache:'no-store',redirect:'error',signal,...(command?{headers:{'Content-Type':'application/json','X-Tunner-CSRF':command.csrfToken},body:JSON.stringify({reference:command.reference,decision:command.decision})}:{})});
  if(!response.ok){let code='DEPENDENCY_UNAVAILABLE';try{const problem=await response.json() as {error_code?:unknown};if(typeof problem.error_code==='string')code=problem.error_code;}catch{/* Preserve the actual HTTP failure. */}throw new ProductAuthorizationHttpError(response.status,code);}
  return response.json() as Promise<unknown>;
}
export async function loadProductAuthorizationOptions(signal:AbortSignal){return parseProductAuthorizationOptions(await request('/product-authorization/options',signal));}
export async function decideProductAuthorization(options:Pick<ProductAuthorizationOptions,'reference'|'csrfToken'>,decision:ProductAuthorizationDecision,signal:AbortSignal){
  return z.object({resumePath:text}).strict().parse(await request('/product-authorization/decision',signal,{csrfToken:options.csrfToken,reference:options.reference,decision}));
}
export function productAuthorizationErrorKey(error:unknown){return error instanceof ProductAuthorizationHttpError&&[400,401,403,404,409].includes(error.status)?'authorization.changed':'authorization.unavailable';}

/** Only recognized presentation variables are supplied to published copy; never synthesize copy. */
export function productAuthorizationContentValues(content:Record<string,PublishedContent>,key:string,productName:string):Readonly<Record<string,string>>|undefined{
  const template=content[key]?.text;if(!template)throw new Error('Required Product authorization publication is unavailable.');
  const variables=[...template.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g)].map(match=>match[1]);
  if(variables.some(name=>name!=='productName'))throw new Error('Unsupported Product authorization content variable.');
  if(variables.length&&productName.length>512)throw new Error('Published Product authorization variable exceeds the shared presentation bound.');
  return variables.length?{productName}:undefined;
}
