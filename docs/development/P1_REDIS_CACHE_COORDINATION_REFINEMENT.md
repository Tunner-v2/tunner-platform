# P1 Redis Cache and Ephemeral Coordination Refinement

## Authorized boundary

TUN-P1-008 may establish only a cohesive technical capability for caller-owned, finite-TTL cache entries and short-lived coordination tokens. Redis is non-authoritative: durable and Product truth remains in the owner system of record.

## Reuse assessment

The repository has Redis as a local dependency service but no application Redis adapter, cache store, coordination primitive, or reusable Redis capability. P1-008 therefore may add a cohesive Platform-owned capability only after its expanded context and mandatory specialist readiness reviews pass.

## Required safeguards

- The caller supplies entry identity, opaque value, and a finite TTL.
- Conditional acquisition must report whether it acquired a token; release must not delete a token owned by another caller.
- Redis unavailability must not promote cache or coordination state into durable authority.
- No business key namespace, retry value, lock duration, identity/session behavior, or Product policy may be inferred.

## Explicit exclusions

No Redis connection/configuration/credentials, fixed port, deployment, cache payload policy, invalidation policy, distributed algorithm, leader election, Product workflow, financial/audit/identity truth, public API, SDK, or Connected Product behavior is authorized by this refinement record.