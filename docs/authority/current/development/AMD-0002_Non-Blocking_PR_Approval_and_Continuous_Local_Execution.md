> **Tunner Development Authority Amendment**  
> **Status:** APPROVED / ACTIVE  
> **Effective date:** 2026-09-30  
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0 + AMD-0001
> **Applicability:** GLOBAL across all development planes, milestones, work items, branches, PRs and governed integration/release workflows.
> **Scope clarification:** 2026-10-03 — the P0 incident that exposed the bottleneck is discovery context only; the execution/integration rule is system-wide.
> **Commit-readiness clarification:** 2026-10-03 — non-blocking PR approval never makes Git commit optional. Repository-changing work must reach a validated committed state before local readiness/completion.

# AMD-0002 — Non-Blocking PR Approval & Continuous Local Execution

## 1. Reason

During P0 execution, required GitHub pull-request approval was causing the AI developer to stop all progress until the Product Owner manually approved each PR. The Product Owner explicitly directed that PR approval should not prevent continued safe local/branch development and requested a lower-interaction workflow.

This amendment removes the unnecessary synchronization point while preserving protected-main review/integration controls.

## 2. Change type

`TECHNICAL | GOVERNANCE | PROCESS | DEVELOPER_EXPERIENCE`

No Tunner commercial/business-domain semantics are changed. No Product feature plane is authorized by this amendment.

## 2A. Global applicability clarification

AMD-0002 applies to **all current and future Tunner development**, not only P0. Wherever governed Git/PR integration exists, human approval of protected-main integration is distinct from agent/human execution eligibility.

Later planes must continue eligible local/branch implementation, validation, evidence, documentation and independent work while a PR awaits approval, subject to declared dependency readiness and concurrency policy.

The P0 approval bottleneck described in the reason section is the discovery context, not an applicability limitation.

## 2B. Committed-state readiness clarification

AMD-0002 separates **commit/local validation** from **protected-main merge approval**. It does not permit repository-changing work to become locally complete while its owned changes exist only in an uncommitted worktree.

The invariant is:

`COMMIT REQUIRED FOR LOCAL READINESS != MERGE REQUIRED FOR INTEGRATION`

For any governed work item that changes repository-resident source, tests, contracts, configuration, documentation, governance state, scripts or evidence-producing logic:

1. all work-item-owned implementation changes must be committed to the governed branch/lineage before the item may satisfy `LOCAL_VALIDATED` or `READY_FOR_MERGE`;
2. the exact validation-candidate commit SHA must be recorded in work-item/evidence state;
3. required final build/test/review/evidence gates must correspond to that committed SHA;
4. any behavior-affecting source/contract/config/test change after the validated SHA invalidates local readiness until the new committed state is revalidated;
5. governance-only closure/handoff metadata may be committed after the validation gate without rerunning product tests when the later commit changes no behavior-affecting implementation/contract/config/test content; that closure commit must be recorded separately;
6. work-item-owned uncommitted changes block `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` and completed handoff;
7. unrelated/pre-existing dirty-worktree changes may remain only when explicitly classified and recorded; an agent must not absorb or commit unrelated user work merely to obtain a clean tree;
8. a work item that makes no repository change may close without creating an artificial commit, but the evidence must explicitly record `repository_changed: false`.

`MERGED_TO_MAIN` remains a separate readiness level. A validated commit may be consumed by downstream work when `LOCAL_VALIDATED` is sufficient even while PR approval/merge remains pending.

## 3. Approved changes

1. Human PR approval is a protected-main **integration/merge gate**, not a general development execution gate.
2. `APPROVAL_PENDING` does not by itself place the project, milestone or work item in a global `BLOCKED` state.
3. While approval is pending, AI/human contributors continue all governance-eligible local/branch work, tests, reviews, evidence, documentation/context updates and independent work.
4. Dependencies distinguish `LOCAL_VALIDATED` from `MERGED_TO_MAIN` readiness.
5. A downstream work item may proceed before merge when its declared prerequisite requirement is satisfied by `LOCAL_VALIDATED`.
6. Only scopes explicitly requiring `MERGED_TO_MAIN` remain blocked on the human merge action.
7. `tunner governance next` surfaces pending human integration actions separately from agent-executable work and continues to calculate eligible work.
8. The orchestrator must not idle merely because one or more PRs await approval.
9. Human interaction remains required for actual protected-main merge approval where policy requires it, explicit Product decisions/acceptance, production/release authorization and security/compliance/legal gates that require human authority.
10. Governance may impose configurable limits on dependent unmerged PR chains/concurrent unmerged work to preserve reviewability; reaching such a limit blocks further dependent chaining, not unrelated eligible work.
11. A repository-changing item cannot reach `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` or completed handoff while work-item-owned changes remain uncommitted.
12. Governance/evidence records must identify the validation-candidate commit SHA and, when different, the later closure/handoff commit SHA.
13. Final validation evidence must correspond to the validation-candidate committed state; later behavior-affecting changes invalidate that evidence until revalidation.
14. Pre-existing or unrelated uncommitted changes must be preserved and explicitly classified rather than silently committed with the current work item.
15. Root `AGENTS.md` and Pre-P0 governance templates carry this rule so new AI sessions inherit it without corrective chat prompts.
16. The Product Owner has reported that the GitHub repository and GitHub Actions have now been created; canonical URLs remain to be registered when supplied.

## 4. Affected authorities/artifacts

- `03_Governed_Development_Lifecycle_Scrum_Git.md`
- `04_Governance_and_Context_Tooling_Specification.md`
- `12_AI_Development_Protocol_Context_and_Project_Memory.md`
- `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md`
- `37_Current_Authority_Index.md`
- `README.md`
- `Pre-P0 Implementation Package/AGENTS.md`
- `Pre-P0 Implementation Package/Governance Templates/lifecycle-policy.template.yaml`
- `Pre-P0 Implementation Package/Governance Templates/work-item.template.yaml`
- `Pre-P0 Implementation Package/Governance Templates/gate.template.yaml`
- `Pre-P0 Implementation Package/Governance Templates/evidence-manifest.template.yaml`
- `Pre-P0 Implementation Package/Project Memory/HANDOFF.template.md`
- new `Pre-P0 Implementation Package/Governance Templates/dependency.template.yaml`
- new `Pre-P0 Implementation Package/Governance Templates/pr-integration-policy.template.yaml`
- `Pre-P0 Implementation Package/PRE_P0_PACKAGE_INDEX.md`
- `Pre-P0 Implementation Package/PRE_P0_PACKAGE_MANIFEST.json`

## 5. Acceptance rule

The amended governance implementation is correct only when:

- a pending PR approval remains visible;
- independent/local-eligible work still appears in `tunner governance next`;
- locally validated dependencies can be consumed when declared sufficient;
- merged-main dependencies remain blocked until merge evidence exists;
- human interaction is requested only for genuine human gates;
- configurable concurrency/reviewability controls prevent unlimited dependent unmerged chains;
- a repository-changing work item cannot satisfy `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` or completed handoff without a recorded validated commit SHA;
- required final validation evidence resolves to the recorded validation-candidate commit;
- behavior-affecting changes after validation invalidate readiness until recommitted/revalidated;
- unrelated/pre-existing dirty-worktree changes remain uncommitted and explicitly classified rather than being absorbed into the work-item commit.

## 6. Development authorization

**Historical authorization snapshot at AMD-0002 approval. This section does not limit global applicability after later planes become authorized.**


- Documentation/business closure: remains PASS.
- P0 Governance & Development System: remains AUTHORIZED.
- P1–P8 feature/platform implementation: remains BLOCKED until the existing P0 enablement gate passes.
- Open Product Owner decision introduced by this amendment: NONE.

## 7. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-09-30.
