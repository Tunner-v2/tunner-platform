> **Tunner Development Authority Package**  
> **Status:** CURRENT AUTHORITY INDEX  
> **Effective date:** 2026-09-30

# 37 — Current Authority Index

## 1. Effective authority

Current implementation authority:

`Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002 + AMD-0003 + AMD-0004 + AMD-0005`

Immutable baseline archive:

- File: `Tunner_Development_Documentation_Baseline_1.6.0_LOCKED.zip`
- Drive file ID: `15LKeYkePa5_0RgPlikZNKsp7XAEsI8EW`
- Canonical link: https://drive.google.com/file/d/15LKeYkePa5_0RgPlikZNKsp7XAEsI8EW/view

Active amendments:

- `AMD-0001_Pre-P0_Governance_Agent_Skills_Context_and_Bootstrap_Completion.md`
  - Drive: https://drive.google.com/file/d/15MYKfh7zplOWoz80FvgPJOPi-Y6GfI7H/view
  - Status: `APPROVED / ACTIVE`
  - Applicability: `GLOBAL`
- `AMD-0002_Non-Blocking_PR_Approval_and_Continuous_Local_Execution.md`
  - Drive: https://drive.google.com/file/d/1dmODTNgtRS7WbKZyuX__FVmuJKQplkxN/view
  - Status: `APPROVED / ACTIVE`
  - Applicability: `GLOBAL`
  - Effective date: `2026-09-30`
- `AMD-0003_Location-Independent_Repository_and_Path_Resolution.md`
  - Drive: https://drive.google.com/file/d/1N8GLvNiRkkm5hEgIIvJzJo_7DNpBJYJD/view
  - Status: `APPROVED / ACTIVE`
  - Applicability: `GLOBAL`
  - Effective date: `2026-09-30`
- `AMD-0004_Shared_Technical_Primitives_Formatting_and_Canonicalization.md`
  - Drive: https://drive.google.com/file/d/1ZBHXj1ndc5x341fRxEsva06Z2vpUPWzn/view
  - Status: `APPROVED / ACTIVE`
  - Applicability: `GLOBAL`
  - Effective date: `2026-10-02`
- `AMD-0005_Authority-First_Decision_Resolution_and_Duplicate-Decision_Prevention.md`
  - Drive: https://drive.google.com/file/d/1M_B4TYwf5gDKaZ-1w_gMR9Nse-Fs7B87/view
  - Status: `APPROVED / ACTIVE`
  - Applicability: `GLOBAL`
  - Effective date: `2026-10-03`

## 2. Authorization state

| Scope | State |
|---|---|
| Documentation/business closure | `PASS` |
| P0 Governance & Development System | `AUTHORIZED` |
| P1–P8 feature/platform development | `BLOCKED_PENDING_P0_ENABLEMENT_GATE` |

P1–P8 are automatically eligible for governance scheduling only after document 36's machine-verifiable P0 gate passes.

## 3. New amendment authorities

- `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md` — https://drive.google.com/file/d/1Pp1rlvF20HIA-e_1AOd72esSxZlNISQj/view
- `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md` — https://drive.google.com/file/d/1iIe-aRFQzJdaoLItpLx8tCIXZSCiVYzg/view

These are mandatory under AMD-0001.

## 3A. Complete pre-P0 materialization package

Canonical folder:

https://drive.google.com/drive/folders/15pVwFcj6TfbRQWAcR_54V7-DrrMJ7k28

Status: `COMPLETE_READY_FOR_P0_MATERIALIZATION`.

Contents include:
- concise `AGENTS.md`;
- 20 role-specific `SKILL.md` templates;
- milestone/sprint/work-item/TODO/defect/reopen/decision/amendment/gate/review/evidence/release/context/authority/bootstrap templates;
- PROJECT_CONTEXT, CURRENT_STATE, DECISION_INDEX, RISK_REGISTER and HANDOFF templates;
- repository blueprint;
- bootstrap/self-hosting runbook;
- P0 Definition of Ready and Definition of Done;
- fresh-agent recovery test;
- context cost/quality policy;
- GitHub bootstrap policy;
- package manifest with SHA-256 source hashes.

These artifacts are pre-P0 implementation inputs. Their existence does not mean the repository or governance executables already exist. `TUN-P0-001` materializes them into the repository and brings them under normal governance.

## 4. Bootstrap-generated resources

| Resource | Current state | Creation/update rule |
|---|---|---|
| GitHub source repository | `CREATED / CANONICAL_URL_PENDING_REGISTRATION` | Product Owner reports repository created during P0; record canonical URL in authority/project resources when supplied. |
| GitHub Actions / workflow links | `CREATED / CANONICAL_URLS_PENDING_REGISTRATION` | Product Owner reports Actions configured; record canonical workflow links when supplied. |
| Repository project memory/context | `NOT_YET_CREATED` | Created during P0 from locked baseline + active amendments. |
| Agent skills in `.agent/skills/` | `NOT_YET_CREATED` | Materialized during P0 from document 35 contracts. |
| Governance/context/orchestrator tools | `NOT_YET_IMPLEMENTED` | Implemented and acceptance-tested in P0 according to documents 04, 11 and 36. |

These states are expected before P0 and do not reopen Product/business documentation closure.

## 5. Authority order

1. explicit Product Owner decisions / accepted PDRs where applicable;
2. active Development Documentation baseline + active approved amendments;
3. synchronized FigJam architecture/workflow representation;
4. Figma UX implementation references;
5. implementation/code.

No generated context, skill, repository summary, provider object or AI memory can override this order.

## 6. Startup rule

Before repository creation, development handoff uses the locked Drive authority + all active approved amendments.

After repository creation, the repository imports this authority lineage and becomes the transferable working project-memory surface. Drive remains the canonical retained documentation source until a later governance decision changes that arrangement.

## 7. Archived draft manifests

Any manifest explicitly named/marked `draft`, `archived`, or `development_authorized=false` is historical evidence only and must not be interpreted as the current authorization state.

## 8. Repository authority mirror

P0 must import this effective authority into a hash-verified repository mirror. Normal development may consume that local mirror for cost/performance/reproducibility only after `tunner authority verify` passes.

A mismatch between the repository mirror and approved source provenance is a governance blocker, not an AI merge decision.

## 9. Bootstrap governance

P0 begins under the limited bootstrap-governance protocol in document 36 because the governance executable does not exist at the first commit. Bootstrap mode permits only control-plane work and ends at the mandatory self-hosting cutover. The implemented governance tool must replay and validate bootstrap history before any P1–P8 work can become eligible.

## 10. PR approval and execution continuity

AMD-0002 makes required human PR approval an integration/merge gate, not a global development execution gate. Governance continues all eligible local/branch, validation, evidence and independent work while a PR is awaiting approval. A dependent scope pauses only when its declared dependency readiness is `MERGED_TO_MAIN` or another human-authority gate is genuinely required.

`LOCAL_VALIDATED` is committed-state readiness. Repository-changing work must record a validated commit SHA (and tested SHA/equivalent source identity) before `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` or completed handoff. Work-item-owned uncommitted changes block those states. A separate metadata-only closure/handoff commit is permitted; behavior-affecting changes after validation invalidate readiness until recommitted/revalidated. This does not require protected-main merge.


## 11. Location-independent repository rule

AMD-0003 prohibits hardcoded machine-specific absolute repository/solution/workspace paths. Repository-internal persisted paths are relative to `<repo-root>`; P0 establishes `.tunner-root` plus a shared dynamic resolver. Moving the repository to another drive/directory must not require committed path changes. External Google Drive/GitHub/Figma URLs and IDs remain canonical resource identifiers and are not filesystem paths.


## 12. Shared technical primitives and flow-to-code rule

AMD-0004 makes flow-to-code mapping explicitly many-to-many. Repeated deterministic technical mechanics are implemented once as cohesive reusable capabilities; domain/business semantics, technical canonicalization and presentation formatting remain distinct responsibilities.

Presentation formatters render authoritative typed values and never perform business calculations. Persisted/security-relevant/external canonical values are authoritatively validated/canonicalized server-side. Cross-runtime implementations conform to the same semantic contract/test vectors where applicable.

P1 executable decomposition must be capability-oriented with flow references for traceability; it must not create one implementation stack per flow. AMD-0004 does not reopen P0.


## 13. Authority-first decision-resolution rule

AMD-0005 requires a machine-recorded Authority Resolution Pass before creating Product/technical decisions, amendment requests based on alleged missing authority, or missing-authority blocker states such as `NEEDS_PRODUCT_DECISION`, `CONTRACT_REQUIRED` or `TECH_DECISION_REQUIRED`.

Existing applicable authority must be reused. A decision/blocker is valid only when verified applicable authority has been reviewed and the exact unresolved gap remains.

Conditional/selective technologies do not create artificial gaps when their activation trigger is absent. Generated context/search output is discovery support only and may not substitute for verified source authority.

## 14. Global applicability of active amendments

The cross-cutting governing principles in AMD-0001 through AMD-0005 are **GLOBAL** across all current and future Tunner development planes wherever semantically applicable.

- AMD-0001: global governance/AI/context/orchestration/evidence principles; initial bootstrap deliverables remain P0-specific establishment work.
- AMD-0002: global PR integration vs local execution semantics.
- AMD-0003: global repository/path portability invariant; P0 establishes the initial resolver/evidence.
- AMD-0004: global reuse, many-to-many flow-to-code mapping, formatting and canonicalization rules.
- AMD-0005: global authority-first decision resolution and duplicate-decision prevention.

A plane, work item or incident named in an amendment may describe where the gap was discovered or where the control was first implemented; it is not an applicability boundary unless the amendment explicitly declares one. Global applicability does not automatically reopen completed historical work; remediation/reopen follows the applicable governance rules when actual nonconformance exists.


## 15. Post-closure Revision (`RV`) lifecycle

The active Development Documentation now defines append-only post-closure Revision lineage for later corrective/hardening/maintenance/improvement work against legitimately closed milestones/work items.

Canonical forms include `P0-RV001` for milestone-level revision and `P0-004-RV001` for work-item revision. Revision sequences are monotonic and are not artificially limited.

A Revision is development-work lineage, not authority lineage. It does not replace or create an `AMD-*`, ADR or PDR. Where authority must change, the corresponding governed authority record remains required and the Revision implements that approved change.

Historical `CLOSED`/`DONE` evidence is preserved. `REOPENED` remains available for the active acceptance/release lifecycle; post-closure work defaults to Revision lineage.

This closes a development-lifecycle documentation gap and does not change Tunner Product/business-domain semantics or create a new active authority amendment.
