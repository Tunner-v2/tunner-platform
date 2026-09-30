# HANDOFF

## Work item

- ID: TUN-P0-005
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local P0 integration under DEC-0001)
- Branch: change/TUN-P0-control-plane
- Local commit: 7f0cc93 (`governance: refresh bounded context evidence`)
- Integration: AMD-0002 permits continued governance-eligible local and branch work while protected-main approval is pending.
- Prerequisite: TUN-P0-003 is locally validated under DEC-0001 P0 compatibility; P0-005 cannot enter DONE before the final protected P0 PR merges.

## Completed

- The repository-native `tunner context build --work-item <id>` command produces a bounded, manifest-first context pack.
- The pack includes the active-authority summary, selected authority references including AMD-0002, decisions, prerequisite state, project state, required evidence, activated roles, exclusions, hashes, and bounded Git history.
- `tunner context verify` reports CURRENT for unchanged sources and STALE for a missing or modified source; generated context does not become authority.
- Fixture coverage now asserts that a pack includes `docs/authority/current-authority.json`.
- Corrected malformed tabbed CLI wording in the P0-005 acceptance record.

## Tests/evidence

- Static P0-005 source validator: PASS.
- P0-004 static source validator and 16 governance schema contracts: PASS.
- .NET 10.0.401 build: PASS, 0 warnings, 0 errors.
- Functional fixture harness: PASS, including fresh/context-stale and active-authority-summary scenarios.
- Live governance validation: PASS for 16 records with no diagnostics.
- Live P0-005 context build/verify: PASS / CURRENT.
- `governance gate check TUN-P0-005`: READY.
- `governance next`: no human protected-main integration actions.
- NuGet vulnerability metadata: no vulnerable packages reported.
- Evidence: `governance/evidence/TUN-P0-005-VALIDATION.json`, `governance/evidence/TUN-P0-005-ROLE-REVIEWS.json`, and `governance/context/CTX-TUN-P0-005-001.yaml`.

## Decisions/blockers

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- DEC-0001 allows local P0 integration only. No P0 work item is DONE until the final protected P0 PR merges.
- AMD-0002 does not bypass GitHub Rulesets, CODEOWNERS, human protected-main approval, Product decisions, or release authority.
- TUN-P0-033 owns authority hash verification and build refusal for a corrupt mirror; P0-005 records the active verified-authority summary but does not claim that later enforcement.
- P1–P8 Product behavior remains out of scope until P0 closes.

## Context state

- Generated pack: `docs/context/current/manifest.json`.
- P0-005 selection: `governance/context/CTX-TUN-P0-005-001.yaml`.
- Pack scope: effective authority, explicit governance/context authority, AMD-0002 workflow authority, decision/prerequisite state, required evidence, active roles, project state, bounded Git history, and explicit exclusions.

## Exact next action

- Use `governance next`; it presently reports only existing validation records, so create/select the next repository work-item record from the canonical P0 backlog before implementing another scope. Do not push or merge before the final P0 aggregate PR is ready for protected-main review.