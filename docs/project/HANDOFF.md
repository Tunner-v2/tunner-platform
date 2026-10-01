# HANDOFF

## Work item

- ID: TUN-P0-012
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local implementation, validation evidence, and role review recorded)
- Branch: `change/TUN-P0-007-secrets-foundation`
- Scope: repository-native deterministic local R&D/source registry checks only.
- Integration: local P0 execution continues under AMD-0002. No push or protected-main change has been made.

## Completed implementation

- `tunner sources check` produces a write-once JSON manifest for an existing governed work item.
- Output is constrained to a new direct `.json` file in `artifacts/evidence/`; existing outputs, paths outside the repository, missing/reparse-point inputs, invalid authority, unavailable Git identity, and secret-shaped manifest values are rejected.
- The manifest records a verified authority-summary hash, tool version, SHA-256 hashes for explicit repository-relative artifacts, build/test/security references, exact current Git commit, and pre-generation working-tree state.
- The output expressly records that it is not release approval, Product acceptance, protected-main authorization, deployment proof, or production evidence.
- The isolated functional harness covers successful generation, byte-identical output for identical inputs, path rejection, and secret-shaped-output rejection. `LOCAL_EVIDENCE_MANIFEST.md` documents the local command.

## Boundaries

- Do not embed or scan source contents into the manifest; retain only controlled paths and hashes.
- Do not treat a local manifest as a release, deployment, Product, approval, or external-publication record.
- Do not push without user direction. P0 remains subject to protected integration and the P0 enablement gate.

## Exact next action

1. Complete the P0-011 source, build, functional, secret, authority, governance, context, and diff-hygiene validation sequence.
2. Record role review and validation evidence, move to `VALIDATION`, then ask `governance next` for the next eligible P0 item.