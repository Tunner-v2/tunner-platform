> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 29 — Privacy, Data Lifecycle & Processing Governance FRD

## 1. Authority and traceability

Fulfills FigJam **142–150** and global-market privacy requirements. Privacy coordinates Tunner-held data rights/governance while owner domains remain authoritative for their records. There is **no central PII lake** and Privacy cannot bypass Finance/Audit/Security/retention holds.

## 2. Canonical registries/resources

- `DataClassDefinition`;
- `DataInventoryEntry`;
- `ProcessingActivityVersion` / `ProcessingGovernanceContract`;
- `RetentionPolicyVersion` / `RetentionClass`;
- `RetentionHold`;
- `ArchiveRecord` / restore index;
- `PrivacyPolicyVersion`;
- `PrivacyRequest`;
- `PrivacyOwnerTask`;
- `PrivacyDisclosureExport` metadata;
- `PrivacyConflictCase`;
- `PrivacyIncident`;
- `Processor/SubprocessorVersion`;
- `Transfer/ResidencyPolicyVersion`.

## 3. Data classification

At minimum:

```text
PUBLIC
INTERNAL
CONFIDENTIAL
RESTRICTED_PERSONAL
RESTRICTED_FINANCIAL
RESTRICTED_SECURITY
SECRET_CREDENTIAL_REFERENCE
```

Actual field/resource classification is registry-driven. Logs/telemetry are subject to the same classification/minimization rules and cannot become an uncontrolled PII replica.

Specific high-sensitivity baselines:

- Merchant KYC identity/business evidence and raw document objects are `RESTRICTED_PERSONAL` and/or `RESTRICTED_FINANCIAL`; access is purpose-scoped, step-up protected and audited.
- registration `date_of_birth` is `RESTRICTED_PERSONAL`; under PDR-0007 it is collected for age eligibility and is retained only when an approved Processing Governance Contract/Retention Policy requires retention. Otherwise only the derived 18+ eligibility evidence is durable.

## 4. Data inventory

Every authoritative resource/data class records:

- owning domain;
- purpose(s);
- data/subject category;
- sensitivity;
- physical/logical stores;
- recipients/processors;
- Product vs Tunner authority;
- retention class;
- residency/transfer profile;
- privacy request capabilities;
- encryption/tokenization/redaction requirements.

A schema migration adding a materially new personal-data field requires inventory/processing-governance review before production activation.

## 5. Processing Governance Contract

States:

```text
DRAFT
UNDER_REVIEW
ACTIVE
SUPERSEDED
RETIRED
BLOCKED
```

Activation validates:

- purpose/necessity/minimization;
- legal/contract authority reference supplied by approved policy;
- notice/consent requirements where applicable;
- processor/subprocessor and transfer/locality contract;
- retention/hold behavior;
- privacy request coverage;
- incident/breach handling;
- security controls;
- DPIA/risk assessment trigger when active policy requires it.

Developers do not invent legal basis; the system requires an approved policy reference before a governed processing activity can become ACTIVE.

## 6. Retention policy

Retention duration is **data-class/jurisdiction/contract/purpose driven**, not a global hardcoded number.

A `RetentionPolicyVersion` must define before production use:

- trigger (`created_at`, `closed_at`, `contract_end`, `financial_period_end`, etc.);
- minimum/maximum period or event rule;
- archive eligibility;
- anonymization vs deletion action;
- hold precedence;
- jurisdiction/legal-entity scope;
- restore/evidence behavior.

This is implementation-complete even when the exact legal period differs by market: market activation is blocked until required policy values exist.

## 7. Record lifecycle

Canonical lifecycle is independent of business status:

```text
HOT
WARM
ARCHIVE_ELIGIBLE
ARCHIVED
RESTORE_REQUESTED
RESTORED
PURGE_ELIGIBLE
PURGED_OR_ANONYMIZED
```

Owner-domain data remains in its authoritative store unless approved archival design moves cold binary/history material. PostgreSQL keeps authoritative lifecycle metadata and references.

Cloudflare R2 is used for eligible binary/archive artifacts behind `IObjectStorage`; R2 lifecycle/bucket-lock capabilities are infrastructure controls, while Tunner PostgreSQL policy/evidence remains authority.

## 8. Retention holds

`RetentionHold` states:

```text
ACTIVE
RELEASED
EXPIRED_IF_POLICY_ALLOWS
```

A hold is scoped to stable resource/data/case IDs and a reason/policy/authority. Active hold blocks destructive purge/anonymization for the protected evidence. Privacy/operator cannot manually bypass a valid hold.

Hold release is a governed operation with evidence.

## 9. Privacy Request lifecycle

Canonical states:

```text
RECEIVED
IDENTITY_VERIFICATION_REQUIRED
VERIFIED
SCOPING
IN_PROGRESS
WAITING_OWNER_DOMAIN
CONFLICT_REVIEW
RESPONSE_READY
COMPLETED
DENIED_OR_LIMITED_BY_POLICY
CANCELED
```

Request types include policy-defined forms of:

- access/export;
- correction;
- deletion/erasure;
- restriction;
- portability where applicable;
- consent/preference withdrawal.

Availability is resolved by current Privacy Policy, market, subject relationship and contract; UI never promises a right globally simply because the feature exists.

## 10. Identity verification for privacy requests

Verification uses Accounts/Identity contract and proportional proof. Privacy does not invent a second credential system. Request identity proof evidence is stored minimally and linked by reference.

## 11. Owner-domain discovery

Each owner domain implements a registered `PrivacyDiscoveryHandler` supporting:

- subject/resource lookup by stable Tunner identifiers;
- data-category inventory;
- export projection with redaction/classification;
- correction eligibility/action;
- deletion/anonymization eligibility/action;
- restriction flag/action if owned by that domain;
- conflict/retention blocker response.

Privacy Coordinator orchestrates tasks; it never copies all data into its own database as a new authority.

## 12. Export package

Export generation uses:

- stable request/export IDs;
- machine-readable manifest;
- human-readable summary where policy requires;
- source domain/data category;
- timestamp/version;
- integrity hash;
- access-expiring download authorization;
- R2 protected object where large binary package is needed.

Export object expiry does not delete the immutable request/audit metadata.

## 13. Erasure/anonymization and conflicts

Before destructive action, each owner domain evaluates:

- financial/tax retention;
- audit/security evidence;
- legal/contract hold;
- unresolved dispute/support/risk case;
- Product-owned data boundary;
- technical dependency.

Conflict opens `PrivacyConflictCase`; required retained data is minimized/restricted according to policy rather than falsely reported deleted.

Financial ledger/journal/invoice evidence is never modified solely to satisfy a privacy request where active retention/integrity policy requires preservation. Identifiers may be pseudonymized/anonymized only through an approved owner-domain contract preserving referential/audit integrity.

## 14. Product boundary

Tunner privacy coordination covers Tunner-held data and explicit Product integration duties only. It cannot delete Product workspace/member/entitlement/usage data directly.

For a Product contractual privacy task, Tunner creates an integration task/event and tracks Product acknowledgement/result separately; Product remains owner of Product-local execution.

## 15. Processor/subprocessor governance

Provider additions/changes such as Stripe, Resend, Cloudflare, identity providers or future cloud services require a versioned processor record containing:

- service/purpose;
- data classes;
- contract/DPA reference;
- subprocessors/change mechanism;
- storage/processing location profile;
- transfer/residency mechanism reference;
- breach/incident duty;
- return/deletion terms;
- effective dates.

No provider is activated for a processing activity whose governance contract blocks it.

## 16. Residency/data_region

Every Product/environment has a configurable `data_region` / `ResidencyPolicy` capability. No strict residency promise is made until deployment/provider jurisdiction is selected and policy activated.

For R2, location hints are not treated as residency guarantees; jurisdictional restrictions are used where an approved residency policy requires a supported jurisdiction.

Cross-border processing is governed by active privacy/processor policy; home jurisdiction alone does not determine all applicable privacy obligations.

## 17. Privacy Incident

`PrivacyIncident` links Security/Platform incident but retains privacy assessment versions.

Lifecycle:

```text
OPEN
CONTAINMENT
SCOPE_ASSESSMENT
MATERIALITY_ASSESSMENT
NOTIFICATION_DECISION
COMMUNICATION_IN_PROGRESS
REMEDIATION
CLOSED
```

Notification duties/deadlines/audiences come from active jurisdiction/contract policy. The system creates durable due/escalation timers from policy; no developer invents universal breach deadlines.

Material fact changes create a new assessment/notification-decision version, never rewrite the prior decision.

## 18. Consent/preferences boundary

Privacy/Communication policy stores consent/notice evidence only where applicable. Consent is not used as a generic legal basis for all processing and developers cannot add “consent=true” to bypass governance.

Marketing preference/consent execution is in Notification domain but references Privacy/Communication policy versions.

## 19. APIs/events/UI

Admin Privacy Control Center requires:

- requests/cases;
- inventory/processing activities;
- retention/holds;
- processor/subprocessor registry;
- incident assessments;
- conflict resolution;
- evidence export.

Events include `PrivacyRequestReceived/Completed`, `RetentionHoldApplied/Released`, `ProcessingGovernanceActivated`, `RecordArchived/Restored/Purged`, `PrivacyIncidentOpened`, `PrivacyNotificationDecisionChanged`.

## 20. Acceptance cases

- privacy request cannot bypass active financial/legal hold;
- owner-domain export uses exact current/historical authorized data and redaction policy;
- Product-local data request becomes Product integration task, not direct Tunner mutation;
- R2 archived object cannot be purged while PostgreSQL hold is active;
- processing activity with missing processor/transfer control cannot activate;
- policy deadline creates timer/escalation but timer does not fabricate legal decision;
- historical privacy notification decision remains visible after material reassessment;
- no central privacy table becomes a duplicate master copy of Account/Finance/Product data.
