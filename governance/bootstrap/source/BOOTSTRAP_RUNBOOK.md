# Repository Bootstrap Runbook

## Preconditions
Baseline 1.6.0 ACTIVE/LOCKED; AMD-0001 active; docs 35-37 + pre-P0 package available; no Product decision blocks P0.

## B0 — Local authority bootstrap
Create local repo; materialize blueprint; mirror approved authority under `docs/authority/`; generate SHA-256 manifest; verify; create `BOOT-P0-001`.

## B1 — Governance records
Materialize governance templates, P0 records, AGENTS.md, 20 skills and project-memory files. Apply document-35 review matrix manually until governance self-hosts.

## B2 — Git/GitHub creation
Initialize Git; commit verified bootstrap state; create GitHub repo; register URL; push `main`; configure Rulesets/CODEOWNERS/required checks as workflows appear. GitHub/Actions URLs are outputs, not preconditions.

## B3 — Governance MVP/self-hosting cutover
Implement schemas + `tunner authority`; implement governance validate/status/next/gate/block; import/replay all bootstrap records; self-test PASS; end bootstrap exception.

## B4 — Context/orchestration/test/dev tooling
Implement bounded context, role activation, orchestrator, evidence generator, developer automation, CI/security pipelines and cost/context telemetry.

## B5 — P0 close
```text
tunner authority verify
tunner governance validate
tunner-dev doctor
tunner-dev setup
tunner-dev start
tunner-test dev
tunner context build --current
tunner context verify
tunner governance milestone close-check P0
```
P1-P8 remain blocked until machine-readable PASS.
