# Local transactional-outbox verification

`TUN-P1-003` establishes only the shared PostgreSQL transaction and outbox primitive. The single table, `platform.outbox_messages`, has the Platform Persistence/Outbox technical owner. It stores generic envelope metadata and an opaque JSON payload; it does not create a Product/domain table or choose business event semantics.

The transactional coordinator sees only `ITransactionalOutboxContext`, a deliberately narrow capability: begin one transaction, add the outbox message, and save changes. It never passes a general EF Core context to a caller. A future owner module supplies its own context that maps only its owned table(s) plus the shared outbox mapping, so the coordinator cannot become a cross-domain mutation surface. An owner callback may flush its staged state while the coordinator transaction is active, but it must never commit independently. The real integration test uses an equivalent `OutboxAtomicityTestDbContext` with a disposable `test_support.outbox_owner_write_probe` table; that test-only schema is created inside the throwaway container and is never part of a production migration.

The canonical event-envelope-compatible `published_at` column is nullable. P1 persists it as `null` and provides no publish, claim, retry, or state-transition behavior. A later separately governed post-commit transport item must own any change to that field as well as broker/consumer behavior.

This slice deliberately excludes transport publishing, broker topology, consumer handling, inbox/idempotency, durable work, leases, timers, retry/redrive behavior, providers, Product APIs, and UI behavior.

## Operator-run verification

Run restore/build before the command below if package assets are not current. The integration test starts a disposable PostgreSQL container that receives a random host port; it does not bind a configured local PostgreSQL port.

```powershell
$env:TUNNER_RUN_CONTAINER_TESTS = '1'
try {
    .\tools\dev\tunner-test.ps1 integration
}
finally {
    Remove-Item Env:TUNNER_RUN_CONTAINER_TESTS -ErrorAction SilentlyContinue
}
```

Rehearse the forward-only migration against the operator's local non-production database only. Supply the connection through the environment; do not commit a connection string, credential, or host port.

```powershell
$env:TUNNER_MIGRATION_CONNECTION_STRING = '<operator-supplied non-production PostgreSQL connection string>'
try {
    .\tools\dev\tunner-migrations.ps1 rehearse
}
finally {
    Remove-Item Env:TUNNER_MIGRATION_CONNECTION_STRING -ErrorAction SilentlyContinue
}
```

Do not use the rehearsal command for production deployment. The result is required evidence before `TUN-P1-003` can move to `DONE`.