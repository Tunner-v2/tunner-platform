# ADR-0013 — Transactional Email: Resend Primary + Mailpit Local/Test

**Status:** ACCEPTED  
**Date:** 2026-09-21  
**Supersedes:** ADR-0010

## Decision

Use Tunner `IEmailProvider`/Notification contracts. **Resend** is the primary production transactional-email provider through Resend's official .NET SDK. **Mailpit** is the local/test sink. **Mailgun** is an approved alternate provider, but its implementation is deferred until a governed resiliency/commercial/regional/provider-risk trigger exists.

## Delivery safety

- Tunner owns stable Notification/EmailDelivery identities and durable outbox/delivery state.
- Supply a stable provider idempotency key for a logical Resend send.
- Resend's provider-side idempotency window is an additional safety layer, not Tunner's durable duplicate-prevention authority.
- Unknown provider outcomes are reconciled; they do not trigger blind alternate-provider sends.
- Verify Resend webhook signatures and deduplicate provider events before updating delivery evidence.

## Boundaries

Provider delivery, failure, complaint and unsubscribe signals are normalized into Tunner Notification state. Provider state does not directly mutate unrelated business domains. Marketing consent/preference remains Tunner-authoritative and separate from transactional/security classification.

## Secrets/environments

Resend API keys and webhook signing secrets are vault-managed and environment-scoped. Production credentials are never used by local/test.

## Revisit trigger

Provider performance, deliverability, regional, compliance, commercial or resilience requirements justify implementing the already-approved Mailgun alternate or another adapter.
