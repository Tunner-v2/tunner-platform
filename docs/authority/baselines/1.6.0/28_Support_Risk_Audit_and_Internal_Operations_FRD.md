> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 28 — Support, Risk, Audit, Governance & Internal Operations FRD

## 1. Authority and traceability

Fulfills FigJam **100–122** and operational-control portions of Finance, Identity, Provider and Platform workflows. These domains coordinate evidence, authorization and exception handling but never become shadow owners of Payment, Identity, Subscription, Product or Provider business state.

## 2. Support Case

Canonical resource: `SupportCase` with stable `SUP-*` ID.

States:

```text
OPEN
TRIAGED
IN_PROGRESS
WAITING_CUSTOMER
WAITING_INTERNAL
WAITING_EXTERNAL
RESOLUTION_READY
RESOLVED
CLOSED
REOPENED
CANCELED_AS_DUPLICATE_OR_INVALID
```

Each case binds:

- category/subcategory;
- severity/priority;
- Product/Account/Billing/financial context refs;
- owner/team;
- SLA policy/version;
- response/resolution timers;
- customer communication refs;
- evidence refs;
- escalation/action requests;
- closure/reopen evidence.

Support notes are append-only/versioned. Corrections create a new note/version; sensitive notes have explicit classification and access scope.

## 3. Support investigation

Support obtains **permission-filtered read models/evidence links** from owner domains. It never directly edits owner-domain rows.

A support resolution requiring authoritative mutation creates a typed `PrivilegedActionRequest` for the owning domain, for example:

- resend/repair notification;
- revoke session after approved security flow;
- request refund review;
- reopen reconciliation;
- refresh provider requirement evidence;
- rerun safe integration delivery.

The action request contains exact target IDs/versions, reason, requested operation, policy, approval/step-up requirements and correlation.

## 4. Support impersonation / customer access

Initial security rule: **no unrestricted customer impersonation**.

Where troubleshooting requires customer-context reproduction, use one of:

- read-only Support projection;
- explicit customer-provided evidence;
- synthetic/test account;
- governed time-bound support-access capability that does not expose customer credentials and is separately audited.

Any future interactive “view as customer” capability requires a dedicated ADR/PDR and must preserve customer-visible/auditable access evidence where policy requires it.

## 5. Risk Case

`RiskCase` states:

```text
OPEN
TRIAGE
ASSESSMENT
WAITING_EVIDENCE
WAITING_REVIEW
DECIDED
MONITORING
RESOLVED
CLOSED
```

Risk inputs can include Identity/Security signals, provider requirements, merchant evidence, Payment patterns, Support signals and policy exceptions. Risk never directly rewrites owner-domain state.

`RiskDecision` is immutable and contains:

- policy/version;
- subject/scope;
- evidence refs;
- decision `ALLOW | DENY | ALLOW_WITH_CONDITIONS | REVIEW_REQUIRED`;
- rationale code + protected analyst notes;
- validity/effective interval;
- required follow-up/reverification.

Owner domains consume a current decision synchronously when it is an execution gate.

## 6. Identity Verification Review

Where a policy requires stronger identity verification, Tunner creates an `IdentityVerificationRecord` plus review/evidence refs. Provider/raw documents are minimized and not copied into general support/admin tables.

Outcomes:

```text
PENDING
VERIFIED
REJECTED
REVIEW_REQUIRED
EXPIRED_REVERIFICATION_REQUIRED
```

Verification result is evidence for the Identity/Risk owner; it is not Product membership or billing authority.

## 7. Provider-account compliance review

Provider/merchant account evidence can trigger a `ProviderComplianceReview`. This records provider requirements, business-model eligibility and decision conditions. It does not substitute for provider KYC capability state and does not create Tunner financial-regulatory registration assumptions.

Per PDR-0006, Connected Product KYC/KYB/compliance review is Tunner-managed. Compliance/Risk maintains the canonical `ProductKycCase` and governed Product evidence, including raw document references only where required by active Tunner service/compliance policy. Provider verification of Tunner's own merchant/account relationship is separate Provider Operations evidence and does not replace ProductKycCase authority.

KYC document reveal/download requires purpose-scoped workforce permission, step-up and immutable access audit. Support receives redacted/status views by default and cannot browse raw KYC evidence merely because a Support Case exists.

## 8. Policy exceptions

`PolicyException` is always:

- narrow in subject/action/scope;
- reasoned;
- time-bound or one-use where appropriate;
- approved under SoD/step-up policy;
- immutable after decision;
- non-transferring of underlying authority.

No “permanent override” checkbox exists.

States:

```text
REQUESTED
UNDER_REVIEW
APPROVED
REJECTED
ACTIVE
CONSUMED
EXPIRED
REVOKED
```

## 9. Audit Event

`AuditEvent` is append-only and includes:

- stable event ID/time;
- actor principal and actor type;
- workforce/customer/non-human/AI identity refs;
- action/operation ID;
- target resource IDs/versions;
- permission/scope/policy versions;
- reason where required;
- step-up/approval refs;
- before/after version refs, not uncontrolled full sensitive object copies;
- provider/evidence correlation;
- result.

Audit records are tamper-evident/integrity-checked according to implementation policy and retention class. Audit search is permission-aware and access to sensitive audit evidence is itself audited.

## 10. Approval control

`ApprovalRequest` states:

```text
REQUESTED
PENDING
APPROVED
REJECTED
EXPIRED
CANCELED
CONSUMED
```

Approval binds the exact operation/target/version/amount/scope and cannot be reused after material change.

At execution the owner domain revalidates:

- expected versions;
- approval validity;
- actor authorization;
- step-up evidence;
- dependency/market/provider readiness.

Approval is authorization evidence, never downstream success.

## 11. Workforce identity and access

Workforce identity is separate from Tunner customer Account.

Canonical resources:

- `WorkforceIdentity`;
- `InternalRoleVersion`;
- `PermissionVersion`;
- `ScopeVersion`;
- `RoleAssignment`;
- `AccessRequest`;
- `PrivilegedSession`;
- `AccessCertificationCampaign`;
- `SoDConflict`.

No frontend role label is authorization. Backend `EffectiveAccessResolver` evaluates permission + scope + resource + current assurance + assignment/effective time.

## 12. Workforce lifecycle

Onboarding requires verified workforce identity, approved assignment and MFA/passkey posture appropriate to internal policy.

Offboarding:

- disables future authentication;
- immediately revokes workforce sessions;
- ends assignments/privileged sessions;
- invalidates pending access requests as policy requires;
- preserves historical audit/access evidence;
- triggers certification/remediation when ownership gaps result.

## 13. Privileged/JIT access

Privileged access is time-bound and operation/scope specific.

States:

```text
REQUESTED
APPROVED
ACTIVE
EXPIRED
REVOKED
CLOSED
```

A privileged session does not become a permanent Role Assignment. Activity performed under elevation records the privileged-session ref on every protected operation.

## 14. Segregation of duties

`SoDPolicy` defines incompatible combinations, for example subject to finance policy:

- requester cannot independently approve same high-impact action;
- journal adjustment author cannot be sole approver where threshold requires independent review;
- production secret rotation and approval can require separate principals;
- access certification reviewer cannot attest their own high-risk privilege without independent path.

Conflicts produce `SoDConflict`; exception requires narrow expiry/approval and does not rewrite role history.

## 15. Access certification

Periodic/event-driven campaigns snapshot effective access at campaign creation. Reviewer decisions are `RETAIN | REMOVE | MODIFY | ESCALATE`. Remediation is executed through normal assignment workflows and linked back to campaign evidence.

## 16. Governance policy lifecycle

Governance/Risk/Access policies use:

```text
DRAFT
UNDER_REVIEW
APPROVED
SCHEDULED
ACTIVE
SUPERSEDED
RETIRED
```

Publication includes impact review, approval and effective time. Used versions are never deleted.

## 17. Reporting and SLA

Support/risk/internal-access metrics are derived read models, never operational authority. SLA due dates are calculated from immutable policy/calendar versions. Changing SLA policy never rewrites historical case due calculations.

## 18. UI/control centers

Required Control Centers:

- Support Queue + Support Case Workspace;
- Risk/Compliance Case Center;
- Audit Explorer and Evidence Package;
- Approval Queue;
- Internal Access Control Center;
- Privileged Access/JIT Queue;
- Access Certification Campaign;
- SoD Conflict Review.

All mutation screens are guided, contextual and eligibility-driven. No generic CRUD for financial/security/access authority records.

## 19. Acceptance cases

- Support cannot directly set Payment/Account/Subscription status;
- expired approval cannot execute after target version changes;
- JIT access expires automatically and future protected calls fail;
- workforce offboarding revokes sessions and assignments without deleting audit history;
- Support cannot retrieve password/TOTP/recovery-code/passkey secret material;
- audit search applies evidence permissions and logs sensitive evidence access;
- policy exception cannot silently broaden scope or become permanent;
- risk decision gates owner workflow without direct owner-row mutation;
- SoD-conflicted actor cannot self-approve a protected operation when policy requires independence.
