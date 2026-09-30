> **Tunner Development Authority Amendment**  
> **Status:** APPROVED / ACTIVE  
> **Effective date:** 2026-09-30  
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0 + AMD-0001

# AMD-0002 — Non-Blocking PR Approval & Continuous Local Execution

## 1. Reason

During P0 execution, required GitHub pull-request approval was causing the AI developer to stop all progress until the Product Owner manually approved each PR. The Product Owner explicitly directed that PR approval should not prevent continued safe local/branch development and requested a lower-interaction workflow.

This amendment removes the unnecessary synchronization point while preserving protected-main review/integration controls.

## 2. Change type

`TECHNICAL | GOVERNANCE | PROCESS | DEVELOPER_EXPERIENCE`

No Tunner commercial/business-domain semantics are changed. No Product feature plane is authorized by this amendment.

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
11. Root `AGENTS.md` and Pre-P0 governance templates carry this rule so new AI sessions inherit it without corrective chat prompts.
12. The Product Owner has reported that the GitHub repository and GitHub Actions have now been created; canonical URLs remain to be registered when supplied.

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
- configurable concurrency/reviewability controls prevent unlimited dependent unmerged chains.

## 6. Development authorization

- Documentation/business closure: remains PASS.
- P0 Governance & Development System: remains AUTHORIZED.
- P1–P8 feature/platform implementation: remains BLOCKED until the existing P0 enablement gate passes.
- Open Product Owner decision introduced by this amendment: NONE.

## 7. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-09-30.
