# P2-007 — Passkeys

Implement registration, primary authentication and governed revocation under FRD17, catalog32, ADR0006 and current PDR0011/AMD0007/0008/0009. The prerequisite requirements audit and current P2 source gates pass; original closed work is preserved.

Use existing ASP.NET Core Identity schema3/store and ceremony verification APIs. Reuse Tunner session/restriction/abuse enforcement, exact operation step-up, policy resolution, transaction/outbox and current UI primitives. Do not create a parallel crypto verifier, identity store, cookie-session authority or deployment.

- Require explicit versioned RP/origin/ceremony/security/processing/retention/content configuration; no production fallback. Local fixtures stay isolated and synthetic.
- Enrollment requires the approved current actor/operation proof. Keep credential inactive until full framework verification and atomic persistence; bind actor/session/origin/policy/action/version, expiry and one-time use.
- Primary assertion feeds existing authoritative Account/restriction/abuse/session behavior; preserve ordinary minor-account eligibility. Classification uses verified method/context and approved assurance policy; no blanket assurance upgrade.
- Revocation checks ownership, current eligibility/version and operation step-up before atomic change. Preserve evidence and required post-commit activity/notification contracts.
- Avoid sensitive URLs/logs; bounded strict backend validation remains authoritative. No passkey private key is received, stored or logged; browser receives only permitted ceremony data and Admin views expose safe metadata.
- Reconcile unknown responses to the exact original operation; do not blindly replay a mutation or attribute another tab’s result.

The approved Add Passkey overlay is520x410 (46:24,52:291); security/method compositions are13:2 and36:2. Sample MFA/activity/identity content is not factual runtime state. U-AUTH-05/U-ACCT-03 route and screen/modal ownership are refined from these actual designs. Reuse approved shared patterns; request explicit scoped Product Owner approval for a genuinely missing or materially adapted visual before final production UX, after checking related designs. All Figma/FigJam/Drive remain read-only.

Validation must include real PostgreSQL/HTTPS and framework-generated challenge/signature handling, wrong origin/RP/challenge/type/actor/session/policy and replay, inactive enrollment, revocation/bypass/concurrency/rollback/unknown responses, password/registration regression, approved browser/keyboard/accessibility/visual/responsive checks, and committed source ownership. Native and browser failures are recorded rather than converted into acceptance.

Production configuration/publication and any necessary specific visual approval remain separate. No implementation or test PASS is claimed by this refinement. Inherited P1 proof drift stays in TODO-TUN-P2-017-GOV002 and cannot be reported as globally clear.
