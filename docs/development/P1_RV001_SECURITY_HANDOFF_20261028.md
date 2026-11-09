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

## Current execution direction

User explicitly directed feature development to resume using a workable prebuilt image rather than continuing custom monitoring rebuilds. Use exact grafana/otel-lgtm:0.38.1@sha256:0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7 for local dev/test; this exact image previously passed41 complete stack/gRPC compatibility checks. No private LGTM rebuild is active or scheduled automatically. The optional custom candidate CI job is removed; all seven declared image scans, severity thresholds and required aggregate checks remain intact. Findings remain release-blocking; this is not a security waiver or production approval.

Current source: localdf84597 includes validated Loki78/368-package scan and fresh Windows harness proofs; publishedb3d07ff has successful security baseline/web and all six non-LGTM runtime scans. Linux candidate job128940797864 at merge58648b684cced8ddaef8f3ebe6dbe351d1fe27d2 failed during Prometheus-plugin test setup on missing Go dependency checksums. Do not spend more time fixing/rebuilding it now. Log: artifacts/security-followup-20261028/linux-b3d07ff-complete-job.log. Full private44-check compatibility and image scan did not run. P1-RV001-001 remains IN_PROGRESS/NOT_READY, with final security evidence and reviews outstanding.

Preserved current proofs: P1-RV001-001-RUNTIME-REVALIDATION-20261108.json (PostgreSQL/OpenBao/Mailpit8/8/5, Redis/Rabbit real clients2/2+restart1, real browser3, starter7); PROMETHEUS-UI-VALIDATION-20261108.json (878 upstream UI tests and clean production graphs); TEMPO-VEX-REBIND-VALIDATION-20261108.json (actual new approved guard/corrected scan); LOKI-COMPONENT-VALIDATION-20261108.json (78 tests,368 Go packages clean); WINDOWS-HARNESS-VALIDATION-20261108.json (two normal suites and six controlled failures, zero warnings/errors). These are component proofs, not full private-stack acceptance.

Browser application is ready at https://localhost:24443/registration; password login /login; inbox http://127.0.0.1:28025; PostgreSQL127.0.0.1:25432 ->5432; SMTP21025 ->1025. Browser isolation needs only PostgreSQL/Mailpit/User BFF. The monitoring rebuild and its local18GiB disk preflight are not P2 feature-development prerequisites.

Exact next action: continue TUN-P2-006 governed refinement/startup reviews and implement the next approved identity slice. Isolated context: artifacts/p2-006-assurance-20261108/context. Retain security defect DEF-P1-CONTAINER-SCAN-001 and revision P1-RV001 as open release work. Revisit clean vendor availability or private rebuilds separately when directed; do not apply artifacts/security-followup-20261028/lgtm-adoption-20261108-DRAFT-NOT-APPLIED.patch, which is superseded by this direction.

## Consent, boundaries and recovery

The direct dev/test signing and exact Tempo-plugin VEX approvals are recorded in their P1-RV001-001 approval receipts. VEX expires2026-11-27T00:00:00Z; no core-service VEX, other CVE or new binary/source hash is covered. Kernel BPF host instrumentation is optional, not enabled, and not claimed by the unprivileged fixture; complete assets are retained.

Preserve13 pre-existing dirty paths recorded in artifacts/p2-005-auth-20261021/preserved-dirty-before.json, including docs/context/current, three docs/project files and .vscode/settings.json. Do not stage, overwrite or normalize them. Use a separate context output directory. Refresh native authority/governance/context/source checks at the final handoff. All new task-owned tracked changes must be committed and source-bound before acceptance. The Windows functional-test dialog follow-up is reverified: both current ordinary harness suites and six controlled managed-failure paths pass, reusing the unchanged623d3db fix. See governance/evidence/FUNCTIONAL-HARNESS-REVERIFICATION-20261028.json; no original stack attribution or WER suppression is claimed.
