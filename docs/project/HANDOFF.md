# HANDOFF

## Work item

- ID: TUN-P0-008
- Milestone/Sprint: P0 / no sprint
- Status: IN_PROGRESS (migration-foundation source builds cleanly)
- Branch: change/TUN-P0-007-secrets-foundation
- Prerequisite: TUN-P0-006 is locally validated; no Product/domain schema is authorized.
- Integration: local P0 execution continues under DEC-0002. No push or protected-main change has been made for P0-008.

## Completed

- Completed and remediated the P0 foundation audit; all registered P0 work-item gates now return READY.
- Created the canonical TUN-P0-008 work record and bounded TASK context selection.
- Recorded current EF Core migration, EF Core 10, Npgsql 10, and migration-application primary-source evidence.
- Added and built the P0-only database/migrations projects using EF Core 10.0.0, Npgsql 10.0.0, and a patched System.Security.Cryptography.Xml 10.0.12 dependency pin. Commit: bcb8e1d.

## Boundaries

- P0-008 may establish only migration-system mechanics: EF Core/Npgsql foundation, forward-only migration policy, design-time/execution separation, and a credential-free operator-run rehearsal path.
- Do not create Product/domain tables, business semantics, application startup migration behavior, production deployment, or credential values.
- Do not start containers or run a database migration in this session.

## Next action

- Complete P0-008 static acceptance evidence and role reviews. Any local migration rehearsal remains operator-run and must not include Product schema or credential values.