# CURRENT_STATE

## Baseline and milestone

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- Authority mirror: `tunner authority verify` is currently passing for all 72 imported authority artifacts.
- Milestone: P0 is ACTIVE. P1–P8 remain blocked by the separate P0 enablement gate.
- Integration: local/branch development continues under AMD-0002; protected `main` integration still requires the approved human controls.

## Locally validated P0 foundations

- TUN-P0-001 through TUN-P0-010 are locally validated where recorded; no item is represented as `DONE` before protected integration and P0 enablement.
- TUN-P0-010 completed its independent audit: clean committed diff, zero-warning build, four unit tests passing, repository secret scan with no finding, and current governance/authority/context evidence.
- TUN-P0-008’s local migration rehearsal is operator-confirmed against Tunner’s dedicated loopback PostgreSQL port `25432`; the connection value was not retained in repository evidence.
- TUN-P0-009 provides only a local, redacted OpenTelemetry baseline. It does not authorize Product telemetry or production observability.

## Active work item

| ID | State | Scope | Next action |
|---|---|---|---|
| TUN-P0-011 | VALIDATION | Deterministic local evidence manifests only | Retain local evidence; integration remains protected. |

## Current P0-011 boundary

- The new `evidence generate` command accepts only an existing governed work-item scope and repository-relative evidence paths.
- It writes only a new direct `.json` child below ignored `artifacts/evidence/`, records SHA-256 hashes rather than source contents, verifies authority, and records exact current Git commit plus pre-generation tree state.
- It requires build, test, and security-scan references; rejects path escapes, missing/reparse-point artifacts, existing outputs, unavailable Git identity, invalid authority, and secret-shaped output.
- It does not read credentials, access Drive/GitHub/Docker/databases/Product data, publish externally, approve a release, authorize a merge, or establish Product acceptance.

## Exact next authorized action

- Run the final gate/context audit for P0-011, retain its local evidence, commit locally without pushing, then use `governance next` to identify the next eligible P0 item. Do not push or change protected `main` without explicit user direction.