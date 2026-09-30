# HANDOFF

## Work item

- ID: TUN-P0-002
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local P0 integration under DEC-0001)
- Branch: change/TUN-P0-control-plane
- Integration: AMD-0002 permits continued governance-eligible local and branch work while any protected-main pull request is approval-pending.
- Prerequisite: TUN-P0-001 (complete); P0-002 cannot enter DONE before the final protected P0 PR merges.

## Completed

- The repository-native contribution guide and pull-request template retain protected-main and required CODEOWNERS review controls.
- The contribution policy now distinguishes local validation from protected-main integration: approval-pending status continues governance-eligible local/branch work, while merge into `main` retains human approval.
- Pull-request metadata now records whether a dependent scope requires `LOCAL_VALIDATED`, `MERGED_TO_MAIN`, or no dependency-readiness gate.
- A focused static validator protects those policy statements from unreviewed regression.

## Tests/evidence

- `tools/validation/Validate-GitContributionPolicy.ps1`: PASS.
- `governance validate`: PASS for 15 records with no diagnostics.
- TUN-P0-002 generated context pack: PASS / CURRENT with verified hashes.
- Static policy evidence: `governance/evidence/TUN-P0-002-STATIC-VALIDATION.json`.
- `git diff --check`: PASS before the local commit.

## Decisions/blockers

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- DEC-0001 allows local P0 integration only. No P0 work item is DONE until the final protected P0 PR merges.
- AMD-0002 does not bypass protected-main approval, GitHub Rulesets, or CODEOWNERS. It only prevents approval-pending PR state from becoming a global development blocker.
- P1–P8 Product behavior remains out of scope until P0 closes.

## Context state

- Generated pack: `docs/context/current/manifest.json`.
- P0-002 selection: `governance/context/CTX-TUN-P0-002-001.yaml`.
- Pack scope: contribution policy authority, prerequisite state, required evidence, active roles, project state, bounded Git history, and explicit Product/R2 exclusions.

## Exact next action

- Commit the locally validated TUN-P0-002 governance-policy slice, then run `governance next` and select the next governance-eligible P0 work item. Do not push or merge before the final P0 aggregate PR is ready for protected-main review.