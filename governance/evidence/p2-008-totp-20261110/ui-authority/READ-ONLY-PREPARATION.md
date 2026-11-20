# P2-008 TOTP UI authority preparation

Status: read-only authority discovery and bounded proposal outline. No implementation, source-authority edit, Product approval or test PASS. Figma session `626d04de-d9ff-4a44-86c5-b6b7deae1af1`.

The current approved desktop compositions already cover management, enrollment and successful first-code verification. They should be reused. The additional states below need concrete visual proposals and scoped consent before their production UI is implemented. Runtime profile/eligibility/session effects and published copy remain authoritative server/content inputs.

| Existing authority | Exact reference | Observed coverage |
|---|---|---|
| Authenticator management | Platform UX `83:2`, 1440×1100 | Account shell; active status; app details; Set up new app; removal panel |
| Enrollment and first code | `83:117`, 1440×1100 | Setup card `83:208` 760×748; dynamic QR slot `83:211` 220×220; manual-key slot `83:282` 430×52; code field `83:287` 420×56; Cancel/Verify |
| Verified result | `83:298`, 1440×1100 | Success card `83:386` 700×420; Done |
| Sign-in methods owner | `36:2`; TOTP row `36:127`–`36:132` | Existing Authenticator app row and Manage action; runtime facts must replace sample ACTIVE |
| Shared operation chooser | `98:27` / card `98:28`; Authenticator row `98:35` | Approved chooser composition; no linked authenticator code-detail variant |
| Invalid-code panel | `46:150`; backdrop `52:428` | Existing 420×118 error surface; copy includes an unsuitable request-new-code instruction for TOTP |
| Shared field/error rules | `70:1257`; AMD-0009 §3/3A | Reuse approved field invalid/focus/error/summary mechanics rather than a new visual system |

File: `apNbwvAgdn1xIxYH1oiaSC`. Durable reference URL format: `https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=83-117` (substitute the listed node). Reference PNGs are evidence only and must never become screenshot-backed implementation assets.

Both actual file pages were read: Login `0:1` and Account Center `7:5`. Their complete top-level frame metadata contains exactly the three dedicated TOTP frames above. No dedicated inactive/empty/unavailable TOTP view, removal confirmation, reset confirmation, operation-code detail or TOTP mobile/error variant was found. Related generic dialogs, invalid-code panel, field/error reference and approved shared chooser were inspected before declaring these gaps.

Observed prototype bindings (read-only `reactions`):

- `36:131` Manage → `83:2`.
- `83:106` Set up new app → `83:117`.
- `83:289` Cancel → `83:2`.
- `83:291` Verify → `83:298`.
- `83:391` Done → `83:2`.
- Remove `83:115` and chooser Authenticator `98:35` have no reaction/destination. These clicks do not define a domain transition or confirmation state.

## Domain and routing boundary

FRD17 lines 161–181 identify TOTP as MFA/step-up, exclude SMS/voice initially, and forbid activation before first-code verification. Lines 191–212 require versioned assurance and fresh operation-bound proof for adding/removing/resetting authenticators. FRD17 §15 and doc31 rows U-AUTH-04/U-ACCT-03 require setup and safe method management. FigJam section `88:6720` (TUN-FLOW-ID-03) confirms eligible-method resolution `79:5515`, exact operation proof/owner revalidation `79:5551`/`79:5554`, one-time secret/QR and first-code verification `79:5569`, one-time handling boundary `79:5587`, and separate governed reset/revocation with explicit policy effects `79:5590`.

No six-digit appearance establishes an algorithm, period, drift window, attempt limit, assurance mapping, lifetime, replacement policy, session effect or login-MFA trigger. The supported profile and session effects must be explicit runtime inputs. No minimum remaining factor, new password requirement, simultaneous-app support or reset ordering is inferred from advisory prototype text. OpenBao versioned seed references remain backend-owned and are never visible user content.

Proposed canonical paths, following the actual independently navigable Figma screens and AMD-0009:

| View | Proposed pathname | Guard/reload requirement |
|---|---|---|
| Manage (`83:2`) | `/account/security/authenticator` | Current actor/session and safe current metadata; never fabricate active state |
| Setup (`83:117`) | `/account/security/authenticator/setup` | Exact authorized pending enrollment; QR/key only in permitted one-time presentation; reload cannot reveal consumed secret or create a new enrollment silently |
| Ready (`83:298`) | `/account/security/authenticator/ready` | Exact committed first-code result; pathname alone cannot prove activation |

These are proposals, not current inventory or server routes. The existing `/account/security/sign-in-methods` owns the methods list. U-AUTH-04 is currently PLANNED with no owner/path/Figma binding and needs an explicit inventory refinement. Independently navigable management and ready views also need distinct governed item ownership (IDs assigned by governance); one setup receipt cannot approve all three. Add same-screen disable/reset review and shared step-up local state under their owner URL; do not invent separate full-screen modal routes. The shared operation-code proof stays in StepUpDialog under the caller URL. No `/login/totp` route or primary-login MFA behavior is inferred.

Field contract: one editable code string, preserving leading zeroes; client schema mirrors the actual backend grammar/profile. The prototype's visual `123 456` grouping is not permission to accept spaces/hyphens or coerce to a number. Reuse `TextField`, `FieldError`, `FormErrorSummary`, `useTunnerForm`, `PrimaryButton`, ContentText and current modal/focus guards. Input mode and one-time-code autocomplete can guide entry; authoritative validation remains on the server. No code, setup key, URI, seed reference or operation secret goes in URLs/browser storage/logs. Unknown submission retains exact operation identity and uses safe read-only reconciliation; no success from step-up authorization alone.

## Bounded material proposal scopes for later review

1. **Truthful lifecycle and content adaptation.** Reuse the existing three layouts and current neutral Account shell. Replace sample person/app brand/date/ACTIVE with trusted facts or approved neutral labels. The prototype QR/manual key and internal prototype/production note (`83:283`/`83:284`) are not runtime data or user copy. Avoid minimum-factor or multi-app promises and future backup-code/SMS actions. Show Ready only after exact first-code activation. Existing correct labels/layouts need no redesign.
2. **Availability and one-time presentation states.** Add the minimum inactive/enrollment-unavailable/pending/expired/consumed-presentation/unknown-result views inside those approved layouts, with actions determined by server state. Do not make refresh re-reveal a secret, mint a new enrollment or infer a policy choice. The concrete screens depend on the final safe recovery projection and profile.
3. **Disable/reset review and terminal feedback.** Reuse the current 500×330 shared confirmation composition (Figma `46:44`, generic remove-trusted-device reference; previously adapted for passkey removal), shared step-up, and existing feedback sizing/focus guards. Exact TOTP consequences, eligible alternatives, session impact, cancellation and unknown-result copy must follow the explicit backend contract. No TOTP-specific confirmation exists in the current file; the visible Remove panel alone is insufficient coverage.
4. **Code proof/error detail and responsive accessibility.** Keep chooser `98:28`/row `98:35`, compose the actual code entry from approved field/error primitives, and adapt `46:150` wording without a resend-code action. Preserve `83:287`'s approved field geometry where applicable. Prepare mobile reflow of QR/manual key/code/actions/guidance and the three full screens at 390/320, plus content-fitting error/unknown states and contrast/focus corrections. Existing mobile billing frames are shared shell references, not approved TOTP compositions. No production detail/mobile design is invented here.

The first three desktop screens and existing chooser/field/error primitives are already approved reference coverage. The four scopes describe material adaptations to make concrete later; they are not a consent receipt or a request to approve unseen states. A later packet should show actual desktop/mobile proposed renders against these references before asking the Product Owner. None of the passkey six-scope consent is treated as blanket TOTP approval.

Specific contrast preparation: the approved setup field placeholder is `#94a8c2` on `#fafcff` (20px medium), calculated sRGB contrast 2.365:1; the same pale labels on white are 2.431:1. Both fall below 4.5:1 for normal-size text. The existing body token `#475970` on white is 7.165:1. A later concrete scope-4 proposal should show an approved existing accessible token in the affected slots, preserving geometry; this calculation is design inspection, not a rendered application accessibility PASS.

Future checks will map each route/form/component separately to URL-001..006, VAL-001..008 and UX-FID-001..008. One-time secret handling, exact operation binding, empty/error/expiry/cancel/unknown/success, policy-selected session termination, keyboard/focus, contrast, responsiveness and full-height summary/action geometry are applicable. No tests, server, native ceremony or acceptance run was started during this preparation. Figma/FigJam/Drive and production source remain unchanged.
