/** Semantic publication contracts. These keys supply no runtime copy or policy defaults. */
export const totpShellKeys = [
  'brand.tunner','passkeys.header.accountCenter','passkeys.header.account','passkeys.header.search','passkeys.header.notifications','passkeys.header.menu',
  'passkeys.navigation.account','passkeys.navigation.overview','passkeys.navigation.profile','passkeys.navigation.security','passkeys.navigation.sessions',
  'passkeys.navigation.billing','passkeys.navigation.notifications','passkeys.navigation.products','passkeys.navigation.activity','passkeys.navigation.help','passkeys.navigation.helpDescription'
] as const;
export const totpPresentationKeys = [
  'heading','description','eyebrow','status.label','status.activeHeading','status.activeDescription','status.inactiveHeading','status.inactiveDescription',
  'status.unavailableHeading','status.unavailableDescription','status.active','status.inactive','status.disabled',
  'manage.heading','manage.description','manage.method','manage.added','manage.lastUsed','manage.notUsed','manage.guidanceHeading','manage.guidanceDescription','manage.guidanceEffects','manage.removeHeading',
  'methods.label','methods.description','methods.inactiveDescription','methods.unavailable','methods.manage',
  'setup.heading','setup.description','setup.scanHeading','setup.scanDescription','setup.manualHeading','setup.manualDescription','setup.warning',
  'setup.codeHeading','setup.codeDescription','setup.consumedHeading','setup.consumedDescription','setup.alreadyShownHeading','setup.alreadyShownDescription','setup.guidanceHeading','setup.guidanceDescription','setup.compatibleApps',
  'setup.preparingHeading','setup.preparingDescription','setup.verifyingHeading','setup.verifyingDescription','setup.expiredHeading','setup.expiredDescription',
  'setup.unknownHeading','setup.unknownDescription','setup.codeUnavailable','setup.qrUnavailable',
  'ready.heading','ready.description',
  'disable.heading','disable.description','disable.preserve','disable.affectedSessions','disable.currentSessionEnds','disable.confirmDescription',
  'reset.heading','reset.description','reset.separateEnrollment','reset.preserve','reset.affectedSessions','reset.currentSessionEnds',
  'terminal.disableHeading','terminal.resetHeading','terminal.preserveDescription','terminal.endedHeading','terminal.endedDescription','terminal.resetDescription','terminal.unknown',
  'code.label','code.placeholder','action.setup','action.reviewReset','action.remove','action.verify','action.cancel','action.back','action.done','action.close','action.check','action.signIn','action.continue',
  'error.required','error.code','error.highlighted','error.credentials','error.replayed','error.limited','error.changed','error.unavailable','error.eligibility','error.limit','error.validation','error.qr','error.policy',
  'mfa.heading','mfa.description','mfa.pending','mfa.unknown','mfa.expiredHeading','mfa.expiredDescription','mfa.unavailable'
] as const;
export const requiredTotpKeys = [...totpShellKeys,...totpPresentationKeys.map(key=>'totp.'+key)] as const;
export const requiredTotpMfaKeys = [...requiredTotpKeys,'authentication.footer.privacy','authentication.footer.terms','authentication.footer.support'] as const;
export const totpSelectionKeys = ['heading','proofDescription','loginDescription','optionLabel','added','numberExplanation','selectedDescription','change'].map(key=>'totp.selection.'+key);
export const stepUpTotpKeys = ['stepUp.totp.description','stepUp.totp.code.label','stepUp.totp.code.placeholder','stepUp.totp.pending','stepUp.totp.unknown',
  'stepUp.totp.expired','stepUp.totp.error.required','stepUp.totp.error.code','stepUp.totp.error.credentials','stepUp.totp.error.replayed','stepUp.totp.check','stepUp.totp.close'] as const;
