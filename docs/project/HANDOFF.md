# HANDOFF

## Work item

- ID: TUN-P0-006 / DEF-TUN-P0-006-003
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION
- Branch: change/TUN-P0-007-secrets-foundation
- Scope: Remediate PowerShell 7 handling of Docker Compose normal stderr progress and move all Tunner host bindings to a dedicated loopback port range; container-internal topology, credentials, Product behavior, and production deployment are unchanged.
- Integration: Local P0 execution continues under DEC-0002. No push or protected-main change was made in this validation refresh.

## Completed

- Added a native Docker invocation boundary that keeps Docker Compose progress visible but uses its exit code—not ordinary stderr—for failure detection.
- Current validation passed: P0-006 source validation and a deterministic fake-Docker regression test that emits container-progress output to stderr with exit code zero. The wrapper completed doctor and start without contacting Docker.
- Docker Desktop 4.87.0 / Engine 29.7.2 and Compose v5.4.0 are available. The credential-free defaults now use Tunner-only loopback ports, including PostgreSQL `25432`; the real Compose retry remains operator-run and will reconcile the existing PostgreSQL container before P0-008 rehearsal.
- DEF-TUN-P0-006-003 is in VALIDATION until the real Compose retry confirms normal stderr progress no longer terminates the wrapper.

## Boundaries

- The authority and runbooks reserve the real Compose retry and live migration rehearsal for an operator. Codex must not start containers or execute `tunner-migrations.ps1 rehearse`.
- A local connection string must never be committed, placed in `.env.local`, or pasted into chat, evidence, or shell history.
- The live rehearsal is local validation only; it is not production migration or deployment approval.

## Exact next action

1. Run `./tools/dev/tunner-dev.ps1 start` and share its sanitized output. This is the required real Compose retry for DEF-TUN-P0-006-003 and reconciles the existing PostgreSQL container.
2. If it succeeds, set only this local, credential-free value for the current PowerShell process: `$env:TUNNER_MIGRATION_CONNECTION_STRING = 'Host=127.0.0.1;Port=25432;Database=tunner;Username=tunner'`.
3. Run `./tools/dev/tunner-migrations.ps1 rehearse`, then clear the variable with `Remove-Item Env:TUNNER_MIGRATION_CONNECTION_STRING`.
4. Share only sanitized command output (never environment values). Record the successful output as P0-008 operator-rehearsal evidence before any lifecycle advancement.
