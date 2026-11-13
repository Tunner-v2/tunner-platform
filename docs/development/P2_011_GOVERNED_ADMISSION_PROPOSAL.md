# P2-011 governed recovery admission proposal

Status: BOTH DIRECTIONS APPROVED BY PRODUCT OWNER FOR THE USER'S AUTHORITY UPDATE. Exact typed contract wording and owner/source qualification remain draft. No authority amendment/mirror update, production activation or full fourth-tier acceptance is recorded by this document.

Consent: [direction-only receipt](../../governance/evidence/P2-011-GOVERNED-ADMISSION-DIRECTION-CONSENT.json), question `call_0f4439cc9ce5465eaace556e1d546671`, answer `Approve both recommended directions`, recorded 2026-11-13T17:10:12Z. The requesting user's Product Owner role was already established. The same directions do not need a second approval request; the user-held authority update and actual source/policy inputs remain outstanding.

Work owner: TUN-P2-011, P2 Account & Identity. Dependency: TODO-TUN-P2-011-GOVERNED001. The existing consumer has eight passing focused governed-recovery cases; those tests use synthetic trusted-owner projections. The production disposition owner/control providers and contact-independent subject admission are not implemented.

## Authority and remaining selection

The focused AMD-0005 pass and bounded decision-history expansion found the governing constraints but no approved selection of these two admission sources. This is INSUFFICIENT_CONTEXT for their production implementation, not a claim that a new amendment or business decision is necessarily required.

- [FRD17 sections 3, 4, 10 and 13](../authority/current/development/17_Account_Identity_and_Session_FRD.md) owns Account recovery, proof precedence, enumeration protection, exact effects and stable operation identity. Governed Identity Verification/Security review is the fourth tier; Support cannot bypass it.
- [FRD28 sections 3, 5, 6, 10 and 11](../authority/baselines/1.6.0/28_Support_Risk_Audit_and_Internal_Operations_FRD.md) keeps mutation with its domain owner. RiskDecision is immutable and records policy/version, subject/scope, evidence, decision, rationale, effective interval and follow-up. IdentityVerificationRecord has review/evidence references and the specified verification outcomes; the FRD does not supply its detailed field schema or a recovery-specific subject-binding mechanism. Approval binds the exact operation/target/version/scope, while the owner rechecks current controls. Workforce identity is separate from the customer Account.
- [Catalog32 sections 4, 10, 17, 18 and 19](../authority/current/development/32_Domain_API_Event_Data_and_Policy_Contract_Catalog.md) requires server-derived actor context, domain ownership, exact effective policy and fail-closed missing/ambiguous controls.
- [Plan11](../authority/current/development/11_Development_Planes_Milestones_and_P0_Backlog.md) places Account recovery in P2; shared Support foundations belong to P4 and Security Ops/Risk/Internal Access maturation to P6. This proposal does not authorize implementing those broader planes.

The following two directions now have bounded Product Owner consent for the user's authoritative documentation update. Proposed fields below remain recommendations for refinement, not fields asserted to exist in approved IdentityVerificationRecord or other authority schemas. Existing code contracts are implementation evidence, not authority.

## Approval scope 1: authoritative recovery disposition admission

Recommend an Identity/Security-owned immutable disposition authorizing one exact recovery effect on an already resolved original RecoveryCase. Support may route evidence and a typed owner request; it cannot issue the disposition, select an Account for mutation or turn a generic VERIFIED/APPROVED status into credential authority.

Proposed binding:

- Stable disposition reference/version and original RecoveryCase/request reference/version.
- Exact Account/version, recovery context, relevant current Account/user/contact facts and source versions/digests.
- Exact restoration goal, session effect and unresolved downstream effects; explicit effect encoding/digest, recovery policy/profile/contract versions and effective execution interval.
- Producing owner authority-policy reference/version and immutable control-evidence reference. That evidence resolves the actual server-derived actor authorization and every applicable verification, approval, step-up/SoD and dependency requirement selected by the approved policy.
- Processing/retention references and operation/correlation lineage, with owner-defined currentness/withdrawal evidence. A later correction or withdrawal preserves prior evidence through new versioned lineage; it does not rewrite a consumed disposition or imply that a completed effect is reversed.

The producing owner must independently validate these bindings and control evidence. The consumer receives a qualified current owner projection, revalidates it at execution and records the exact disposition with its immutable RecoveryResult and one-use witness. A Boolean permission or a captured successful status alone is insufficient. Missing, expired, changed, ambiguous or unqualified sources leave execution unavailable.

The approved direction for this scope is the owner boundary, exact operation/effect binding, qualified-source admission and evidence-only Support role. It does not approve a reviewer role, permission list, approval count, verification method, production policy, validity duration or provider integration. Applicable controls and their sources must be supplied by the responsible policy/security owners before activation.

## Approval scope 2: contact-independent recovery subject admission

Recommend that Identity derive the exact Account/version server-side from a qualified trusted verification record and its protected evidence, bound to the original recovery request/context. The resolved subject is an input to governed review, not a successful proof or an authorization to repair a credential.

Proposed subject-binding projection:

- Stable owner record/reference/version, qualified source/contract and effective policy/version.
- Exact Account/version and original request/context binding, plus evidence references demonstrating how the producing owner established that binding.
- Effective interval, currentness/withdrawal state and processing/retention references needed for admission.

These are proposed projection requirements. The approved FRD28 verification outcomes do not themselves establish these fields or the subject-binding procedure. Security/Identity must qualify the actual source, evidence method and permitted lookup before implementation.

Preserve the existing current verified-contact entry path. For a claimant without such contact, accept no caller-supplied Account selector, historical email match, generic verification status or Support assertion as sufficient subject proof. Historical contact may be considered only where it is already lawfully retained and an approved policy authorizes that use; it remains supporting evidence and cannot alone establish identity or mark a new contact verified. Do not create a new retention purpose or reverse anonymization/purge to recover an identifier.

Ambiguous, conflicting, stale, absent or anonymized subject evidence remains unavailable with enumeration-safe public behavior. Any new verified contact still follows its owning verification/change workflow. CLOSED-account correction remains a separately governed Security/Recovery operation before anonymization/purge, preserving the original closure evidence.

The approved direction for this scope is the trusted server-derived subject-binding boundary and the prohibition on identifier/status-only proof. It does not select an evidence provider, new customer identification method, historical-data retention period, document requirement or public recovery screen.

## Boundaries retained by both scopes

- Strong authenticator, recovery code, safe verified-contact/risk and governed-review precedence remains authoritative. Qualified stronger-tier unavailability is required; a lost contact does not automatically authorize review completion.
- Identity owns the Account/RecoveryCase/result and native credential effect. No Support/workforce override, customer impersonation, automatic login, unrelated-factor reset, Product entitlement or commercial/age/consent mutation is introduced.
- The existing consumer's password/stamp/result/outbox transaction and explicit unresolved effects remain bounded. Notification intent is not delivery, and password repair is not full Account restoration.
- No UI, production assurance/default, external service, legal basis, retention value or authority-source edit is approved by this proposal. Existing V01–V03 consent and pending V04 disposition retain their own scope.

## Minimum authority and contract updates

After review, the authority owner can record the two selections in the applicable approved decision/contract process without changing unrelated FRDs:

1. Clarify FRD17 section 10: the authoritative Identity/Security disposition owner, exact operation/effect binding, qualified current-control requirement, and contact-independent server-derived subject admission. Preserve the proof hierarchy and closure boundary.
2. Add the two typed owner projections and producer/consumer responsibilities to catalog32's Identity contracts, with references to policy/control sources and explicit unavailable/error/currentness behavior. Detail proposed fields only after approval.
3. Clarify FRD28 sections 6/10 only if the selected verification/approval source needs a recovery-specific binding projection; retain its existing outcomes, evidence ownership and workforce separation. Do not treat this as authorization to build generic Support/Risk/workforce workflows in P2.

Reference the resulting approved records from TUN-P2-011 and its existing TODO. An authority change requires verified mirror/context refresh and native eligibility before source implementation; this proposal never substitutes for that authority.

## Copy-ready authority wording for the user

The following text expresses the approved directions for the user's authoritative update. Its field families and descriptive contract labels are proposed, not approved canonical resource or member names. The user may incorporate them through the existing authority process; this repository document does not modify FRDs or the mirror.

### FRD17 section 10: governed recovery admission

> Governed fourth-tier recovery is authorized only by an immutable Identity/Security-owned disposition bound to the exact original RecoveryCase/request, Account and expected version, current relevant subject/context evidence, recovery policy/version, restoration goal, explicitly selected effects and effective execution interval. Its producing owner validates the current server-derived actor authorization and all applicable verification, approval, step-up/segregation-of-duties and dependency controls required by the active approved policy through qualified authoritative sources. Support may route evidence and a typed owner request; it cannot issue recovery authority or bypass proof precedence. Generic verification or approval status alone does not authorize a credential effect. Execution revalidates the disposition and required sources, preserves immutable one-use lineage and records every actual effect in RecoveryResult. Missing, stale, ambiguous, withdrawn or unqualified required evidence leaves execution unavailable.

> When no current verified contact is available, Identity may resolve the exact Account/version only from a qualified trusted verification record and its protected evidence, bound server-side to the original recovery request/context. Subject resolution is not itself recovery proof or authorization. Caller-supplied Account selectors, historical contact matches, generic verification status and Support assertions cannot alone establish that binding. Historical contact is supporting evidence only where already retained and authorized for this purpose; it is not sufficient proof and cannot verify a new contact. Preserve enumeration-safe behavior, the existing current-contact path, owner-controlled new-contact verification and the prohibition on reversing anonymization/purge. CLOSED-account correction retains its separate governed boundary.

### Catalog32 section 10: two proposed typed contract families

> **Trusted recovery subject-binding projection (proposed descriptive label):** produced by the qualified Identity/verification owner; carries stable source record/reference/version and contract qualification, the exact Account/expected version and original request/context, protected evidence references, applicable effective policy/version and interval, currentness/withdrawal evidence, and processing/retention references. The producer establishes the binding from authorized evidence; the caller does not choose the authoritative Account. The consumer rejects absent, conflicting, ambiguous, stale, anonymized or otherwise unqualified bindings without disclosing arbitrary Account existence. This projection does not authorize restoration or new-contact verification.

> **Governed recovery disposition projection (proposed descriptive label):** produced only by the authoritative Identity/Security owner after validating its applicable controls; carries immutable disposition reference/version, exact original RecoveryCase/request/version, Account/version and relevant subject/context/source bindings, recovery policy/profile/contract versions, explicit goal/session/other effects and unresolved downstream effects, an independently validated exact-effect witness/encoding, effective execution interval, producing authority-policy/version, protected control-evidence references and processing/retention lineage. Its current projection must identify an actual owner decision, not a caller Boolean or generic verification/approval outcome. The consuming Identity transaction rechecks exact bindings and applicable source currentness, prevents reuse and records the actual result/effects; correction/withdrawal preserves history and does not retroactively claim reversal of an executed effect.

> Both contract families resolve scope and policy by the catalog's existing effective-time rules. Their approved source contracts must define actual actor authorization, evidence admissibility, required control combinations, source versions, currentness/withdrawal and unavailable behavior. Missing qualification blocks only the dependent operation. Technical implementation schemas are refined after the authority update and do not select business policy values.

### Conditional FRD28 sections 6 and 10 clarification

> Where Identity Verification or approval evidence is consumed for governed Account recovery, its owning source provides the recovery-specific exact subject/operation/version/context binding required by the Identity contract and active policy. Verification and approval outcomes remain evidence; only the Identity/Security owner can authorize the exact recovery effect after revalidating applicable controls. Existing verification/approval states, separate workforce identity and evidence minimization remain unchanged.

Use this FRD28 clarification only if needed to describe the selected source's recovery binding; it does not introduce a generic workforce or Support implementation scope.

## Activation and proof

First qualify the real owner and control sources, their durable records and subject-binding contract. Refine the smallest Identity-owned vertical slice, register any shared closed-scope ownership through revision lineage, then implement only after native governance eligibility. Reuse existing Identity, policy, audit, outbox, native transaction and finite-test capabilities; do not add another framework or harness.

Required proofs extend the existing consumer tests with the actual producer/source boundary:

- Authorized and unauthorized actor/scope, required-control absence, changed approval/step-up/verification/dependency evidence, and effective-policy/currentness rejection.
- Exact case/Account/version/context/goal/effects binding, independent effect witness, interval edges, stale/withdrawn disposition, one-use competition and immutable original-result reconciliation.
- Subject mismatch, ambiguous or conflicting evidence, stale source/version, caller-selector rejection, unavailable current contact, historical-contact-only rejection and anonymization/purge preservation.
- Atomic failure/cancellation/rollback, real native credential/session effects and outbox evidence; current-contact regressions and enumeration-safe HTTP behavior.

Use committed-source execution identities and existing resource/cleanup guards. Passing synthetic consumer tests cannot qualify a production source. Any newly exposed UI requires its own approved Figma/consent, route/form and fidelity evidence. Production activation remains blocked until applicable policy/provider/configuration and acceptance gates pass.

## Inputs still required from policy and security owners

Supply references to the approved disposition/verification source contracts, permitted actor permissions/scopes, applicable verification and approval/step-up/SoD/dependency rules, evidence-admissibility and ambiguity rules, effective/withdrawal semantics, and processing/retention authorization. Supply actual approved policy versions, validity limits and production configuration separately. No values, approval counts, durations or legal conclusions are selected here.

Exact next action: the user incorporates the two approved directions into the authoritative FRD17/catalog32 records, with the conditional FRD28 clarification if applicable, and supplies or identifies the qualifying source/control/policy references. No repeat approval of the same directions is requested. After the authority update is verified and mirrored and context refreshed, rerun native eligibility for the smallest owner slice. Preserve TODO-TUN-P2-011-GOVERNED001 and keep unqualified admission unavailable while independently eligible work continues.
