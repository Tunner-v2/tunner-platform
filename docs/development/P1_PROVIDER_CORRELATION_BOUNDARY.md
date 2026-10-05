# P1 provider correlation and evidence boundary

TUN-P1-015 implements the internal provider-neutral correlation/idempotency foundation defined by Document 23 sections 1, 2, 7 and 9, Document 32 sections 1, 2, 4 and 15, and accepted ADR-0014. Canonical Drive copies were checked on 2026-10-05. Stripe is already selected; the earlier missing-contract pass omitted these approved sources. No new authority decision is required.

## Technical contract

The existing Tunner.Platform assembly will contain a Providers capability. ProviderBindingReference records the exact owner-selected provider instance, account, environment and contract reference. ProviderOperationContext snapshots operation ID, correlation ID, requested time and mandatory mutation idempotency key from TechnicalCommandEnvelope and reuses IdempotencyScope. Inputs are opaque and preserved exactly; no trimming, generated keys, hashing, routing or policy interpretation changes their identity. This context records selection; it does not authorize a side effect.

ProviderIdempotencyBinding associates the operation context with an owner-supplied request fingerprint. ProviderIdempotencyGuard compares bindings previously persisted by the owner: different owner/provider/account/environment/key identities are separate; the same identity with a changed operation, correlation, contract or fingerprint is a conflict; an identical binding is equivalent. Requested time is observational and does not alter the identity. The owner must atomically reserve/check this identity in durable state before submission, include every effect-relevant request field in its fingerprint, and reconcile unknown external outcomes. The pure guard neither stores reservations nor executes, retries, authorizes, or proves an external effect. Existing InMemoryIdempotencyExecutor remains a process-local primitive and is not promoted into production provider coordination.

ProviderObjectReference carries exact binding context, typed object type/ID, optional event ID, observed time and copied safe evidence metadata references under Document 32 section 15. ProviderCorrelationEvidence attaches a copied reference collection to the original operation context and rejects mismatched provider/account/environment/contract bindings. Empty evidence is permitted for an unobserved outcome and carries no success/failure claim. Evidence and contexts are immutable snapshots. Timestamps are represented as UTC instants; IDs and fingerprints are compared ordinally. Owners minimize metadata and supply reference values, never secrets or raw provider payloads.

No provider SDK types or new public API/SDK contracts are introduced. Owners supply normalized business outcomes separately. This work does not implement payments, webhooks, provider retries/failover, routing policy, credentials, retention, finality or deployment. It introduces no new packages, persistence schema, coordinator service or lifecycle mechanism.

## Validation and handoff

Tests cover exact identity propagation, missing inputs, immutable snapshots, scoped idempotency comparisons and conflicting reuse, cross-binding evidence rejection, UTC timestamps, empty evidence and provider-type isolation. Existing command, OpenAPI, policy and storage architecture tests remain regression coverage. Build must pass with zero warnings/errors. All behavior and tests are committed before exact-SHA validation; mandatory role reviews, bounded context and closure evidence follow separately.

Use `tunner context build`, then `tunner context verify`. Work-item selection accepts `tunner context build --work-item TUN-P1-015`. After local closure run `tunner governance next`; protected-main integration does not pause independent eligible work.
