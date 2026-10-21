# Security remediation and UI revalidation — 21 October 2026

Current technical result: P0-RV009 and P1-018-RV002 are CLOSED at a61b1434029eeeaf5b8026975e37e4313306a642 after native checks passed. Current P0 enablement and GLOBAL_UI gates PASS. P1-RV001 remains ACTIVE and release-blocking; this report does not declare the whole security milestone or P2 complete.

## Authority and scope

Baseline1.6.0 plus active amendments including AMD-0008/0009 remain unchanged. Native verification covered196 artifacts; current Drive index metadata was rechecked21October and still points to the approved17October authority line. The current index and verified baseline references were used; reading current effective Doc10/31 is not represented as byte verification of those mirrors. The AMD-0005 resolution passes and source-freshness checks are recorded in the linked evidence.

The user explicitly approved maintained security rebuilds and reviewed Git patches. No new Figma deviation was implemented. Existing genuine scoped Product consents remain authoritative. All12 specialist contracts were applied by the executing agent; no independent human review or new Product acceptance is claimed.

## Changes and committed sources

- 8afa4769f9b8553b619c0455abbb6890aff2974b: Vite8.2.4 minimal patch; maintained PostgreSQL/OpenBao/Mailpit recipes and isolated compatibility checks; pinned source/build provenance; fail-closed CI enforcement.
- fd58093d839aec7d55e7578b24a35a804e984eaf: active append-only shared frontend Revision with inherited34-path ownership, used for the fresh frontend run. No classified behavior changed after8afa.
- 2f9c8e0562d4a301d8fcc60463de8c58c286295f: committed actual scan/log/screenshot evidence and technical reviews.
- a61b1434029eeeaf5b8026975e37e4313306a642: native-approved closure of the two technical Revisions. Historical closed records are retained.

Relevant source groups: `.github/workflows/security-supply-chain.yml`, `.github/workflows/create-pull-request.yml`, `tools/security/Assert-TunnerCiResult.ps1`, `tools/security/Build-TunnerSecurityImages.ps1`, `infra/security-images/`, three `tools/validation/Test-Tunner*Image.ps1` fixtures, `infra/docker/.env.example`, `infra/docker/compose.yaml`, `web/package.json` and `web/pnpm-lock.yaml`. The only shared UI behavior-file changes in this security slice are the Vite package/lock patch.

## Actual validation

| Check | Result | Source / evidence |
|---|---|---|
| Governance functional suite | PASS |8afa; includes60 RV and15 closed-ownership checks |
| Authority/context audit | PASS13 |8afa |
| CI enforcement regressions | PASS27 |8afa unchanged source; final repeated check |
| Private image compatibility | PASS21 |8 PostgreSQL,8 OpenBao,5 Mailpit at8afa |
| Full private image vulnerability scans | PASS |3 images,0 fixable HIGH/CRITICAL, nonempty target coverage |
| Frontend build and dependency audit | PASS |fd58093; zero audit advisories |
| Frontend unit / browser | PASS31 / PASS38 |10 theme+21 contract;16 shared+22 registration |
| Additional shared screenshot collection | PASS16 |Same source; captures were cleared by the second suite |
| Rendered comparison | PASS29 |28 pixel-identical; mobile Account Ready differs by46 pixels at maximum channel difference1, visually reviewed |
| SAST / secrets / container policy / source guards | PASS |No severity reduction or CVE suppression |
| Native global UI | PASS10 items |5 screens,3 forms,2 shared fixtures;53 planned inventory items excluded |
| Native P0 enablement | PASS |40 satisfied conditions after technical closures |
| Current vendor image release readiness | BLOCKED |Unresolved vendor findings and LGTM signed-plugin boundary |

Fresh screenshots, approved Figma references, per-item URL-001..006 / VAL-001..008 / UX-FID-001..008 records and genuine existing exceptions are linked from `governance/evidence/GLOBAL-UI-COMPLIANCE.json`. The original global proof is retained under `governance/evidence/history/GLOBAL-UI-COMPLIANCE-bea01cf.json`; original scoped closure receipts were not replaced. Prior backend bypass/authorization evidence is retained: this patch changes no backend source.

Durable evidence: `governance/evidence/security/20261021/image-results.json`, complete private scan JSONs and build receipts in that directory; `governance/evidence/ui/security-20261021/test-index.json` and `visual-comparison.json`; `governance/evidence/P0-RV009-NATIVE-GATES-20261021.json`; `governance/evidence/P0-RV009-REQUIRED-CHECKS-20261021.json`.

## Docker images and ports

These are configured defaults in `infra/docker/.env.example`, not a claim that every service is running. Bindings use127.0.0.1. The browser starter can select free alternate ports, so its runtime status is authoritative.

| Service | Configured image | Host → container port |
|---|---|---|
| PostgreSQL |`postgres:18.6` |25432 →5432 |
| RabbitMQ |`rabbitmq:4.3.6-management` |25672 →5672;25673 →15672 management |
| Redis |`redis:8.10.2` |26379 →6379 |
| OpenBao |`openbao/openbao:2.7.0` |28200 →8200 |
| Mailpit |`axllent/mailpit:v1.31.6` pinned to sha256:23fe39e81bde596a918000b3135c0f85dc8bb4c9ea6be274b051143f3e300387 |21025 →1025 SMTP;28025 →8025 inbox |
| LGTM |`grafana/otel-lgtm:0.34.0` |23000 →3000 Grafana;24317 →4317 OTLP gRPC;24318 →4318 OTLP HTTP |
| Optional placeholders |`busybox:1.37.0` |No published application port |

Tested private candidates, not adopted as the Compose defaults:

| Image | Actual local OCI image ID |
|---|---|
|`tunner-security/postgres:18.6-r1` |`sha256:297d7db03adf82fff8a53e817e7cad0f6f154e01822af47331ed94b286beda06` |
|`tunner-security/openbao:2.7.1-r1` |`sha256:84d609ce13df5e673522ffe504268867ef816cc50f9ac48a8d2efbc30c0ec96e` |
|`tunner-security/mailpit:1.31.6-r1` |`sha256:1fbd3039a6676dd3ce58e45b23ec95d0ad51d8351dfd46c9c745973742868604` |

Fixtures used disposable owned containers/volumes. PostgreSQL published no host port; Bao/Mailpit used dynamically allocated loopback ports. Their cleanup completed. At final inspection only the unrelated `oxganic-postgres-dev` (`postgres:17-alpine`,127.0.0.1:5432→5432) was running; it was preserved. `tools/dev/tunner-browser.ps1 status` reports the browser application stopped. Its preferred HTTPS app port is24443; start it with `start-browser.cmd` and use status for actual selected ports.

## Remaining blockers and next authorized action

The unchanged HIGH/CRITICAL policy still blocks vendor LGTM. The20October0.37.0 candidate scan found110 HIGH findings; this is dated evidence, not a claim about a new21October whole-image scan. The latest inspected vendor-signed PostgreSQL datasource package13.0.4 has5 fixable HIGH findings. Rebuilding its binary invalidates the vendor-signed manifest. The maintained-build approval does not authorize unsigned plugins, removed plugins, new plugin identities, a different trust root, or disabled integrity checks.

Recorded AMD-0005 boundary: `governance/evidence/P1-RV001-LGTM-SIGNED-PLUGIN-BOUNDARY-20261021.json`. No new Product decision is required for the completed technical patch. A separately reviewed signing/trust ADR is genuinely required before a proposed identity/trust change; otherwise obtain clean compatible vendor-signed packages and rebuild/scan the complete LGTM stack. Do not implement a trust deviation from this report.

P1-RV001-001 stays open for complete container remediation and governed local adoption. P1-RV001-002 explicitly requires001 MERGED_TO_MAIN; a protected-main integration approval is a genuine dependency for that child only. Required main checks now effectively include Security baseline,Container security gate and Web build tests and dependency audit, all GitHub Actions app15368, with strict up-to-date behavior. One codeowner approval,stale dismissal,no-delete,no-force-push and empty bypass actors were preserved. Actual aggregate failure at published4cb8460 demonstrates fail-closed behavior.

Next authorized action: run governance next; finish scoped LGTM signing design/research or compatible vendor remediation underP1-RV001-001, while continuing eligible P2-002/003/004 validation. Prepare mandatory roles/context before P2-005 sign-in implementation. No whole-P2 completion,protected-main merge,registry publication,production deployment or release is authorized by this handoff.

## Handoff and limitations

Branch `codex/global-ui-compliance`, existing PR18 (`https://github.com/Tunner-v2/tunner-platform/pull/18`), parent PR17 remains a dependency. Source-bound local checks above are distinct from the new pushed GitHub run; do not report an unfinished or failed CI run as green.

All13 preexisting dirty context/project/editor paths were preserved and excluded from staging. Final hashes match `artifacts/security-remediation-20261020/preserved-before-final-validation.json`; the unavailable pre-interruption byte snapshot is not claimed. Use the isolated final context pack under `artifacts/security-remediation-20261020/final-context/` to resume without overwriting those files.

Failed/superseded attempts are retained: initial npm audit HTTP503 responses were not PASS; the pinned Trivy production-lock audit required nonempty199-package coverage and passed. Initial PostgreSQL candidate74935e had a truncated binary and failed even the stock runtime; it was rejected without asserting an unproven vendor cause. The healthy pinned baseline plus exact OS patches passed all checks. Disk-full/read-only Docker failures were resolved after operator cleanup/restart; no broad prune or unrelated volume deletion occurred. Initial raw-hash UI receipt mismatch was corrected by restoring only owned committed LF evidence; hashes and gate now pass.
