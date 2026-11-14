/** Independently navigable screens; selection/continuation never enters a URL or browser storage. */
export const sessionRoutes={
 list:{id:'U-ACCT-04',pathname:'/account/sessions'},
 details:{id:'U-ACCT-SESSION-DETAILS',pathname:'/account/sessions/details'}
} as const;
export function isSessionRoute(pathname:string){return Object.values(sessionRoutes).some(route=>route.pathname===pathname);}
