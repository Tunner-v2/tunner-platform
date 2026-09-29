# ADR-0001 — Durable Messaging: PostgreSQL Outbox/Inbox + RabbitMQ

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Context
Tunner requires reliable post-commit asynchronous delivery without allowing a broker failure to roll back the authoritative owner-domain transaction.

## Decision
Use PostgreSQL transactional outbox/inbox as the durability and idempotency boundary, with RabbitMQ 4.3 current supported patch as the internal asynchronous transport. Publisher confirms are required. Critical replicated queues use quorum queues where their safety/availability characteristics are appropriate.

## Boundaries
- RabbitMQ transports committed work; it is not business truth.
- A business transaction commits its state and outbox atomically.
- Consumer inbox/idempotency prevents duplicate business mutation.
- Replay/redrive preserves event lineage and must not imply a new external side effect.
- Long-lived business timers remain persisted in PostgreSQL durable-work/scheduler state unless a later ADR changes that.

## Rejected alternatives
- Broker-only durability without an outbox.
- Redis Pub/Sub as durable business messaging.
- Direct synchronous fan-out to independent subscribers.

## Revisit trigger
Measured throughput, multi-region topology, broker limitations, or an approved service decomposition requiring a different transport.
