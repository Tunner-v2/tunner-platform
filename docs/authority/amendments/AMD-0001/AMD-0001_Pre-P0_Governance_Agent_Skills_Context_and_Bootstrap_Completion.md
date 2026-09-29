> **Tunner Development Authority Amendment**
> **Status:** APPROVED / ACTIVE
> **Effective date:** 2026-09-29
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0

# AMD-0001 — Pre-P0 Governance, Agent Skills, Context Economy & Bootstrap Completion

## 1. Reason

Before repository creation and P0 implementation, the Product Owner required completion of the AI-development governance model so development can preserve project memory/context, strict workflow, milestones/TODOs, multidisciplinary skills, governance-controlled orchestration and cost-aware context retrieval.

The repository and GitHub Actions do not yet exist. They are outputs of P0 bootstrap and therefore must not be treated as missing pre-P0 authority links.

## 2. Change type

`TECHNICAL | GOVERNANCE | PROCESS | UX/QUALITY`

No Tunner commercial/business-domain semantics are changed.

## 3. Approved changes

1. Add six specialist skills: Business Analyst, SDK Engineer, Governance Engineer, R&D Engineer, Cyber Security Engineer and DevOps Engineer.
2. Retain the existing fourteen roles for a total mandatory catalog of twenty skills.
3. Define detailed skill contracts in document 35.
4. Make orchestration explicitly subordinate to governance.
5. Add adaptive context modes `CORE/TASK/EXPANDED/FULL_AUDIT`.
6. Separate repository access scope from prompt/context scope.
7. Require source-hash freshness/invalidation for generated context.
8. Add governed `INSUFFICIENT_CONTEXT` escalation.
9. Use deterministic-first retrieval; semantic/vector retrieval is optional and non-authoritative.
10. Keep P0 focused on repository/governance/context/orchestration/evidence/developer-system implementation.
11. Relocate feature/platform implementation previously listed as P0 closure work to its owning later plane.
12. Treat GitHub repository and Actions URLs as `BOOTSTRAP_GENERATED / NOT_YET_CREATED` until P0 creates them; register actual URLs immediately after creation.
13. Add implementation specification document 36.
14. Baseline 1.6.0 remains the immutable locked archive. The effective development authority is **Baseline 1.6.0 + AMD-0001** until a later consolidated documentation baseline supersedes it.
15. Add a hash-verified repository authority mirror and `tunner authority status/import/verify/diff` so local project memory/context cannot silently diverge from approved Drive authority.
16. Add a narrow bootstrap-governance protocol for building the governance tool itself, followed by mandatory history replay and self-hosting cutover before any feature plane may begin.
17. Add document 37 as the current effective-authority/authorization index.
18. Add the canonical `Pre-P0 Implementation Package` containing the ready-to-materialize `AGENTS.md`, 20 `SKILL.md` templates, governance templates, project-memory templates, repository blueprint, bootstrap/self-hosting runbook, context-cost policy, GitHub bootstrap policy, P0 Definition of Ready/Done and fresh-agent recovery test.
19. Require the pre-P0 package manifest/inventory to be verified before `TUN-P0-001` materializes the package into the repository.

## 4. Affected documents

- `00_Authority_Change_Control_and_Documentation_Governance.md`
- `03_Governed_Development_Lifecycle_Scrum_Git.md`
- `04_Governance_and_Context_Tooling_Specification.md`
- `11_Development_Planes_Milestones_and_P0_Backlog.md`
- `12_AI_Development_Protocol_Context_and_Project_Memory.md`
- `13_RD_Source_Register.md`
- `16_Full_Project_Documentation_Closure_and_Gap_Register.md`
- `README.md`
- new `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md`
- new `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md`
- new `37_Current_Authority_Index.md`
- new `Pre-P0 Implementation Package/` (implementation templates and bootstrap/acceptance pack)

## 5. Development authorization

- Documentation/business closure: remains PASS.
- P0 Governance & Development System: AUTHORIZED.
- P1–P8 feature/platform implementation: BLOCKED until document 36 P0 enablement gate passes.
- Repository/GitHub URL absence before P0 creation: NOT A BLOCKER.
- Open Product Owner decision introduced by this amendment: NONE.

## 6. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-09-29.
