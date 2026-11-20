export const registrationRoutes = {
  register: { id: 'U-AUTH-02', pathname: '/registration' },
  verify: { id: 'U-AUTH-03', pathname: '/registration/verify-email' },
  personal: { id: 'U-AUTH-03A', pathname: '/registration/personal-information' },
  consent: { id: 'U-AUTH-CONSENT', pathname: '/registration/consent' },
  ready: { id: 'U-AUTH-RESULT', pathname: '/registration/complete' }
} as const;
export type RegistrationStage = keyof typeof registrationRoutes;
export function stageForAction(action: string): RegistrationStage | undefined {
  return action === 'VERIFY_CONTACT' ? 'verify' : action === 'PERSONAL_INFORMATION' ? 'personal' : action === 'CONSENT_REQUIRED' ? 'consent' : action === 'COMPLETE' ? 'ready' : undefined;
}
