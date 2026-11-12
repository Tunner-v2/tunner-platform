# P2-011 Account recovery refinement

Status: IN_PROGRESS. Native startup eligibility passed before implementation. First containment slice and approved presentation are committed at e61b246a9e5c73f9fbe640fe3eeac6278516120e; current-source native qualification is in progress. This is not whole-feature acceptance or restored access.

## Resolved authority

FRD17 section10 fixes proof precedence: active strong authenticator, valid recovery code, current verified-contact challenge plus risk/context, then governed Identity Verification/Security review. Password reset alone cannot authorize a different factor; Support cannot bypass proof. RecoveryResult explicitly records every policy-selected session, authenticator, MFA reenrollment and restriction effect. No security questions.

Live read-only FigJam Flow42/TUN-FLOW-ID-07, section88:6732, matches FRD17. Marker83:6171 resolves the contract to FRD17 section10. Nodes83:6093/6096 require enumeration-safe resolution/current risk and state;6105/6108 bind the original RecoveryCase, policy, restoration goal and proof hierarchy;6123/6126 govern review;6138/6144 restrict effects to the affected method and preserve new-contact verification;6147/6150 require immutable results, unresolved downstream effects and policy-authorized notification;6159/6162/6165/6168 prohibit secrets/financial-data-only proof/unrelated-factor reset and require explicit session/device/token effects. The contact/device sketch is illustrative, not a replacement proof order.

References: docs/authority/current/development/17_Account_Identity_and_Session_FRD.md;32_Domain_API_Event_Data_and_Policy_Contract_Catalog.md;29_Privacy_Data_Lifecycle_and_Processing_Governance_FRD.md; PDR-0011; AMD-0007/0008/0009. Architecture board: https://www.figma.com/board/bdJwXt1U7n3iyluSD21OYK/Tunner-Platform-Architecture?node-id=83-6171. No new business decision gap was found by the focused AMD-0005 authority pass.

## Ownership and reuse

Identity owns the durable RecoveryCase, original request/result lineage, current proof admission and explicit effects. Preserve the Account state, contact, eligibility/consent and security restrictions; age alone cannot deny ordinary recovery. CLOSED-account correction remains governed and cannot reverse anonymization/purge.

Reuse src/Modules/Identity/RecoveryCodeConsumption.cs and IRecoveryCodeConsumptionOwner Admit/Apply/CurrentAt. The existing executor owns the transaction: the owner reads actual case/policy state, acquires the existing common locks in order and applies effects inside that same transaction. No nested transaction, earlier-order lock after entry claim or provider/network I/O inside it. Revalidate after awaited work and reconcile only the actual original completed outcome.

Reuse native authenticator verification, verifier-safe recovery code consumption, session owners and transactional outbox. Authenticated StepUp cannot become anonymous/locked-out recovery by bypassing its session guard. Registration challenge mechanics may be factored/reused; registration verification/activation cannot serve as recovery contact admission. Contact evidence must be current and accompanied by trusted risk/context admission. Governed review requires an exact case-bound authoritative disposition; Support receives no override.

Policies must explicitly admit the restoration goal, proof combinations/currentness, limits and effects. Missing/unqualified inputs remain unavailable; no production duration, code format, default risk score, provider capability, session effect or legal basis is chosen here. Notifications record an owner intent independently of actual delivery. Contact change and other downstream workflows remain with their owners.

## Small vertical slice

First implement a stable original RecoveryCase/idempotency boundary, current hierarchy admission, the real009 code-consumption owner adapter, and one explicitly policy-selected effect/RecoveryResult/outbox transaction. Prove duplicate/original-result reconciliation, competing one-use consumption, rollback and policy/source drift before browser integration. This slice does not itself claim completion of every proof tier or the whole feature.

## Visual authority and pending proposal

Live Platform UX Login page0:1 has no dedicated recovery/reset screen. U-AUTH-07 is PLANNED with no approved pathname/form/Figma mapping. Account Center34:278 and48:101/52:551 configure authenticators; they do not implement RecoveryCase execution. This is UI_REQUIRED for affected production UI, not a missing domain contract or an excuse to block independent backend refinement.

Reuse the approved Login2:4 shell/theme/fields/footer; verification70:539/70:1257/46:150 styling; chooser98:27/28/35 presentation; result46:116/120 and98:44 patterns. Shared TextField/useTunnerForm/errors/ModalDialog/content/routes remain the foundation. Setup/code-display components and authenticated proof state are not recovery admission authority.

The Product Owner approved the pictured neutral entry, original-case proof and case status/result scopes 011-V01–V03, including /recovery,/recovery/verify,/recovery/result, through the exact receipt governance/evidence/TUN-P2-011-VISUAL-CONSENT.json. Shared presentation is implemented and focused browser checks pass; production routing/backend integration and per-item visual acceptance remain pending. Password/factor repair presentation requires its own bounded review when the owner workflow needs it. Figma/Drive/FigJam remain read-only; the owner-held design backfill remains tracked.

## Gates and next action

007/008 source-currentness is reconciled through their existing envelopes using actual734828a shared regressions and unchanged-core/retained UI evidence, preserving all old execution identities and VALIDATION histories. 010 is DONE with explicit scoped Product acceptance. Eleven activated role contracts, fresh targeted context, native work eligibility and source/revision ownership must precede implementation.

Startup context110554c was CURRENT and work start was ELIGIBLE before implementation; all eleven startup role reviews passed. Five compatibility revisions remain ACTIVE throughout011. Preserve the passing presentation evidence and immutable native attempts. Current e61b246 protocol17 passes; resource admission checks the bounded two-worker capacity remedy without changing deadlines, migrations or storage limits. After admission passes, execute PRIVATE_RECOVERY81 with the five new case tests first. Do not close the SQL-integrity or capacity defects before actual runtime proof.

Next functional slice: anonymous original-entry resolution and actual policy-selected credential repair, reusing protected browser/CSRF, native password validation/hashing and the existing code-consumption transaction. Keep Account/session/set selection server-owned; preserve the original case and stronger-proof precedence. Containment is a separate immutable outcome, not a RecoveryCompleted event or an access-restoration claim. Missing production policy remains unavailable; this implementation plan does not select it. Complete remaining strong/contact/risk/governed-review integration, HTTP/routes, per-item UI gates and acceptance before feature DONE.
