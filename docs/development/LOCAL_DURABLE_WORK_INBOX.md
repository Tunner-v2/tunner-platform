# Local Durable Work and Inbox Verification

`TUN-P1-007` adds PostgreSQL-backed technical persistence only. It does not start a worker, RabbitMQ consumer, handler, timer, retry policy, or Product workflow.

## What the foundation does

- Leases opaque caller-owned durable-work records only when they are retry-eligible and not currently leased.
- Conditionally completes or releases a lease using its lease token.
- Reclaims a lease only after the caller-provided lease expiry.
- Reserves a consumer/message pair atomically, allows release after a failed owner operation, and suppresses later reservations after successful completion.

The caller supplies work identity, work kind, opaque JSON payload, retry-eligibility instant, lease expiry, consumer key, and message identity. This capability establishes none of their Product meaning or policy values.

## Operator validation

The integration suite uses Testcontainers with a random mapped PostgreSQL port and does not rely on the local Compose PostgreSQL port. Do not change committed Compose mappings to use a port owned by another solution.

After the P1-007 implementation records specify the exact validation commands, run the source guard and the real PostgreSQL integration tests with `TUNNER_RUN_CONTAINER_TESTS=1` in a non-production environment.

Do not treat a successful migration rehearsal or integration test as a production deployment, consumer activation, or Product acceptance.