# P2-007 — Passkeys

Implement registration, primary authentication and governed revocation under FRD17, catalog32, ADR0006 and current PDR0011/AMD0007/0008/0009. Initial entry gates are recorded at start; current readiness is measured by native gate receipts. Original closed work is preserved.

Use existing ASP.NET Core Identity schema3/store and ceremony verification APIs. Reuse Tunner session/restriction/abuse enforcement, exact operation step-up, policy resolution, transaction/outbox and current UI primitives. Do not create a parallel crypto verifier, identity store, cookie-session authority or deployment.

- Require explicit versioned RP/origin/ceremony/security/processing/retention/content configuration; no production fallback. Local fixtures stay isolated and synthetic.
- Enrollment requires the approved current actor/operation proof. Keep credential inactive until full framework verification and atomic persistence; bind actor/session/origin/policy/action/version, expiry and one-time use.
- Primary assertion feeds existing authoritative Account/restriction/abuse/session behavior; preserve ordinary minor-account eligibility. Classification uses verified method/context and approved assurance policy; no blanket assurance upgrade.
- Revocation checks ownership, current eligibility/version and operation step-up before atomic change. Preserve evidence and required post-commit activity/notification contracts.
- Avoid sensitive URLs/logs; bounded strict backend validation remains authoritative. No passkey private key is received, stored or logged; browser receives only permitted ceremony data and Admin views expose safe metadata.
- Reconcile unknown responses to the exact original operation; do not blindly replay a mutation or attribute another tab’s result.

The approved Add Passkey overlay is520x410 (46:24,52:291); security/method compositions are13:2 and36:2. Sample MFA/activity/identity content is not factual runtime state. U-AUTH-05/U-ACCT-03 route and screen/modal ownership are refined from these actual designs. Reuse approved shared patterns; request explicit scoped Product Owner approval for a genuinely missing or materially adapted visual before final production UX, after checking related designs. All Figma/FigJam/Drive remain read-only.

Validation must include real PostgreSQL/HTTPS and framework-generated challenge/signature handling, wrong origin/RP/challenge/type/actor/session/policy and replay, inactive enrollment, revocation/bypass/concurrency/rollback/unknown responses, password/registration regression, approved browser/keyboard/accessibility/visual/responsive checks, and committed source ownership. Native and browser failures are recorded rather than converted into acceptance.

Current scoped engineering results are recorded below. Production configuration/publication remains separate. Inherited P1 proof drift stays in TODO-TUN-P2-017-GOV002 and cannot be reported as globally clear.

## Implemented shared-operation proof and current evidence

The native passkey adapter now verifies the exact actor/session/operation/request/attempt/ceremony binding through the existing shared proof issuer, consumer and owner transaction. It reuses verified UV/BE/BS facts and approved runtime assurance mapping; primary sign-in never substitutes for fresh operation proof. Persisted source refresh under the account lock and final current authorization prevent stale tracked credentials or expiry across awaited checks. Credential lifecycle version and signed-counter/last-use concurrency remain independent.

Enrollment stays inactive until verified atomic completion. Protected original-result recovery preserves exact IDs and remember choice without restoring bearer/session material. Approved Ready→Login→signed-in→Account Security navigation and current logical-focus/error states use shared components and actual V01–V06/NAV01/NAV02 consent. Figma, FigJam and Drive stay read-only; owner backfill remains tracked separately.

Current source carrier: `745f9c2dc1b9060c4be0318b33b4019ae77e9e36`; production UI correction: `b0c04424136aace1b02c486baeaafd53ed61b8ec`. Actual native224 passed at `0efbb6e`; targeted recovery14 at `80a32ab`; current Edge60/Firefox60, legacy67, real HTTPS/PostgreSQL/Mailpit4, foundation16, architecture237, theme10/contracts21 and TypeScript pass at their recorded cohort SHAs. Repeated/overlapping cohorts are not counted as unique coverage. All eight scoped/global native UI checks pass, covering21liveitems and316checks with34explicit authority-supported exclusions.

Traceable results and source equivalence: [execution index](../../governance/evidence/p2-recovery-20261109/passkey-proof-nav/execution-index.json), [per-item UI evidence](../../governance/evidence/p2-recovery-20261109/passkey-proof-nav/ui/current-item-evidence.json), [defect verification](../../governance/evidence/TUN-P2-007-DEFECT-VERIFICATION-20261110.json). Historical RED, timing, launcher and Windows WebKit failures remain retained; canonical Strict cookie assertion was never weakened. The Windows Curl-port limitation stays in `TODO-TUN-P2-017-WEBKIT001`; this does not claim native Safari/physical-authenticator or production browser-matrix acceptance.

TOTP and federated fresh-proof integration remain owned by P2-008/P2-010. P2-017 conformance does not substitute for those implementations. Current parent-source/revision/lifecycle gates must pass before the next feature starts. No whole-P2, protected-main merge, production policy/publication, deployment or release acceptance is inferred.
