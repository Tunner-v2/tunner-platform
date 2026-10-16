# Local RabbitMQ confirmed-outbox dispatch foundation

`TUN-P1-006` adds a small internal transport capability after the transactional-outbox foundation. It is not a Product event system, a hosted worker configuration, or a consumer implementation.

## What this capability does

`EfOutboxDispatchStore` reads only committed `platform.outbox_messages` whose `published_at` is null, in a deterministic occurred-at then event-id order. `OutboxDispatchCoordinator` gives each immutable outbox projection to `IConfirmedOutboxPublisher` and conditionally sets `published_at` only after the publisher completes successfully.

`RabbitMqConfirmedOutboxPublisher` uses the official `RabbitMQ.Client` package with a long-lived connection and channel. The channel is created with publisher confirmations and publisher-confirmation tracking enabled, and a semaphore serializes use of the shared publisher channel. It declares one durable internal topic exchange, publishes the opaque JSON payload with the generic event type as routing key, uses a persistent delivery mode, and sets `mandatory: true`. The persisted generic envelope metadata is preserved as internal AMQP properties and headers: event id, envelope version, type, schema version, occurred-at timestamp, source, subject, data classification, and any present resource, operation, correlation, or causation identifiers. This is a transport projection of the approved generic envelope, not a Product event schema.

With RabbitMQ.Client 7 confirmation tracking, a broker nack or an unroutable mandatory publish completes the publish call with a failure. In either case the coordinator does not mark the outbox row published. The record remains available for a later governed dispatch attempt.

## Deliberate delivery boundary

This is an at-least-once foundation, not exactly-once processing:

- If publishing fails before broker confirmation, `published_at` remains null.
- If RabbitMQ confirms the publish but recording `published_at` fails, a later dispatcher may publish the same message again.
- A conditional mark that affects no row means another dispatcher already recorded the publication; it is not counted as a new completion.
- Consumer inbox/idempotency, acknowledgements, retry/redrive, durable work, queues, bindings, and business reactions are deliberately deferred to `TUN-P1-007` or later approved work.

The P1-006 exchange is intentionally not a domain topology. It declares no queue, binding, consumer, or Customer/work-item routing rule. A publish against this foundation before a later governed binding exists is unroutable and therefore stays pending because the message is mandatory.

## Runtime and configuration boundary

The capability is **not automatically registered** in `TunnerWorkerHost`. It accepts a runtime broker URI and exchange name only when a later governed composition scope constructs it. `TUN-P1-016` owns the environment configuration and secret-delivery boundary. Do not commit a broker URI, credential, user profile, workspace path, host, or port into this repository.

This is not production deployment evidence. It does not establish high availability, quorum queues, topology recovery policy, operational alerting, or a production credential mechanism.

## Operator-run verification

Restore and build the solution before running the focused unit suite if assets are not current:

```powershell
dotnet restore .\Tunner.Platform.slnx
dotnet build .\Tunner.Platform.slnx --no-restore
powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerRabbitMqDispatchSource.ps1 -RepositoryRoot (Get-Location).Path
dotnet test .\tests\Tunner.Worker.Tests\Tunner.Worker.Tests.csproj --no-restore
```

These deterministic tests prove the coordinator sequence and bounded failure behavior without opening a broker connection. A real broker proof must use only an operator-supplied non-production endpoint after the applicable runtime configuration and later consumer-topology authority exists; it must not use a committed fixed port or credential.
