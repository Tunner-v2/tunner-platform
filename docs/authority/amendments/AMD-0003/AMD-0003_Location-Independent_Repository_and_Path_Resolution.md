# Tunner Development Authority Amendment

> **Status:** APPROVED / ACTIVE
> **Effective date:** 2026-09-30
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0

# AMD-0003 — Location-Independent Repository & Path Resolution

## 1. Reason

The Product Owner requires the Tunner repository, solution and P0 development tooling to remain valid when the checkout is moved to another drive, directory, worktree, CI workspace or container-host location.

A developer-machine drive/path must never become project authority or a hidden dependency.

Audit of the live core Development Documentation and selected Pre-P0 path-bearing templates found no literal developer-specific drive-letter, home-directory, workspace, mount or user-profile checkout path. However, the prior authority did not make path portability a sufficiently explicit enforceable invariant, and repository trees used `/` as a conceptual root marker. This amendment closes both risks.

## 2. Change type

`TECHNICAL | GOVERNANCE | PORTABILITY | DEVELOPER_EXPERIENCE`

No Tunner business/commercial/domain semantics change.

## 3. Approved rules

1. The Tunner repository/solution checkout location is runtime context, never project authority.
2. No committed source, script, build config, solution/project reference, Docker/CI config, governance record, evidence artifact, generated project state or documentation instruction may require a developer/runner-specific absolute repository path.
3. Repository-internal persisted paths use `<repo-root>`-relative logical form and canonical `/` separators.
4. Add committed `.tunner-root` as a repository-root marker.
5. All P0 tools use one shared root/path resolver:
   - explicit validated `--repo-root`;
   - validated `TUNNER_REPO_ROOT`;
   - Git top-level;
   - upward `.tunner-root` discovery;
   - otherwise fail; never guess.
6. Current working directory is not proof of repository root.
7. Runtime absolute filesystem paths may exist transiently after resolution, but in-repository references are persisted back as repository-relative paths.
8. Explicit external filesystem locations are configuration/runtime inputs and cannot silently redefine repository structure.
9. Docker/Compose, scripts and CI derive checkout/workspace paths dynamically.
10. Google Drive URLs/IDs, GitHub URLs, Figma URLs, provider URIs and network endpoints are external identifiers, not local filesystem paths; they remain valid authority/resource references.
11. Validation must detect unapproved machine-specific absolute repository paths.
12. P0 must prove portability by relocating/copying/cloning the checkout to a different parent location/drive and rerunning the governed toolchain without committed path edits.
13. Conceptual repository diagrams use `<repo-root>/`, not `/`, to avoid implying a Linux filesystem-root dependency.
14. Add `TUN-P0-035 — Location-independent repository/path resolution` and require it for P0 closure.

## 4. Affected authorities

- `02_Repository_Solution_and_Module_Architecture.md`
- `04_Governance_and_Context_Tooling_Specification.md`
- `06_Environment_Configuration_Secrets_and_Deployment.md`
- `11_Development_Planes_Milestones_and_P0_Backlog.md`
- `12_AI_Development_Protocol_Context_and_Project_Memory.md`
- `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md`
- `37_Current_Authority_Index.md`
- Development Documentation `README.md`
- Decisions `README.md`
- Pre-P0 `AGENTS.md`
- Pre-P0 repository blueprint/bootstrap/DoR/DoD/checklist
- Pre-P0 governance/context/authority templates
- Pre-P0 package index/manifest

## 5. Development authorization

- Documentation/business closure remains PASS.
- P0 remains AUTHORIZED.
- P1–P8 remain blocked by the existing P0 enablement gate.
- This amendment adds a P0 portability requirement; it does not reopen Product/business design.
- Product Owner decision introduced: NONE beyond the explicit approval of this amendment.

## 6. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-09-30.
