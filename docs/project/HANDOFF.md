# HANDOFF

## Work item

- ID: TUN-P0-008
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION
- Branch: change/TUN-P0-007-secrets-foundation
- Scope: PostgreSQL/EF Core migration-system foundation only; no Product/domain tables, business semantics, production deployment, or production credentials.
- Integration: Local P0 execution continues under DEC-0002. No push or protected-main change was made in this validation refresh.

## Completed

- The separate EF Core/Npgsql migrations project, empty forward-only P0 rehearsal migration, operator-scoped design-time connection boundary, and operator runbook are present.
- Current validation passed: clean solution build, functional governance harness, static migration-source validation, credential-free migration-project verification, secret scan (297 files), authority verification (72 artifacts), governance validation (22 records), P0-008 gate check, context verification, and Docker/Compose doctor.
- Docker Desktop 4.87.0 / Engine 29.7.2 and Compose v5.4.0 are available; Compose configuration is valid. The existing PostgreSQL container is healthy but predates the current loopback port mapping, so it must be reconciled before the host-side rehearsal. No images were pulled and no containers were started by Codex.
- DEF-TUN-P0-004-001 is closed. Its historical authority-command finding remains retained in P0-008 evidence, followed by the passing remediation verification.

## Boundaries

- The authority and P0-008 runbooks reserve the live migration rehearsal for an operator. Codex must not start containers or execute `tunner-migrations.ps1 rehearse`.
- A local connection string must never be committed, placed in `.env.local`, or pasted into chat, evidence, or shell history.
- The live rehearsal is local validation only; it is not production migration or deployment approval.

## Exact next action

1. In a local PowerShell session, run `./tools/dev/tunner-dev.ps1 start`; this reconciles the healthy-but-stale PostgreSQL container with the configured loopback `127.0.0.1:5432` port.
2. Set only this local, credential-free value for the current PowerShell process: `$env:TUNNER_MIGRATION_CONNECTION_STRING = 'Host=127.0.0.1;Port=5432;Database=tunner;Username=tunner'`.
3. Run `./tools/dev/tunner-migrations.ps1 rehearse`, then clear the variable with `Remove-Item Env:TUNNER_MIGRATION_CONNECTION_STRING`.
4. Share only sanitized command output (never environment values). Record the successful output as P0-008 operator-rehearsal evidence before any lifecycle advancement.
