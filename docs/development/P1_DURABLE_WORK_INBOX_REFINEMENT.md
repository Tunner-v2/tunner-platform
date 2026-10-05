# P1 Durable Work, Inbox, and Retry Mechanics — Refinement Boundary

**Work item:** `TUN-P1-007`  
**Status:** `IN_PROGRESS`  
**Scope:** generic technical persistence mechanics only

## Authority-backed outcome

This work item may establish a PostgreSQL-backed technical foundation for:

- claim/lease/reclaim of durable work;
- caller-supplied retry eligibility timestamps; and
- consumer-inbox duplicate detection keyed by stable technical identities.

PostgreSQL is the durable authority. RabbitMQ remains transport only. The implementation must preserve at-least-once processing assumptions and allow a worker crash after a claim to leave work reclaimable without a duplicate business mutation.

## Deliberate limits

No default retry timing, backoff, attempt limit, work type, payload meaning, business timer, workflow state, handler dispatch, domain event catalog, or consumer registration is defined here. Those remain owner-domain or later governed scope.

Claims must use deterministic ordering and concurrency-safe conditional state transitions. Current primary sources support PostgreSQL `FOR UPDATE SKIP LOCKED` only for the queue-like claim case and EF Core affected-row counts for explicit conditional transitions.

## Planned vertical slice

1. Model cohesive `Tunner.Database` durable-work and inbox persistence records—never a generic `Common`, `Utils`, or `Helpers` layer.
2. Add a forward-only migration and narrow store/coordinator interfaces that accept identities and eligibility/lease instants from their caller.
3. Test concurrent claim behavior, expired-lease reclaim, conditional completion/release, and duplicate inbox detection without any owner-domain handler.
4. Add a source-scope guard and local runbook. Operator-run database/migration evidence will remain required before completion.

## Explicitly deferred

- RabbitMQ consumer runtime, queues/bindings, acknowledgements, redrive topology, and retry delivery policy.
- Product, Connected Product, user/admin, BFF, public API/SDK, identity, authorization, provider, financial, compliance, and commercial behavior.
- Runtime configuration, credentials, fixed hosts/ports, deployment, production operations, and release work.