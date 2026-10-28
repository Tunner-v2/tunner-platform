# P1-RV001-001 security follow-up handoff

State: IN_PROGRESS. Baseline1.6.0, P0/P1 historical closure and current P2 authorization remain unchanged. Branch: codex/security-scan-remediation-20261028. Draft PR21: https://github.com/Tunner-v2/tunner-platform/pull/21 ; automatic merge remains disabled. No registry publication, protected-main merge, production release, Figma or Drive writes.

## Validated scopes

| Scope | Actual result | Evidence |
|---|---|---|
| PostgreSQL18.6-r1, OpenBao2.7.1-r2, Mailpit1.31.6-r2 | Full OS/Go scans pass; compatibility8/8/5. Mailpit production UI199 packages, DOMPurify3.4.17 audit passes. Real browser3 and starter safety7 pass. | governance/evidence/P1-RV001-001-RUNTIME-R2-REVALIDATION-20261028.json |
|13 plugin candidates | Corrected scan passes; exact two Tempo raw alerts retained. Typed guard/signing regressions59 pass and isolated consumer passes. | governance/evidence/P1-RV001-001-TEMPO-VEX-VALIDATION-20261028.json |
| OBI component |180 tests pass; executable scan covers176 packages. Shaded Java direct scan is inconclusive; exact official SBOM covers both ByteBuddy dependencies with zero fixable HIGH/CRITICAL. | governance/evidence/P1-RV001-001-OBI-COMPONENT-VALIDATION-20261028.json |
| Tempo service candidates |58 test results pass,3 executable targets scan clean without VEX. Original failed attempt remains retained; clean final CI is still required. | governance/evidence/P1-RV001-001-TEMPO-COMPONENT-VALIDATION-20261028.json |
| Vendor compatibility control |37 checks pass: all plugin signatures, UI assets, trace/log/metric/profile ingest/query, upgrade, restart and backup/restore rollback. This is an unchanged vendor control, not rebuilt-image acceptance. | governance/evidence/security-20261028/vendor-compatibility-control.json |
| P2-005 | Native engineering gate READY and explicit scoped UI gate PASS. Owned behavior paths remain unchanged since f132e0f. Product-approved Figma backfill stays nonblocking. | governance/evidence/TUN-P2-005-VALIDATION.json |

## Current build and exact next action

The complete private LGTM build is running from the committed recipe at7660ed5. Output: artifacts/security-followup-20261028/complete-lgtm-concurrent-build.log. It uses documented concurrent Go caches after exclusive mounts caused prolonged queue contention. Interrupted task-build logs are retained; only verified task build clients were stopped. Docker and operator services were preserved. Primary-source analysis: docs/development/LGTM_BUILD_CACHE_CONCURRENCY_20261028.md.

Wait for that single full build; do not launch another competing complete build. On success inspect artifacts/security-images/lgtm-build.json and recipe binding, then run tools/security/Scan-TunnerLgtmImage.ps1 and tools/validation/Test-TunnerLgtmImage.ps1 against tunner-security/lgtm:0.38.1-r1. The scanner requires all23 executables, OS/Node coverage, the exact Java runtime SBOM and the source/hash/test/review/expiry guard before applying the two approved plugin VEX statements. Keep raw reports and unrelated findings blocking.

The full rebuilt image, final compatibility, SBOM/licenses/provenance, all final role reviews, runtime/catalog/matrix adoption and clean CI remain unaccepted. LGTM runtime default is still grafana/otel-lgtm:0.34.0 and the aggregate GitHub container gate correctly fails. Do not create FINAL-VALIDATION or mark DONE until actual required gates pass. P1-RV001-002 explicitly requires MERGED_TO_MAIN; unrelated eligible P2 branch work is not stopped by that integration gate.

## Consent, boundaries and recovery

The direct dev/test signing and exact Tempo-plugin VEX approvals are recorded in their P1-RV001-001 approval receipts. VEX expires2026-11-27T00:00:00Z; no core-service VEX, other CVE or new binary/source hash is covered. Kernel BPF host instrumentation is optional, not enabled, and not claimed by the unprivileged fixture; complete assets are retained.

Preserve13 pre-existing dirty paths recorded in artifacts/p2-005-auth-20261021/preserved-dirty-before.json, including docs/context/current, three docs/project files and .vscode/settings.json. Do not stage, overwrite or normalize them. Use a separate context output directory. Refresh native authority/governance/context/source checks at the final handoff. All new task-owned tracked changes must be committed and source-bound before acceptance. The user also requested review/fix of the Windows functional-test application-error dialogs after the current process; do not lose that follow-up.
