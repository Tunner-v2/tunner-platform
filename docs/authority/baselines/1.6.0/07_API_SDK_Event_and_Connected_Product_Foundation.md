> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 07 — API, SDK, Event & Connected Product Foundation

## 1. Objective

Deliver a strong MVP integration foundation early, without implementing every deep Connected Product operation before Tunner core business domains are ready.

## 2. Contract-first rule

Canonical machine-readable contracts are authored/validated before SDK behavior is considered complete.

- HTTP API: OpenAPI.
- Event-driven integration: AsyncAPI where practical.
- Payload schemas: JSON Schema.
- Event envelope: CloudEvents-compatible conventions where useful.
- OAuth/OIDC security: current IETF standards profile.

Contract and SDK code must be generated/verified from the same source family where possible.

## 3. Current standard profile

Baseline research profile:

- **OpenAPI 3.1 is the normative executable HTTP contract for the .NET 10 baseline.** OpenAPI 3.2 is a forward compatibility target and may become normative after a governed runtime/tooling upgrade.
- AsyncAPI 3.1.0.
- CloudEvents 1.0-compatible.
- OAuth 2.0 Security BCP — RFC 9700.
- Browser apps — RFC 10017.
- Native apps — RFC 8252 when applicable.
- Authorization Server Metadata — RFC 8414.
- Protected Resource Metadata — RFC 9728.
- PKCE.
- DPoP RFC 9449 selectable by security policy.
- WebAuthn Level 3 for passkey-capable user authentication.

Tooling compatibility must be verified before locking a contract format; a standards version is not useful if the selected generator cannot safely process it. If a compatibility export is required, the canonical source and generated compatibility view must be explicit.

## 4. MVP SDK scope

### .NET reference SDK

First-class hand-written/generated hybrid where necessary.

Responsibilities:

- authentication/client setup;
- environment selection;
- contract-version negotiation;
- request correlation/idempotency helpers;
- typed error/problem handling;
- webhook signature verification helpers;
- event parsing/version dispatch;
- diagnostic metadata;
- SDK version reporting.

### TypeScript SDK

Developed next/in parallel once canonical APIs stabilize enough for strong MVP.

### Future

Python/Java/other SDKs based on Product demand.

Do not hand-maintain multiple divergent contract models.

## 5. SDK must not own business authority

The SDK:

- validates developer inputs;
- prevents obvious incompatible calls;
- manages protocol details;
- reports version.

The server always re-validates:

- Product/client;
- environment;
- capability;
- authority;
- business state;
- policy;
- idempotency.

## 6. Connected Product foundation MVP

Implement early:

- Product registry;
- Product environments;
- OAuth/client registration;
- redirect allow-list;
- SDK/API compatibility state;
- capability assignment;
- webhook endpoint registration;
- signing configuration;
- subscription to supported Tunner events;
- delivery identity/attempt evidence;
- integration diagnostics/readiness;
- API protection policy foundation.

Deep product-specific workflows expand as Tunner business domains become functional.

## 7. Connected Product commercial/payment capability

Each payment-enabled Product/environment references a versioned `ProductCommercialAgreement`, Product payment/compliance eligibility and Tunner routing-policy contracts. The Product does not own or receive a persistent assignment to a provider merchant/account.

`ProductKycCase` is the Tunner-owned Product compliance/KYC record where required. Provider verification of Tunner's own merchant/account relationship is separate provider infrastructure evidence.

Connected Products integrate only with Tunner. They do not select a gateway, hold provider credentials, call payment providers directly or depend on provider-native account/payment IDs. Tunner may route one Product through multiple provider accounts and reuse provider accounts across many Products without changing ProductPayable/Settlement ownership or the Product-facing SDK/API contract.

The integration foundation stores and validates Tunner contract references and capability assignments. Commercial, fee, tax, Product compliance, payment, payable, settlement and payout behavior is defined in the owning FRDs.

## 8. Event semantics

Publisher commits authoritative fact first.

Independent reactions:

`outbox → subscription registry → per-subscription delivery/inbox`

Required:

- stable event ID;
- stable delivery ID;
- schema version;
- at-least-once delivery assumption;
- consumer idempotency;
- retries scoped to delivery, not originating business operation;
- redrive preserves event identity/lineage;
- Product Application Result is distinct from HTTP delivery acknowledgment.

## 9. Webhook signing

- per environment;
- rotatable;
- current + bounded overlap during rotation;
- timestamp/replay controls;
- canonical signing input specified;
- signature version included;
- verification SDK helper available.

No signing secret is exposed after creation except through approved secure rotation flow.

## 10. Integration diagnostic example

Product developer asks “why is webhook not arriving?”

Tunner diagnostic view may show:

- Product/env;
- endpoint configured;
- TLS/reachability status;
- event subscription active;
- latest event ID;
- delivery attempts;
- HTTP acknowledgment category;
- signing config version;
- retry/DLQ state;
- correlation IDs.

It must not expose secret values or internal topology.

## 11. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| SDK-001 | .NET SDK call | SDK/user-agent version metadata reaches Tunner |
| SDK-002 | mismatched environment | server rejects even if SDK locally accepts config |
| SDK-003 | duplicate idempotent request | same logical operation/result returned |
| EVT-001 | webhook 500 | only delivery retries; originating business operation not re-run |
| EVT-002 | webhook redrive | same event lineage retained |
| INT-001 | invalid redirect URI | exact allow-list validation blocks registration/auth flow |
| INT-002 | secret rotation | old/new signatures accepted only during governed overlap |

## 12. Protocol and transport scope

### Public Connected Product API — REST/JSON

HTTPS REST/JSON described by OpenAPI is the canonical public synchronous integration contract. Connected Products and SDKs use it for commands, queries, environment/configuration operations and authoritative result retrieval. A Product is not required to run gRPC, gRPC-Web, SignalR, Redis or RabbitMQ.

### External asynchronous Product delivery — HTTPS webhooks/events

Tunner delivers Product-facing asynchronous results through signed/versioned HTTPS webhook/event contracts. AsyncAPI/JSON Schema describe those contracts where applicable. RabbitMQ is an internal transport only and is never exposed as the Product integration contract.

### Tunner-owned web clients — REST + SignalR + selective gRPC-Web

- REST/JSON remains the default command/query transport.
- SignalR carries live UI updates such as operation progress, notification arrival, dashboard status changes and operator-visible state-change hints. SignalR messages are not the source of truth; reconnecting clients resync through APIs/read models.
- gRPC-Web may be used for strongly typed/high-frequency unary calls or server streaming in Tunner-owned browser clients when an ADR demonstrates clear value. Because browser gRPC has protocol limitations, gRPC-Web is not the general realtime channel and is not the external Connected Product contract.

### Internal module/service communication

Inside the initial modular process, direct typed in-process application/module contracts are preferred. Native gRPC is used only when a module is deliberately deployed as a separate process/service and synchronous typed RPC is justified.

### Internal asynchronous messaging — RabbitMQ

Owner-domain commit and PostgreSQL outbox precede RabbitMQ publication. RabbitMQ transports post-commit events/work to independent consumers. Consumers use inbox/idempotency. RabbitMQ does not replace business timers, payment finality, subscription state or audit truth.

### Redis

Redis supports distributed cache, rate/protection counters, safe ephemeral coordination and SignalR scale-out. It is not used as durable business-event transport.

## Browser/BFF contract boundary

Connected Product public APIs and browser BFF APIs are different contract surfaces.

- Connected Product API: stable REST/JSON + OpenAPI contract, SDK-facing, externally versioned.
- User/Admin BFF API: browser-specific, session-cookie protected, may evolve with its corresponding Tunner UI but must not expose OAuth refresh tokens to JavaScript.
- SignalR: realtime hint/progress channel; clients resynchronize authoritative state through BFF/API reads.
- gRPC-Web: selective Tunner-owned browser capability only; never a mandatory Connected Product protocol.

## External login providers

Google and Microsoft are the first social/enterprise external-login adapters. External provider subject/claims are normalized into Tunner-owned account-link records; provider identity never becomes a substitute for Tunner account authority, audit history, lifecycle or recovery policy.

## Initial payment provider

Stripe is the initial payment-provider implementation. Tunner integrates through provider-neutral payment contracts and the stable official `Stripe.net` SDK. Stripe IDs, API objects and webhook payloads are provider evidence, not Tunner domain authority.

Payment-side-effect rules remain mandatory:

- use stable Tunner idempotency keys for externally mutating calls;
- verify Stripe webhook signatures and ingest events idempotently;
- a network timeout/unknown provider outcome must be reconciled before a new business side effect is attempted;
- no Product or Tunner domain may depend directly on Stripe SDK types.

## Global market and currency contract

Public Product checkout/payment intents must carry enough context for Tunner to resolve market eligibility without assuming any fixed customer geography. At minimum the commercial/payment contract must support authoritative or provider-derived references for:

- customer/billing country (subject to privacy minimization);
- Product/environment;
- Product payment/compliance eligibility context (`ProductKycCase` where required);
- commercial price/currency version;
- explicit `presentment_currency` from Product commercial configuration;
- provider account/region;
- market-eligibility policy version;
- tax-obligation/calculation reference when applicable.

An international card may be accepted while the Tunner Product subscription price remains USD. SDKs must never infer acceptance from card country alone; the API returns the authoritative eligibility/payment result.
