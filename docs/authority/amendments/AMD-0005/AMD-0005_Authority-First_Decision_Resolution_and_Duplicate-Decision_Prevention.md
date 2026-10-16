# Tunner Development Authority Amendment

> **Status:** APPROVED / ACTIVE
> **Effective date:** 2026-10-03
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0
> **Applicability:** GLOBAL across all development planes, work types, roles, domains, UI/API/SDK/infrastructure/tooling/security/compliance/finance/release activities.
> **Scope clarification:** 2026-10-03 — P1 SignalR/gRPC cases are discovery examples only; authority-first resolution is mandatory system-wide.

# AMD-0005 — Authority-First Decision Resolution & Duplicate-Decision Prevention

## 1. Reason

A governance gap was identified during P1 execution. An agent created a new local decision/blocker for SignalR despite existing accepted ADRs and development documentation already defining SignalR's role, authority boundary, reconnect/resynchronization behavior and browser/BFF scope. A similar failure mode appeared for selective gRPC/gRPC-Web work, where the absence of an activation trigger was interpreted as a missing contract.

The existing documentation requires contributors to read relevant authority and forbids assumptions, but it does not make **proof of authority exhaustion** a mandatory, machine-verifiable prerequisite to creating a Product Decision, ADR request, amendment request, `CONTRACT_REQUIRED`, `TECH_DECISION_REQUIRED`, or equivalent missing-authority blocker.

This amendment closes that gap.

## 2. Change type

`GOVERNANCE | AUTHORITY | DEVELOPMENT-PRACTICE | TOOLING | QUALITY`

No Tunner Product/business semantics are changed.

## 2A. Global applicability clarification

AMD-0005 is a **GLOBAL governance invariant**. Its Authority Resolution Pass and duplicate-decision prevention rules apply before any relevant decision, amendment request or missing-authority blocker in P0-P8 and any future plane.

This includes business/Product, architecture, security, compliance, privacy, finance, API/SDK, UI/UX, infrastructure, operations, release and governance/tooling questions.

The SignalR and gRPC cases in this amendment are historical discovery examples. They do not define or limit scope.

## 3. Constitutional rule — authority first

Before any AI/human contributor may:

- create a Product Decision request;
- create a technical/architecture decision request;
- create an amendment request because authority is allegedly missing;
- mark work `NEEDS_PRODUCT_DECISION`;
- mark work `CONTRACT_REQUIRED`;
- mark work `TECH_DECISION_REQUIRED` or an equivalent architecture/contract blocker;
- recommend a new architectural authority that could overlap existing authority;

the contributor must complete and record an **Authority Resolution Pass**.

A decision/blocker created without a valid Authority Resolution Pass is governance-invalid.

## 4. Mandatory Authority Resolution Pass

The pass must use the smallest sufficient context but must examine all applicable authoritative layers:

1. current verified repository authority mirror / Current Authority Index;
2. relevant active Development Documentation / FRD;
3. active approved ADRs, PDRs and amendments;
4. applicable canonical API/event/data/policy contracts;
5. applicable `TUN-FLOW-*` / synchronized FigJam architecture;
6. applicable governed project/work-item state and prior decisions;
7. Figma only when the unresolved question is an approved UX implementation concern;
8. current primary-source R&D only when freshness-sensitive external facts are material.

The pass must record:

- work item / issue;
- authority version/hash or verified authority context;
- exact authority references reviewed;
- relevant search/index terms or aliases used where discovery was required;
- existing authority found;
- conflicts found;
- the exact unresolved question, if any;
- why existing authority does not resolve it;
- which scope is actually blocked;
- evidence supporting creation of the new decision/blocker.

A generic statement such as “no contract found” is insufficient.

## 5. Existing authority takes precedence

If existing applicable authority already resolves the question:

- do not create a new decision request;
- do not create a duplicate ADR/PDR/DEC record;
- do not place the work in a missing-authority blocker state;
- link the work item to the existing authority and proceed according to governance eligibility.

If implementation disagrees with existing authority, that is an implementation/defect/conformance issue unless an intentional authority change is proposed.

If external research or a specialist recommendation conflicts with active Tunner authority, the contributor must surface the conflict and use the governed ADR/PDR/amendment process. External best practice does not silently replace Tunner authority.

## 6. Duplicate-decision prevention

Before creating a decision, governance must detect material overlap with active or applicable:

- ADRs;
- PDRs;
- approved local decisions;
- active amendments;
- development-document authority.

A materially overlapping decision is rejected unless the new record explicitly:

- corrects/supersedes an existing decision;
- resolves a documented conflict;
- narrows an intentionally unresolved branch;
- or is required by an explicit revisit/activation trigger.

Superseded/corrected records remain in immutable history. They are not deleted to make the active state appear cleaner.

## 7. Blocker semantics

`NEEDS_PRODUCT_DECISION`, `CONTRACT_REQUIRED`, `TECH_DECISION_REQUIRED`, and equivalent states mean that an **already authorized requirement cannot safely proceed because existing authority was verified and is genuinely insufficient or conflicting**.

They must not be used merely because:

- an optional/selective technology has no current use case;
- a future capability has not reached its activation trigger;
- a work-item title contains a technology name;
- a developer has not yet searched the authority;
- generated context omitted a relevant authority artifact;
- external documentation presents additional implementation choices that Tunner has already constrained.

## 8. Conditional/selective technology rule

The rule in this section is global. The current P1 protocol example below is illustrative only.

When Tunner authority marks a technology/capability `SELECTIVE`, `SELECTED WHEN NEEDED`, trigger-based, or otherwise conditional:

1. first evaluate whether an approved activation trigger exists;
2. if no trigger exists, do not invent a feature/process to justify the technology;
3. do not create packages, endpoints, clients, schemas or generic scaffolding solely to satisfy backlog numbering;
4. do not classify the absent trigger as a missing contract;
5. record `NOT_APPLICABLE`, `NO_TRIGGER`, or complete the evaluation work item as `DONE` when its acceptance criteria are evaluation/eligibility based;
6. create a feature/process-specific ADR only when the documented trigger actually occurs.

For the current P1 protocol scope:
- REST/BFF remains the default browser command/query path;
- SignalR remains Tunner-owned browser realtime;
- gRPC-Web remains selective for a concrete Tunner-owned browser capability with demonstrated value;
- native gRPC remains selective for a justified separate process/service boundary;
- the absence of a gRPC trigger is not `CONTRACT_REQUIRED`.

## 9. Tooling requirements

`tunner-governance` and `tunner-context` must enforce this rule.

Minimum requirements:

- decision-create validates Authority Resolution Pass evidence;
- missing-authority blocker creation validates the same evidence;
- active-authority duplicate detection runs before a new decision identifier is accepted;
- decision/blocker records link exact authority refs;
- `governance next` does not block on an untriggered conditional capability;
- generated context must not present stale/nonexistent legacy decision filenames as applicable authority;
- semantic/search indexes are discovery aids only; the final resolution must cite verified source authority;
- if context is insufficient, return `INSUFFICIENT_CONTEXT` and expand retrieval before declaring an authority gap.

## 10. Decision record requirements

Every new decision record must contain an `authority_resolution` section or equivalent schema-versioned evidence containing at minimum:

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

A proposed decision with `verified: false`, empty applicable authority review, or an unresolved gap already answered by active authority cannot advance to approval.

## 11. Work-item requirements

Work items must distinguish:

- **required missing authority** — real blocker after Authority Resolution Pass;
- **conditional activation trigger absent** — not a missing-contract blocker;
- **implementation defect/nonconformance** — code/state differs from existing authority;
- **intentional authority change** — amendment/ADR/PDR proposal;
- **insufficient retrieved context** — expand context before making a decision request.

## 12. Affected authorities

- `00_Authority_Change_Control_and_Documentation_Governance.md`
- `03_Governed_Development_Lifecycle_Scrum_Git.md`
- `04_Governance_and_Context_Tooling_Specification.md`
- `11_Development_Planes_Milestones_and_P0_Backlog.md`
- `12_AI_Development_Protocol_Context_and_Project_Memory.md`
- `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md`
- `37_Current_Authority_Index.md`
- Development Documentation `README.md`
- Decisions `README.md`
- `AGENTS.md`
- governance `decision` / `work-item` / lifecycle templates and corresponding repository schemas/tooling

## 13. Existing records and remediation

This amendment is prospective and corrective.

Previously created records that were based on an incomplete authority-resolution pass must be audited. If an existing record is contradicted or made unnecessary by older active authority, preserve its history and correct/supersede it through governance rather than silently deleting authoritative history.

Derived/generated stale references may be removed or regenerated when they are not themselves immutable governance history.

## 14. Global applicability and historical P1 discovery context

AMD-0005 does not reopen already-closed P0 work merely because its rule is global. It governs all future and modified work across every plane.

P1 was the discovery context for the SignalR/gRPC authority-resolution failure. The same Authority Resolution Pass is mandatory later for Account/Identity, Connected Product/SDK, Notifications, Commercial/Billing, Security/Privacy/Operations, Finance/Accounting, Production Readiness and any future domain or platform work.

Previously created records based on incomplete authority resolution are remediated according to section 13 regardless of which plane created them.

## 15. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-10-03.
