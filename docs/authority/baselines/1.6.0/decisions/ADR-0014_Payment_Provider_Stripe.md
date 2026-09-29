# ADR-0014 — Initial Payment Provider: Stripe

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision

Stripe is Tunner's initial payment-provider implementation. Use the stable official **Stripe.net** SDK behind provider-neutral Tunner payment contracts. Preview/beta Stripe SDK/API features require a separate ADR.

## Boundaries

- Tunner remains authoritative for its billing/commercial/payment orchestration state.
- Stripe objects, IDs, dashboard state and webhook payloads are provider evidence and references, not Tunner domain entities.
- No Stripe SDK type appears in public Tunner API/SDK contracts or persisted domain models.
- Product entitlement remains Connected-Product authority.

## Side-effect safety

- externally mutating provider requests use stable Tunner idempotency/correlation identity;
- webhook signatures are verified;
- provider event IDs are deduplicated through inbox/evidence state;
- a timeout or transport error is not interpreted as payment failure;
- unknown external outcomes are reconciled before another business side effect is authorized.

## Revisit trigger

Regional/product/payment-method requirements, provider reliability/commercial risk, or a measured need for another payment provider.
