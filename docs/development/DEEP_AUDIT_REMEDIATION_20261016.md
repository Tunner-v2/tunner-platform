# Deep-audit remediation, 2026-10-16

This authorized corrective slice addresses AUD-01, AUD-02 and AUD-03 from the Drive/FRD alignment audit. P0-RV004 owns the control-plane correction; P0-030-RV002, P0-032-RV002, P0-033-RV002 and P1-022-RV002 preserve same-scope ownership, and P1-024-RV002 owns installer execution receipts. Historical CLOSED/DONE records and the locked baseline are unchanged.

## Behavior

- Nine current Drive sources are stored under docs/authority/current/development with exact source IDs, modification times and SHA-256 pins. A separate required supplemental registration retains the existing hash-pinned current-authority index and bootstrap evidence. Missing, mutated or malformed supplemental files fail verification.
- Context selects the current AMD-0002 clarification and applicable updated documents. Ordinary work-item defects and required evidence appear in retrieval and durable recovery. An explicitly non-default output keeps generated project state inside that pack, preserving existing project files.
- CI restores/builds and executes governance, platform, installer and UI checks, plus actual container integration tests, browser tests, dependency audits and artifact receipts. NuGet inventory/SBOM coverage spans all .NET scopes; frontend audit output is retained separately. Pinned actions and read-only workflow permissions remain.
- The installer emits and saves non-secret R2 run receipts containing UTC execution bounds, the built assembly's source revision and SHA-256, safe target identity, object result and cleanup outcome. A failed cleanup cannot become a passing receipt. Runtime receipts remain under the ignored local profile directory.

## Evidence boundary

The prior operator-attributed live R2 result remains unchanged. No new live provider run is implied by component tests. The existing interactive private-token input is retained; a future operator run of ./tools/dev/setup-r2.ps1 writes a receipt under local-config/r2/receipts. Do not send credentials in chat.

Validation is recorded in the Revision evidence envelopes after execution against the committed candidate. The pipeline is validated locally; no remote GitHub run or protected-main merge is implied.

## Handoff

Pre-existing docs/context/current, docs/project and .vscode changes are excluded from this slice. Disposable patch files, diagnostic probes, test fixtures and test outputs are under artifacts/audit-remediation-20261016 and artifacts/audit-remediation-tests; they are not repository deliverables. User explicitly authorized reviewed git apply after the patch editor rejected governance writes.

The exact next action is to complete native Revision gates and P1 committed-readiness/milestone checks, then evaluate the next declared milestone transition with recorded acceptance/release authority. Do not overwrite historical closure or infer production approval.
