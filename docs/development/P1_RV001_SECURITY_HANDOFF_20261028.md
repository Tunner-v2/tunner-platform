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
| Real gRPC vendor control |41 checks pass, adding real .NET OTLP/gRPC trace/log/metric exports and exact backend queries; compiled with zero warnings/errors and rejects non-loopback inputs. This remains vendor control only. | governance/evidence/security-20261028/vendor-grpc-compatibility-control.json |
| P2-005 | Native engineering gate READY and explicit scoped UI gate PASS. Owned behavior paths remain unchanged since f132e0f. Product-approved Figma backfill stays nonblocking. | governance/evidence/TUN-P2-005-VALIDATION.json |

## Current build and exact next action

Resume verification on2026-11-08: authority196 VERIFIED, governed context CURRENT, source freshness CURRENT(11days), current P1-RV001-001 remains eligible IN_PROGRESS. Native final gate correctly remains NOT_READY because final image validation and final role-review receipts are absent. PR21 is still draft, base main, automatic merge disabled. Effective main rules still require the same three strict GitHub Actions checks; no repository settings were changed. Preserve historical evidence instead of treating it as a fresh image scan.

The previous independent Grafana cache-only stage completed successfully. The Collector compatibility proof also completed on2026-10-29 at source00a556bada529437a2365eb575e89985961db2df:16 authentication-preservation test cases pass with zero failed/skipped cases. See governance/evidence/P1-RV001-001-COLLECTOR-COMPAT-VALIDATION-20261108.json. This removes only obsolete unsupported crypto.Hash(0) initialization from exact authenticated vendored Sermo source; existing ASAP algorithms and rejection behavior remain unchanged. It is not full executable/runtime acceptance. All helper regressions69 and real-gRPC vendor control41 were recorded separately; their scope boundaries remain unchanged.

C: now has22.4GiB free, meeting the unchanged18GiB preflight. Docker was stopped at resume; docker desktop start completed and engine29.7.2 responds. The complete private build is now running from committed29a70b1 source via tools/security/Build-TunnerLgtmSecurityImage.ps1. It reuses verified component caches and refreshes the production UI audit. Actual log: artifacts/security-images/lgtm-build.log. The first resume attempt failed only because the Docker engine was stopped; do not treat that attempt as successful build evidence.

Exact next action: wait for this single complete build, inspect actual Collector CLI version/components output and final provenance, then run tools/security/Scan-TunnerLgtmImage.ps1 and tools/validation/Test-TunnerLgtmImage.ps1 against tunner-security/lgtm:0.38.1-r1. Require23 executable targets, OS/Node and exact Java SBOM coverage; enforce the approved two-alert plugin VEX source/hash/test/review/expiry guards and retain raw findings. Require all41 compatibility checks, including actual gRPC traces/logs/metrics. Do not launch duplicate builds or change runtime defaults before the complete candidate passes.

Gated adoption remains prepared only: artifacts/p2-005-acceptance-20261028/lgtm-adoption-DRAFT-NOT-APPLIED.patch. Re-review against current sources and add source guards before applying after actual full-image acceptance. Final SBOM/licenses/provenance, specialist reviews, runtime/catalog/enforced-matrix adoption and clean CI remain required. Current default is still grafana/otel-lgtm:0.34.0 and the aggregate gate correctly fails. Do not create FINAL-VALIDATION or mark DONE early. P1-RV001-002 alone explicitly requires MERGED_TO_MAIN; unrelated eligible local/branch P2 work may continue.

## Consent, boundaries and recovery

The direct dev/test signing and exact Tempo-plugin VEX approvals are recorded in their P1-RV001-001 approval receipts. VEX expires2026-11-27T00:00:00Z; no core-service VEX, other CVE or new binary/source hash is covered. Kernel BPF host instrumentation is optional, not enabled, and not claimed by the unprivileged fixture; complete assets are retained.

Preserve13 pre-existing dirty paths recorded in artifacts/p2-005-auth-20261021/preserved-dirty-before.json, including docs/context/current, three docs/project files and .vscode/settings.json. Do not stage, overwrite or normalize them. Use a separate context output directory. Refresh native authority/governance/context/source checks at the final handoff. All new task-owned tracked changes must be committed and source-bound before acceptance. The Windows functional-test dialog follow-up is reverified: both current ordinary harness suites and six controlled managed-failure paths pass, reusing the unchanged623d3db fix. See governance/evidence/FUNCTIONAL-HARNESS-REVERIFICATION-20261028.json; no original stack attribution or WER suppression is claimed.
