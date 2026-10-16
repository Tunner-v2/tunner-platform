> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 00 — Authority, Change Control and Documentation Governance

## 1. Objective

Prevent business drift, AI hallucination, stale documentation, hidden changes and contradictory implementation.

## 2. Source classification

Every requirement used during development must be classified:

- `PRODUCT_DECISION` — explicit Product Owner decision.
- `DEV_AUTHORITY` — approved development specification.
- `AMENDMENT` — approved modification to an authority.
- `ARCH_FLOW` — canonical FigJam behavior / ownership contract.
- `UX_REFERENCE_ADMIN` — Figma Admin & Support reference.
- `UX_REFERENCE_USER` — Figma Account/User reference.
- `STANDARD` — external normative or best-practice source.
- `ADR` — technical architecture decision.
- `IMPLEMENTATION` — code-level choice that does not change business semantics.

The source classification must appear in work-item traceability.

## 3. Conflict rule

If sources conflict:

1. stop only the affected work item;
2. preserve all evidence;
3. identify the exact conflicting statements;
4. create `DEC-*` or `AMD-*`;
5. provide a recommended resolution with rationale and impact;
6. obtain Product Owner approval when business behavior changes;
7. update all downstream authorities;
8. resume from the approved baseline.

An AI agent must not “pick the most likely interpretation.”

## 4. Assumption and hallucination policy

An assumption is any behavior not supported by an approved source.

### Prohibited examples

- inventing a subscription grace period;
- assuming Tunner owns Product entitlements;
- choosing a legal retention period without policy;
- assuming a payment is failed because a provider timeout occurred;
- inventing an Admin screen because implementation needs a control;
- creating a new Product membership model in Tunner;
- treating an SDK package version as sufficient API compatibility evidence.

### Allowed engineering inference

Low-risk implementation details may be chosen when they do not change observable business behavior, security/compliance requirements or compatibility.

Example: naming a private helper method is not a Product Decision.

## 5. Product Decision object

```yaml
decision_id: DEC-0001
title: ""
status: PROPOSED | APPROVED | REJECTED | SUPERSEDED
problem: ""
options_considered: []
recommendation: ""
business_impact: ""
technical_impact: ""
security_compliance_impact: ""
affected_documents: []
affected_flows: []
affected_work_items: []
requested_by: ""
approved_by: ""
approved_at: null
supersedes: null
```

## 6. Amendment object

Approved specifications are never silently rewritten.

```yaml
amendment_id: AMD-0001
target_document: TUN-DEV-XXX
from_version: 1.0.0
to_version: 1.1.0
reason: ""
change_type: BUSINESS | CONTRACT | SECURITY | COMPLIANCE | TECHNICAL | UX
affected_flows: []
affected_contracts: []
migration_required: false
sdk_impact: NONE
release_impact: NONE
approval: []
```

The Git diff is evidence, but the amendment explains *why*.

## 7. Documentation lifecycle

`DRAFT → BASELINE_CANDIDATE → ACTIVE → AMENDED → SUPERSEDED → ARCHIVED`

Only ACTIVE + applicable approved amendments are implementation authority.

## 8. Documentation update rule

A code/contract change that affects documented behavior cannot merge until one of these is true:

- documentation updated in the same PR;
- a separate documentation work item is explicitly linked and the governance policy allows deferred documentation;
- change is internal-only and documented as `NO_DOC_IMPACT`.

For public API, SDK, security, compliance, data schema, business lifecycle and operational behavior, deferred documentation is not allowed.

## 9. R&D gate

Before implementing a material external standard/library/provider/protocol:

- search current authoritative primary sources;
- record source URL, publication/version date and retrieval date;
- determine normative vs informative status;
- evaluate current support/EOL;
- record decision in ADR or source register;
- re-check immediately before implementation if information is freshness-sensitive.

Community blogs may support understanding but cannot override normative sources.

## 10. Real example — ambiguous cancellation

**Situation:** Product sends `cancel` but the architecture does not define whether access remains through period end.

Correct action:

- Tunner may define the billing cancellation effective time only from approved subscription contract.
- Product entitlement effect remains Product-owned.
- If the billing effective-time rule itself is missing, create `DEC-*`.
- Do not implement “cancel at period end” simply because a payment provider commonly supports it.

## 11. Acceptance tests

| ID | Given | When | Then |
|---|---|---|---|
| GOV-DOC-001 | ACTIVE spec v1.0 | material behavior is changed | merge is blocked without updated spec or approved amendment |
| GOV-DOC-002 | ambiguous requirement | AI attempts implementation | work item enters `NEEDS_PRODUCT_DECISION`; no guessed behavior is committed |
| GOV-DOC-003 | Product Decision contradicts old doc | decision approved | doc amendment is generated and old behavior becomes historical |
| GOV-DOC-004 | current external standard required | implementation starts | source register contains authoritative research dated before implementation |

## 12. Current baseline closure gate

Package 1.6.0 is the **ACTIVE — LOCKED DEVELOPMENT BASELINE**. The full-project documentation/FigJam/UI/contract/traceability closure gate in document 16 passed on 2026-09-29. P0 Governance & Development System implementation is authorized. Feature/platform planes P1–P8 remain gated by the P0 feature-development enablement gate in document 11; no plane may bypass governance readiness.

Future packages must not use the word “ready” when material technology or Product Decisions remain unclassified.

## 13. Active pre-development amendments

### AMD-0001 — Pre-P0 Governance, Agent Skills, Context Economy & Bootstrap Completion

Status: **APPROVED / ACTIVE — 2026-09-29**

Baseline 1.6.0 remains the immutable locked documentation archive. AMD-0001 supplements it before repository/P0 implementation with:

- the complete 20-role agent skill catalog;
- governance-controlled orchestration;
- adaptive context/token-cost governance;
- repository-access vs prompt-scope separation;
- deterministic-first context retrieval and freshness controls;
- P0 repository/GitHub bootstrap semantics;
- the strict P0-to-feature-development gate;
- relocation of non-governance feature/platform implementation out of P0.

Effective implementation authority before the next consolidated baseline is:

`Locked Baseline 1.6.0 + approved PDR/ADR records + AMD-0001`

The absence of a GitHub repository or GitHub Actions URL before P0 bootstrap is an expected bootstrap state, not an unresolved Product or architecture decision.


## 14. Authority-first decision-resolution gate — AMD-0005

Before creating any Product Decision, ADR/technical-decision request, amendment request, `NEEDS_PRODUCT_DECISION`, `CONTRACT_REQUIRED`, `TECH_DECISION_REQUIRED` or equivalent missing-authority blocker, the contributor must complete a recorded **Authority Resolution Pass**.

The pass must verify the current authority mirror/index and review all applicable active Development Documentation/FRDs, accepted ADR/PDR/amendment records, canonical contracts, applicable `TUN-FLOW-*`/FigJam architecture, governed work/decision history and—only where material—current primary-source R&D.

A decision/blocker is valid only when the recorded pass identifies the exact unresolved question and proves that existing applicable Tunner authority does not already resolve it.

Rules:

1. Existing applicable authority is reused; do not create a duplicate decision merely to restate it.
2. Implementation that conflicts with existing authority is a conformance/defect issue unless an intentional authority change is proposed.
3. External research or “best practice” may justify an ADR/amendment proposal but never silently overrides active Tunner authority.
4. Materially overlapping active decisions are rejected unless the new record explicitly corrects/supersedes an existing record, resolves a documented conflict, or responds to an explicit revisit/activation trigger.
5. Corrected/superseded governance history is preserved; authoritative history is not deleted for cleanup.
6. A selective/trigger-based capability with no current activation trigger is not a missing-contract decision. Do not invent scope to justify the technology.

Minimum decision authority evidence:

```yaml
authority_resolution:
  verified: true
  authority_context_ref: ""
  reviewed_refs: []
  discovery_terms: []
  existing_authority_found: []
  conflicts_found: []
  unresolved_gap: ""
  blocked_scope: []
  evidence: []
```

`verified: false`, an empty applicable authority review, or a gap already answered by active authority prevents the proposed decision/blocker from advancing.

Acceptance:

| ID | Given | When | Then |
|---|---|---|---|
| GOV-AUTH-001 | active authority already resolves a question | agent proposes a new decision | governance rejects the duplicate and links the existing authority |
| GOV-AUTH-002 | material question appears unresolved | decision/blocker creation attempted | recorded Authority Resolution Pass is mandatory before creation |
| GOV-AUTH-003 | selective technology has no current activation trigger | work is evaluated | absence of trigger is not classified as `CONTRACT_REQUIRED`/`TECH_DECISION_REQUIRED` |
| GOV-AUTH-004 | external guidance differs from Tunner authority | implementation is proposed | current Tunner authority remains controlling until governed ADR/PDR/amendment changes it |
| GOV-AUTH-005 | prior decision is later found unnecessary/conflicting | correction occurs | historical record is superseded/corrected rather than silently deleted |

AMD-0005 canonical authority: https://drive.google.com/file/d/1M_B4TYwf5gDKaZ-1w_gMR9Nse-Fs7B87/view
