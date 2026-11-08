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

Reverified2026-11-08: authority196 VERIFIED; governance816 valid; P1-RV001-001 eligible IN_PROGRESS. Final gate remains NOT_READY: final full-image validation and final specialist-review receipts are absent. PR21 remains draft, base main, auto-merge disabled. Effective three strict main checks and human/code-owner review requirements remain unchanged. Drive Current Authority Index, AMD-0008 and AMD-0009 match governed mirrors with the existing BOM/CRLF normalization contract; no external files changed.

Fresh five-dependency validation is source-bound to1e30d6211a04fed83c56005391d88bcada1b0acd. PostgreSQL18.6-r1, OpenBao2.7.2-r1 and audited unchanged-vendor Mailpit1.33.1-r1 each have zero fixable HIGH/CRITICAL and compatibility8/8/5 PASS. Exact official Redis8.10.3-alpine and RabbitMQ4.3.7-management-alpine each scan clean; real clients2/2 plus quorum/management/restart1 PASS. Browser3 and starter safety7 PASS. See governance/evidence/P1-RV001-001-RUNTIME-REVALIDATION-20261108.json. October results above remain historical.

Browser environment is running at https://localhost:24443/registration (login /login); PostgreSQL127.0.0.1:25432 ->5432; Mailpit SMTP21025 ->1025 and inbox28025 ->8025. Existing isolated data/keys and certificate trust preserved; no operator PostgreSQL5432 change.

Linux CI at merge4ab3f9cd1f64fc2d7e2a2a572fe481813f0c6617 completed the entire private LGTM build. Collector0.163.2 CLI version/components and all16 authentication regressions pass: the old Sermo hash-zero panic is fixed. Full scan still failed with26 raw findings /24 after the two approved historical Tempo corrections: x/net in23 executables plus libbrotli. No private-image41-check compatibility run occurred. Raw complete reports and CI artifact hashes are retained in governance/evidence/P1-RV001-001-CI-BUILD-OBSERVATION-20261108.json.

The old local29a70b1 build was interrupted when Docker became unavailable; it is not still running or accepted. Its partial log remains artifacts/security-followup-20261028/lgtm-29a70b1-interrupted-20261108.log. Do not duplicate it. Current Docker29.7.2 is available, free disk about21GiB; the unchanged18GiB complete-build preflight remains mandatory.

Current repair sources use x/net0.62.0 across the existing builders, add exact matching Loki/Prometheus builds (including both full Prometheus UIs), and apply signed native RHEL libbrotli repair. OS repair prototype passed DNF signature/version/rpm verification; it is not yet full-image scan acceptance. The selected Tempo plugin rebuild at893f0fa scans with only the original two Tempo pseudo-version findings; new executable SHA36d85399e4d2fa0e38e8d33ea6f6171ee3fc494184b3275dcd322b1e268d91c7 differs from original approved963c415f. Existing VEX consent explicitly excludes another binary, so obtain scoped consent for the prepared exact rebind before consumption; do not edit the historical approval. Updated builder emits actual linked-package evidence. Current helper tests69 passed before this seven-line provenance-only helper change; rerun at the new source commit.

Exact next action: validate/commit current component recipes, run selected Tempo proof with linked-package list and rejection tests, prepare an exact consent packet while continuing complete Loki/Prometheus UI builds. After all sources/components pass, run the single complete source-bound helper, fresh23-binary+OS+Node+two-Java scan/SPDX/licenses/guard, private41 compatibility and real PostgreSQL plugin integration. Only then re-review the stale gated adoption draft, synchronize LGTM catalog/runtime/required CI matrix and push coherent committed evidence to existing draft PR21. No FINAL-VALIDATION, final review PASS, runtime LGTM adoption, release or DONE yet.

## Consent, boundaries and recovery

The direct dev/test signing and exact Tempo-plugin VEX approvals are recorded in their P1-RV001-001 approval receipts. VEX expires2026-11-27T00:00:00Z; no core-service VEX, other CVE or new binary/source hash is covered. Kernel BPF host instrumentation is optional, not enabled, and not claimed by the unprivileged fixture; complete assets are retained.

Preserve13 pre-existing dirty paths recorded in artifacts/p2-005-auth-20261021/preserved-dirty-before.json, including docs/context/current, three docs/project files and .vscode/settings.json. Do not stage, overwrite or normalize them. Use a separate context output directory. Refresh native authority/governance/context/source checks at the final handoff. All new task-owned tracked changes must be committed and source-bound before acceptance. The Windows functional-test dialog follow-up is reverified: both current ordinary harness suites and six controlled managed-failure paths pass, reusing the unchanged623d3db fix. See governance/evidence/FUNCTIONAL-HARNESS-REVERIFICATION-20261028.json; no original stack attribution or WER suppression is claimed.
