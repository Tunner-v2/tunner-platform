export const passkeyRoutes = {
  security:{id:'U-ACCT-SECURITY',pathname:'/account/security'},
  methods:{id:'U-ACCT-03',pathname:'/account/security/sign-in-methods'}
} as const;
export function isPasskeyRoute(pathname:string){return pathname===passkeyRoutes.security.pathname||pathname===passkeyRoutes.methods.pathname;}
