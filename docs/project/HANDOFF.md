# HANDOFF

## Work item

- ID: TUN-P0-003
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local P0 integration under DEC-0001)
- Branch: change/TUN-P0-control-plane
- Integration: AMD-0002 permits continued governance-eligible local and branch work while any protected-main pull request is approval-pending.
- Prerequisite: TUN-P0-001 (complete); P0-003 cannot enter DONE before the final protected P0 PR merges.

## Completed

- P0-003 establishes versioned JSON Schema Draft 2020-12 entry documents for every AMD-0001-required governance record type.
- The structural validator enforces the shared-definition, schema-version, identifier, and unexpected-entry contract without third-party installation.
- Its deliberate boundary is retained: YAML instance parsing, precise locations, lifecycle enforcement, and Git-history immutability are TUN-P0-004 scope.

## Tests/evidence

- `tools/validation/Validate-GovernanceSchemas.ps1`: PASS for 16 expected entry schemas.
- JSON parse check: PASS for all 17 governance-schema JSON documents.
- Negative unexpected-entry check: PASS; a temporary unexpected schema was rejected, then removed.
- TUN-P0-003 role reviews: PASS for Governance Engineer, Tester/QA Engineer, and Auditor.
- TUN-P0-003 generated context pack: PASS / CURRENT with verified hashes.
- Static schema evidence: `governance/evidence/TUN-P0-003-SCHEMA-VALIDATION.json`.

## Decisions/blockers

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- DEC-0001 allows local P0 integration only. No P0 work item is DONE until the final protected P0 PR merges.
- AMD-0002 does not bypass protected-main approval, GitHub Rulesets, or CODEOWNERS. It prevents approval-pending PR state from becoming a global development blocker.
- P1–P8 Product behavior remains out of scope until P0 closes.

## Context state

- Generated pack: `docs/context/current/manifest.json`.
- P0-003 selection: `governance/context/CTX-TUN-P0-003-001.yaml`.
- Pack scope: schema-tooling authority, prerequisite state, required evidence, active roles, project state, bounded Git history, and explicit Product/R2 exclusions.

## Exact next action

- Commit the current TUN-P0-003 validation refresh, then run `governance next` and select the next governance-eligible P0 work item. Do not push or merge before the final P0 aggregate PR is ready for protected-main review.