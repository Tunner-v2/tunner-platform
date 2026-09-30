# HANDOFF

## Work item

- ID: TUN-P0-004
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local P0 integration under DEC-0001)
- Branch: change/TUN-P0-control-plane
- Integration: AMD-0002 permits continued governance-eligible local and branch work while protected-main approval is pending.
- Prerequisite: TUN-P0-003 is locally validated through the explicit `LOCAL_VALIDATED` dependency record; P0-004 cannot enter DONE before the final protected P0 PR merges.

## Completed

- The read-only governance MVP now loads the canonical AMD-0002 PR-integration policy and dependency records.
- `governance next` keeps local/independent work actionable and returns `HumanIntegrationActions` separately for declared `MERGED_TO_MAIN` dependencies.
- Locally validated dependencies are consumable only when explicitly declared or under the existing DEC-0001 P0 compatibility rule.
- Merged-main dependencies require an explicit dependency declaration, `DONE` prerequisite state, and existing merge-evidence references.
- Configured unmerged-chain/concurrency safeguards are enforced when positive values are set. The live canonical policy intentionally retains `null` limits; no project-specific threshold was invented.

## Tests/evidence

- Schema and static source validators: PASS.
- .NET 10.0.401 build: PASS, 0 warnings, 0 errors.
- Functional fixture harness: PASS, including LOCAL_VALIDATED continuation, MERGED_TO_MAIN human-action blocking, and configured chain-limit blocking.
- Live governance validation: PASS for 16 records with no diagnostics.
- `governance next`: PASS; current graph contains no merged-main requirement, so `HumanIntegrationActions` is empty.
- `governance gate check TUN-P0-004`: READY.
- NuGet vulnerability metadata: no vulnerable packages reported.
- Evidence: `governance/evidence/TUN-P0-004-VALIDATION.json`, `governance/evidence/TUN-P0-004-ROLE-REVIEWS.json`, and `governance/evidence/TUN-P0-004-RD-PACKAGES.json`.

## Decisions/blockers

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- DEC-0001 allows local P0 integration only. No P0 work item is DONE until the final protected P0 PR merges.
- AMD-0002 does not bypass GitHub Rulesets, CODEOWNERS, human protected-main approval, Product decisions, or release authority.
- P1–P8 Product behavior remains out of scope until P0 closes.

## Context state

- Generated pack: `docs/context/current/manifest.json`.
- P0-004 selection: `governance/context/CTX-TUN-P0-004-001.yaml`.
- Pack scope: governance authority, AMD-0002 policy/dependency data, prerequisite state, required evidence, active roles, project state, bounded Git history, and explicit Product/R2 exclusions.

## Exact next action

- Commit the locally validated TUN-P0-004 AMD-0002 control-plane slice, then run `governance next` and select the next governance-eligible P0 work item. Do not push or merge before the final P0 aggregate PR is ready for protected-main review.