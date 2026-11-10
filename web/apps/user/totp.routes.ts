export const totpRoutes = {
  manage:{id:'U-ACCT-TOTP',pathname:'/account/security/authenticator'},
  setup:{id:'U-AUTH-04',pathname:'/account/security/authenticator/setup'},
  ready:{id:'U-ACCT-TOTP-READY',pathname:'/account/security/authenticator/ready'},
  mfa:{id:'U-AUTH-TOTP-MFA',pathname:'/login/verify-authenticator'}
} as const;
export function isTotpAccountRoute(pathname:string){return pathname===totpRoutes.manage.pathname||pathname===totpRoutes.setup.pathname||pathname===totpRoutes.ready.pathname;}
