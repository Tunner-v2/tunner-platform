# CURRENT_STATE

## Baseline and milestone

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- Authority mirror: tunner authority verify is currently passing for all 72 imported authority artifacts.
- Milestone: P0 is ACTIVE. P1–P8 remain blocked by the separate P0 enablement gate.
- Integration: local/branch development continues under AMD-0002; protected main integration still requires the approved human controls.

## Locally validated P0 foundations

- TUN-P0-001 through TUN-P0-015 are locally validated where recorded; no item is represented as DONE before protected integration and P0 enablement.
- TUN-P0-008’s local migration rehearsal is operator-confirmed against Tunner’s dedicated loopback PostgreSQL port 25432; the connection value was not retained in repository evidence.
- TUN-P0-009 provides only a local, redacted OpenTelemetry baseline. It does not authorize Product telemetry or production observability.
- TUN-P0-013 adds a local SAST-style policy, NuGet SCA query, repository secret scan, Compose-definition policy, SPDX 2.3 inventory, and checksum/provenance hook. Its GitHub Actions image scan is configured but awaits a remote run; no release attestation, deployment, or Product behavior is claimed.
- TUN-P0-014 completes developer automation with guarded local reset and deterministic unit-test dispatch. No real Docker lifecycle or reset was run by validation.
- TUN-P0-015 adds a read-only documentation gate for local links, canonical governance IDs, duplicate IDs by record type, authority headers, schema validation, and recorded source freshness. It does not fetch external URLs.

## Active work item

| ID | State | Scope | Next action |
|---|---|---|---|
| TUN-P0-011 | VALIDATION | Deterministic local evidence manifests only | Retain local evidence; integration remains protected. |
| TUN-P0-012 | VALIDATION | Local R&D/source registry | Retain local source-evidence and integration controls. |
| TUN-P0-013 | VALIDATION | Security and supply-chain pipeline baseline | Retain local/CI evidence hooks; actual CI scan requires a remote workflow run after integration. |
| TUN-P0-014 | VALIDATION | Developer automation | Retain local command evidence; real Docker lifecycle remains operator-run. |
| TUN-P0-015 | VALIDATION | Documentation validation | Retain local validation evidence; no external links are fetched. |

## Exact next authorized action

- Use tunner governance next to identify the next governance-eligible P0 item. Do not push or change protected main without explicit user direction.
- TUN-P0-026 locally validates the mandatory agent skill registry and activated-skill version/hash context metadata.
