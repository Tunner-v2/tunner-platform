# CURRENT_STATE

## Baseline

- Documentation: Baseline 1.6.0 (ACTIVE / LOCKED).
- Active amendments: AMD-0001 and AMD-0002.
- Authority verification: PASS for 125/125 bootstrap-bundle entries and 54/54 Pre-P0 entries; canonical Drive folders and controlling documents are readable. Full source-to-mirror diff remains TUN-P0-033 scope.

## Current milestone

- ID: P0
- Status: ACTIVE
- Goal: establish the self-governing development control plane before P1–P8.

## Active work items

| ID | State | Blocker | Next action |
|---|---|---|---|
| TUN-P0-001 | DONE | none | Bootstrap complete |
| TUN-P0-002 | VALIDATION | none | Retain AMD-0002 contribution-policy evidence for protected incremental P0 PRs |
| TUN-P0-003 | VALIDATION | none | Retain schema-validation evidence for protected incremental P0 PRs |
| TUN-P0-004 | VALIDATION | none | Retain AMD-0002 dependency-readiness evidence for protected incremental P0 PRs |
| TUN-P0-005 | VALIDATION | none | Retain bounded context-pack evidence for protected incremental P0 PRs |
| TUN-P0-006 | VALIDATION | none | Runtime health and stderr-wrapper remediation are PASS; retain Compose evidence for protected incremental P0 PRs |
| TUN-P0-007 | VALIDATION | none | Retain credential-free OpenBao policy/bootstrap, local scan, and validation evidence |
| TUN-P0-008 | VALIDATION | none | Local migration rehearsal is PASS; retain validation evidence for protected incremental P0 PRs |
| TUN-P0-019 | VALIDATION | none | Retain GitHub governance evidence for protected incremental P0 PRs |

## Build/test state

- .NET SDK: 10.0.401 installed and used for local P0 validation.
- Tunner.Governance build: PASS (0 warnings, 0 errors).
- Functional harness: PASS, including fresh and stale generated-context scenarios and active-authority-summary inclusion.
- Governance live validation: PASS for 22 records with no diagnostics.
- P0 foundation audit: PASS with three remediated governance/documentation findings. Recoverable predecessor commits remain retained in Git history.
- Governance defect: DEF-TUN-P0-004-001 is CLOSED. The new read-only `tunner authority verify` command validated all 72 imported authority artifacts and blocks governance/context execution on any hash mismatch; P0-008 remains READY.
- Context pack: PASS; `docs/context/current/` is manifest-first, hash-verifiable, and CURRENT for TUN-P0-008. It includes the active-authority summary and AMD-0002 source.
- P0-006 static Compose source/configuration validation: PASS; `tunner-dev doctor` confirmed Docker Desktop 4.87.0 / Engine 29.7.2 and Compose v5.4.0 without pulling images or starting containers. Default host bindings now use Tunner-only loopback ports 25432/25672/25673/26379/28200/21025/28025/23000/24317/24318; container-internal ports remain unchanged.
- P0-006 runtime dependency health: operator-run output confirmed PostgreSQL, RabbitMQ, Redis, OpenBao, Mailpit, and LGTM healthy after the corrected readiness probes and OpenBao configuration path fix. DEF-TUN-P0-006-003 is CLOSED: its deterministic no-Docker stderr regression fixture passed in both the current shell and powershell.exe, and the real Compose retry completed normally with all declared services Healthy.
- P0-007 secrets foundation: PASS for static policy/bootstrap validation, a credential-free repository scan, generated negative fixture, clean build, functional harness, and context verification. No local bootstrap or secret value was executed or recorded.
- P0-008 migration foundation: PASS for EF Core 10.0.12, Npgsql EF Core 10.0.3, and dotnet-ef 10.0.12 restore; credential-free source-only migration rehearsal fixture; secret scan (297 files); clean build; governance functional harness; authority verification (72 artifacts); Docker/Compose doctor; and a successful operator-run local application of `20260930173000_P0MigrationFoundation`. The expected model-snapshot notice reflects the intentionally model-free P0 scope; no Product/domain schema or production migration authority was introduced.
- NuGet vulnerability metadata: no vulnerable packages reported by current source metadata.
- Bundle integrity: PASS (125/125). Pre-P0 package integrity: PASS (54/54). Repository mirror integrity: PASS (125/125).

## Delivery model and controls

- DEC-0002 supersedes DEC-0001 and permits protected incremental P0 pull requests from the temporary P0-only local integration branch: change/TUN-P0-control-plane.
- Each P0 work item is separately committed, validated, evidenced, and governed locally.
- A locally validated P0 prerequisite may unblock dependent P0 work, but no P0 item becomes DONE until its protected integration and the P0 enablement gate are complete.
- Each protected incremental P0 pull request requires an independent ai-dev code-owner approval; GitHub may auto-merge only after protected requirements pass. P0 remains incomplete until its separate enablement gate closes.

## Exact next authorized action

- PR #6 merged to protected main. TUN-P0-007 is locally validated and committed as 7f2f6e4 on its separate branch; retain its evidence and do not introduce Product or production-secret scope.
- TUN-P0-006 and TUN-P0-008 are locally validated in the repository `VALIDATION` state. The Compose retry and local migration rehearsal passed and are recorded without retaining any connection value. Preserve their protected-main integration and P0 enablement gates; do not implement Product/domain schemas or treat the local rehearsal as production migration authority. Run governance next to select the next eligible P0 work item.