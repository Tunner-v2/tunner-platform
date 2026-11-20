/** Approved011-V01–V04 paths; the server owns admission and original-case state. */
export const recoveryRoutes={
 entry:{id:'U-AUTH-07',pathname:'/recovery'},
 verify:{id:'U-AUTH-RECOVERY-VERIFY',pathname:'/recovery/verify'},
 repair:{id:'U-AUTH-RECOVERY-REPAIR',pathname:'/recovery/repair'},
 result:{id:'U-AUTH-RECOVERY-RESULT',pathname:'/recovery/result'}
} as const;
export function isRecoveryRoute(pathname:string){return Object.values(recoveryRoutes).some(route=>route.pathname===pathname);}
