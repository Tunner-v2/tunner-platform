export const deviceRoutes={list:{id:'U-ACCT-DEVICES',pathname:'/account/devices'},details:{id:'U-ACCT-DEVICE-DETAILS',pathname:'/account/devices/:reference'}} as const;
// Keep the dev/build route map independent of browser/React imports. Typed DTO validation remains in the API client.
const canonicalReference=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
export function deviceDetailsPath(value:string){if(!canonicalReference.test(value))throw new Error('A canonical opaque device reference is required.');return deviceRoutes.list.pathname+'/'+value;}
export function deviceRoute(pathname:string):{detail:boolean;reference:string|undefined}|undefined{
 if(pathname===deviceRoutes.list.pathname)return {detail:false,reference:undefined};
 const match=/^\/account\/devices\/([^/]+)$/.exec(pathname);const value=match?.[1];return value&&canonicalReference.test(value)?{detail:true,reference:value}:undefined;
}
export function isDeviceRoute(pathname:string){return deviceRoute(pathname)!==undefined;}
