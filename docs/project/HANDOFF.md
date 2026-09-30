# HANDOFF

## Work item

- ID: TUN-P0-006
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (local P0 integration under DEC-0001)
- Branch: change/TUN-P0-control-plane
- Commits: 78c8466 (foundation), 9ef4ee4 (LGTM health-probe correction)
- Prerequisite: TUN-P0-001 (complete); P0-006 cannot enter DONE before runtime evidence and the final protected P0 PR.

## Completed

- P0-006 added a secret-free local Docker Compose foundation for PostgreSQL 18, RabbitMQ 4.3 management, Redis 8, OpenBao, Mailpit, and Grafana OTEL-LGTM (including a local OpenTelemetry Collector).
- All published host ports bind to loopback by default and can be overridden only for port conflicts through an ignored local environment file.
- API and Worker health placeholders are opt-in and contain no Product implementation.
- `tools/dev/tunner-dev.ps1` provides doctor, setup, start, stop, health, and logs; `docs/development/LOCAL_DOCKER.md` documents its bounded local-only use.
- OpenBao uses normal server mode without a committed root token. P0-007 owns initialization, unseal handling, least-privilege policy, and secret injection.

## Tests/evidence

- P0-006 static source and Compose contract: PASS.
- `tunner-dev doctor`: PASS against Docker Desktop 4.87.0 / Engine 29.7.2 and Docker Compose v5.4.0; no image pull or service start was performed.
- Governance validation: PASS for 13 records, no diagnostics.
- P0-006 generated context pack: PASS / CURRENT.
- `git diff --check`: PASS before this state update; rerun after any change.
- R&D evidence: `governance/evidence/TUN-P0-006-RD-SERVICES.json`.
- Role review evidence: `governance/evidence/TUN-P0-006-ROLE-REVIEWS.json`.

## Decisions/blockers

- P1–P8 Product behavior remains out of scope until P0 closes.
- DEC-0001 allows local P0 integration only. No P0 work item is DONE until the final protected P0 PR merges.
- GitHub automation may create/auto-merge a PR after requirements pass; it cannot approve, bypass, or impersonate the required ai-dev code-owner review.
- Initial user-run runtime evidence showed all core services except LGTM healthy. DEF-TUN-P0-006-001 records the failed wget health probe; the Compose correction now uses LGTM's /tmp/ready sentinel and requires one retest. The expected OpenBao state is responsive but uninitialized/sealed until P0-007.

## Context state

- Generated pack: docs/context/current/manifest.json.
- P0-006 selection: governance/context/CTX-TUN-P0-006-001.yaml.
- Pack scope: infrastructure authority/ADRs, prerequisite state, R&D evidence, required tests/evidence, active roles, project state, bounded Git history, and explicit Product/R2/OpenBao-bootstrap exclusions.

## Exact next action

- Run `./tools/dev/tunner-dev.ps1 start`, then `./tools/dev/tunner-dev.ps1 health` from the repository root in PowerShell and retain the output. Do not pass secrets or create `.env.local` credentials. Stop with `./tools/dev/tunner-dev.ps1 stop` when finished. After the runtime evidence is recorded, begin only the separately governed P0-007 OpenBao bootstrap/policy scope.