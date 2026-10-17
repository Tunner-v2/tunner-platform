# P2-004 registration, contact proof and normal Personal Information

Approved PDR-0011/AMD-0007 replaces the global adult-only Account admission rule prospectively. Historical PDR-0007 evidence and previously failed operations are preserved without relabeling or automatic activation.

## Composition

The User BFF stays opt-in/health-only without IRegistrationPolicySource. Explicit registration providers, Identity store, Data Protection, configured BrowserOrigin, approved publications, IAccountAgePolicySource and IAccountConsentEvidenceSource are mandatory. AccountAgePolicyCatalog uses the existing effective-policy resolver and trusted Account jurisdiction; no browser jurisdiction/consent claim is authority. Missing/ambiguous policy refuses processing. Fixture thresholds and retention/lengths are test-only.

## Private browser contract

See [OpenAPI](../contracts/registration-onboarding.openapi.json). All mutations require HTTPS, configured origin, framework X-Tunner-CSRF and expiring Secure/HttpOnly/SameSite=Strict registration context. This cookie grants no authenticated session. Real network peers use the existing server-secret HMAC abuse partition. Content-length and chunked bodies are bounded before strict JSON schema processing; obsolete DOB-at-registration or forged capability/consent properties fail safely.

| Route | Behavior |
| --- | --- |
| GET /registration/options | Published copy/legal links, current policy lengths/versions, server time and antiforgery token. |
| POST /registration/register | Email/password/confirmation, current notice versions and acknowledgment, stable idempotencyKey. No DOB or age gate. |
| POST /registration/verify | Bound operation, idempotencyKey, expectedVersion and code. Commits winning credential/contact proof, then opens Personal Information. Account remains VERIFICATION_PENDING. |
| POST /registration/personal-information | Verified operation/version, required DOB and approved optional full/display names. Server evaluates age and applicable consent, persists protected Account profile and safe versioned evidence, then activates or requests applicable consent. |
| POST /registration/continue | Bound operation/version only. Rechecks current trusted consent evidence and age policy; client cannot assert completion. |
| POST /registration/resend | Proof-stage only, current version, cooldown/attempt/resend policy and stable retry identity. |
| GET /registration/status/{key} | Browser-bound recovery after uncertain submission. No password or DOB repost, no GET activation. |

nextAction is VERIFY_CONTACT, PERSONAL_INFORMATION, CONSENT_REQUIRED or COMPLETE. Safe result provenance includes operation, correlation, version and applicable policy/evidence references. Verified email/field-limit projection is generated within the verified onboarding boundary and omitted from persisted replay receipts. No raw DOB/name is returned by this interface. Account existence is not disclosed before proof. Existing verified Accounts are not duplicated, linked or reset.

## Account and commercial behavior

Contact proof and Personal Information are separate transactions. Account activation occurs atomically with current safe age evidence, policy-required consent satisfaction and AccountActivated outbox event. Account history/version and credential/contact guards remain authoritative. Under18 with no additional consent requirement activates normally. Policy-required consent can delay ordinary onboarding only for that policy, without inventing a minor Account state. No authentication session or Product membership is created by registration; their owner flows have no age-only denial introduced here.

The shared evaluator reads one UTC instant and calendar years, including leap boundaries. ReadCurrentAsync re-evaluates protected Account material under current policy at action time, so turning18 does not replace AccountId or rewrite historical evidence. IndependentCommercialEligible is an age capability only: it never grants BillingAuthority, paid terms, payment-method access or funds authority. Under18 returns PAYER_CONTEXT_REQUIRED for existing delegated/sponsored payer resolution. Missing/expired/incompatible profile evidence returns unknown and cannot independently authorize a payer.

Trusted consent evidence is Account/policy/time/actor/verification bound. Guardian/authorized consent is normal; school evidence and stronger proof apply only when active policy permits/requires them. Consent grants no payer authority. Billing/Subscription modules are not implemented in this P2 slice; existing FRD18/21 and FigJam47/49/60 own those future actions.

## Privacy, migration and delivery

DOB and profile names are protected together in Account-owned storage with a separate Data Protection purpose. DOB is RESTRICTED_PERSONAL. Retention/processing references and duration are explicit approved inputs. Expired or incompatible material cannot authorize current capability. Bounded purge removes protected payload while retaining safe immutable evaluation history. Production key custody, scheduler and consent-owner adapter remain explicit composition responsibilities; no production values are invented.

New forward migrations remove the adult-only ACTIVE check, add Account profile/history, and add onboarding Stage with LEGACY backfill while retaining historical age columns/provenance. Old migrations are unchanged. Populated downgrade is refused. Old failed AGE_INELIGIBLE operations remain failed under historical PDR-0007; no automatic reactivation occurs.

Pending credential/proof material is separately protected and cleared after proof/expiry. Stable DeliveryId and reference-only shared outbox retain retry behavior. Queue acceptance is not provider delivery. No request/body logging, raw DOB events/webhooks or browser persistence is added. Raw passwords, confirmation and DOB clear after submit, including uncertain requests; original registration key recovers state.

## UX and follow-on work

Initial editable Figma70:270 contains email/password/confirmation/notices. Obsolete DOB-only187:2 is hidden and labeled historical. Existing normal profile35:107 adds DOB; onboarding derivative198:2 reuses its approved controls. Verification/result references remain70:539/70:630. Existing shared controls, local Geist/Inter and intrinsic SVG assets are reused. Desktop and320px states are checked with WCAG2/2.1/2.2 AA automation and visual inspection.

Content proposal stays DRAFT. Runtime requires approved publication/legal/policy/provider configuration. Production sign-in/session is P2-005, Product authorization is P2-014, and delivery/consent/maintenance orchestration is P2-016. This change does not claim those later features or production activation.

Exact-SHA tests, preserved prior receipts, role reviews and ownership are in TUN-P2-004-VALIDATION.json. Protected-main and existing P1-RV001 security/release issues remain unwaived.
