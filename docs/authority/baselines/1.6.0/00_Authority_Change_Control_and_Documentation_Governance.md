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
