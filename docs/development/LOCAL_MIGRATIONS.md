# Local migration foundation

P0-008 establishes only migration-system mechanics. It contains no Product/domain entities or tables.

## Rules

- Migrations are forward-only and immutable once integrated.
- Application startup must not execute production migrations.
- The migration project uses a design-time factory and requires `TUNNER_MIGRATION_CONNECTION_STRING` only in the operator process. Never commit that value or add it to `.env.local`.
- Production uses a separately authorized migration identity and a reviewed migration bundle or SQL artifact. This P0 document does not authorize production execution.

## Operator-run local rehearsal

After P0-006 PostgreSQL is running, an operator may supply a local-only connection string in the current PowerShell process and run EF tooling from `src/Tunner.Database.Migrations`. Inspect generated migration source before applying it. Do not use `EnsureCreated` with migrations.

No migration command is run by Codex. Product/domain schema work requires later governed scope.