> **Tunner Development Authority Package**  
> **Status:** CURRENT AUTHORITY INDEX  
> **Effective date:** 2026-09-30

# 37 — Current Authority Index

## 1. Effective authority

Current implementation authority:

`Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002`

Immutable baseline archive:

- File: `Tunner_Development_Documentation_Baseline_1.6.0_LOCKED.zip`
- Drive file ID: `15LKeYkePa5_0RgPlikZNKsp7XAEsI8EW`
- Canonical link: https://drive.google.com/file/d/15LKeYkePa5_0RgPlikZNKsp7XAEsI8EW/view

Active amendments:

- `AMD-0001_Pre-P0_Governance_Agent_Skills_Context_and_Bootstrap_Completion.md`
  - Drive: https://drive.google.com/file/d/15MYKfh7zplOWoz80FvgPJOPi-Y6GfI7H/view
  - Status: `APPROVED / ACTIVE`
- `AMD-0002_Non-Blocking_PR_Approval_and_Continuous_Local_Execution.md`
  - Drive: https://drive.google.com/file/d/1dmODTNgtRS7WbKZyuX__FVmuJKQplkxN/view
  - Status: `APPROVED / ACTIVE`
  - Effective date: `2026-09-30`

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

Before repository creation, development handoff uses the locked Drive authority + AMD-0001.

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
