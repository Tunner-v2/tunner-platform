> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 15 — Runtime Communication & Infrastructure Technology Scope

## 1. Purpose

This document removes ambiguity about **which Tunner technology is used for which job**. Technology selection must never be inferred from convenience or from a framework being available.

The primary rule is:

> **Use the simplest transport or infrastructure component that preserves the required business semantics. Do not use a realtime or cache technology as durable authority, and do not expose internal infrastructure as a Connected Product contract.**

## 2. Technology classification

| Capability | Technology | Classification | Primary scope |
|---|---|---|---|
| Runtime | .NET 10 LTS / C# 14 | SELECTED | all server applications and tooling unless an ADR explicitly requires another runtime |
| Frontend runtime | React + TypeScript + Vite + TailAdmin React | SELECTED | Tunner User and Admin/Support web applications |
| Browser backend-for-frontend | separate ASP.NET Core User/Admin BFFs | SELECTED | secure browser session, UI composition, no JS token exposure |
| Identity/OAuth server | ASP.NET Core Identity + OpenIddict | SELECTED | Tunner account credential/session/OAuth/OIDC implementation |
| External identity | Google + Microsoft adapters | SELECTED | initial external sign-in providers |
| Source/CI | GitHub + GitHub Actions | SELECTED | source hosting, CI/CD, rulesets, attestations |
| Transactional email | Resend primary production adapter via official .NET SDK; Mailpit local/test; Mailgun alternate deferred | SELECTED | identity/security/transactional notification delivery |
| Payment provider | Stripe via stable official `Stripe.net` SDK behind Tunner provider contracts | SELECTED | initial payment execution/provider evidence; not domain authority |
| HTTP/API | ASP.NET Core 10 | SELECTED | public APIs, web backend endpoints, OAuth/OIDC endpoints, webhooks, health |
| Relational persistence | PostgreSQL 18 + EF Core/Npgsql | SELECTED | authoritative business state, transactions, outbox/inbox, durable timers, relational history |
| Internal asynchronous transport | RabbitMQ 4.3 current supported patch | SELECTED | post-commit asynchronous messaging and independent consumers |
| Distributed cache | Redis 8.x | SELECTED | cache, API protection counters, safe ephemeral coordination |
| SignalR scale-out | Redis backplane | SELECTED WHEN NEEDED | horizontally scaled Tunner SignalR hosts |
| Object storage contract | S3-compatible `IObjectStorage` | SELECTED | large/binary objects and immutable/versioned artifacts |
| Primary object store | Cloudflare R2 through S3-compatible API + official AWS SDK for .NET | SELECTED | large/binary objects; environment-isolated managed storage |
| Browser realtime | ASP.NET Core SignalR | SELECTED | live Tunner UI updates, progress, notifications, dashboards |
| Browser typed RPC | gRPC-Web | SELECTIVE | Tunner-owned web clients only when an ADR demonstrates value |
| Internal synchronous RPC | native gRPC | SELECTIVE | only justified cross-process/service boundaries |
| Connected Product synchronous API | HTTPS REST/JSON + OpenAPI | SELECTED | canonical Product/SDK commands and queries |
| Connected Product async delivery | signed HTTPS webhooks/events | SELECTED | Product-facing lifecycle/events/result delivery |
| Internal module calls | typed in-process application contracts | DEFAULT | modular-monolith communication without unnecessary network hops |
| Secrets | OpenBao abstraction | SELECTED | local/reference vault; production provider adapter later |
| Telemetry | OpenTelemetry | SELECTED | traces, metrics, logs |
| Local observability backend | Grafana OTEL-LGTM | SELECTED FOR DEV/TEST | local OpenTelemetry visualization/diagnostics only; not production authority |
| External broker alternatives | Kafka/NATS/cloud broker | DEFERRED_WITH_TRIGGER | require ADR based on measured workload; not parallel default transports |

## 3. Decision tree — which communication mechanism?

### 3.1 A Connected Product calls Tunner and needs an immediate authoritative response

Use **REST/JSON over HTTPS**, described by OpenAPI and normally consumed through a Tunner SDK.

Examples:

- register/inspect Product integration configuration;
- create an authorized Tunner operation;
- fetch current authoritative operation result;
- query Billing Subscription/commercial state exposed by the Product contract;
- manage webhook configuration;
- run readiness diagnostics.

Do not require the Product to use SignalR, gRPC-Web, Redis or RabbitMQ.

### 3.2 Tunner must notify a Connected Product after a committed business fact

Use a **signed/versioned HTTPS webhook/event contract**.

Internally the event may travel through RabbitMQ, but RabbitMQ is not exposed to the Product.

### 3.3 A Tunner browser UI performs an ordinary command or query

Default to **REST/JSON**.

Examples:

- submit registration;
- update profile through a governed flow;
- initiate an Admin action;
- load a detail page;
- review a case;
- submit a wizard step.

### 3.4 A Tunner browser UI needs live updates

Use **SignalR**.

Examples:

- long-running operation progress;
- live payment/reconciliation status change;
- operator alerts;
- notification arrival;
- support case updates;
- dashboard health changes;
- background export becoming ready.

SignalR carries realtime delivery, **not authority**. Every material UI state can be recovered by re-reading the authoritative API/read model after reconnect.

### 3.5 A Tunner-owned browser feature benefits materially from Protobuf/typed server streaming

Use **gRPC-Web selectively**, with an ADR for the feature or capability.

Good candidates can include high-frequency typed read streams or payload-sensitive internal UI features. It is not required for routine forms, CRUD-like commands, authentication flows or Product integration.

Browser constraints mean gRPC-Web is not the substitute for SignalR's general realtime experience.

### 3.6 Two modules are inside the same Tunner process

Use **in-process typed application/module contracts**.

Do not introduce HTTP/gRPC just to preserve a diagram boundary.

### 3.7 Two Tunner components are deliberately separated into processes/services and require synchronous typed RPC

Use **native gRPC** if synchronous coupling is justified.

Before creating the network boundary, the ADR must document why in-process/module communication is no longer sufficient: scaling, fault/security isolation, ownership/release cadence or workload shape.

### 3.8 A committed fact must trigger independent backend work

Use **PostgreSQL outbox → RabbitMQ → consumer inbox/handler**.

Examples:

- post-commit audit projection;
- notification intent processing;
- Product webhook delivery work;
- independent read model update;
- background integration processing;
- independent domain reaction allowed by the architecture.

Do not use RabbitMQ for a required foreground authority check that must succeed before the original operation can commit.

## 4. RabbitMQ scope

### Use RabbitMQ for

- post-commit domain/integration event transport;
- independent consumer fan-out;
- async work dispatch where ownership/semantics permit it;
- transport-level retry/dead-letter behavior;
- backpressure between independent components.

### Do not use RabbitMQ for

- source-of-truth business records;
- payment/provider finality;
- ledger state;
- canonical subscription state;
- long-lived business timer authority;
- user browser realtime;
- direct Connected Product integration.

### Required Tunner pattern

```text
owner-domain transaction
    ├─ authoritative data
    └─ outbox event
          │
        COMMIT
          │
     dispatcher
          │ publisher confirm
          ▼
       RabbitMQ
          │
   consumer delivery
          │
       inbox guard
          │
 owner-domain consumer action
```

Critical production workloads use quorum queues by default. Queue type/topology is workload-specific and must not create a queue per customer/work item without a documented scaling model.

## 5. Redis scope

### Use Redis for

- distributed cache;
- short-lived derived policy/configuration results;
- safe capability/authorization result cache where invalidation policy is defined;
- API rate/resource protection counters;
- safe ephemeral coordination where specifically designed;
- SignalR backplane on horizontally scaled self-hosted Tunner web nodes.

### Do not use Redis as authority for

- identity/account state;
- Product/Billing relationships;
- Billing Subscription lifecycle;
- commercial versions/history;
- payment/invoice/ledger/settlement;
- approval/audit evidence;
- durable Product event delivery.

A Redis outage must not destroy business truth. The expected failure mode is cache miss, temporary degradation or disabled realtime scale-out—not financial/identity corruption.

## 6. Object storage scope

### Use S3-compatible storage for

- support attachments;
- privacy exports;
- generated PDFs/documents when retained;
- evidence/release artifacts;
- large import/export files;
- archived binary artifacts;
- other content unsuitable for relational-row storage.

### Keep in PostgreSQL

- object ownership and domain linkage;
- object key/version reference;
- content hash/checksum;
- size/media type;
- security classification;
- lifecycle state;
- retention/hold policy references;
- creator/correlation/audit references.

### Reference implementation

Cloudflare R2 is the selected primary object store. The adapter uses the official AWS SDK for .NET against R2's S3-compatible endpoint.

Do not infer complete AWS S3 parity. The implementation is constrained to R2-supported API features. Tunner computes/stores its own SHA-256 and authoritative object metadata in PostgreSQL. R2 lifecycle, bucket-lock and jurisdiction controls are infrastructure policy. Location Hints are not residency guarantees.

Local unit/component tests may use a deterministic fake. Real storage integration/contract tests run against dedicated non-production R2 buckets; no local object-store emulator is authoritative for production behavior.

Uploads are untrusted until validation/scanning rules are satisfied. Presigned URLs must be short-lived and scoped.

## 7. SignalR scope

SignalR is a **presentation/realtime delivery channel** for Tunner-controlled clients.

Use groups/topics keyed by safe application identifiers—not secret data. Authentication/authorization is re-evaluated according to the applicable hub/operation policy.

The UI must handle:

- reconnect;
- duplicate messages;
- reordered/stale hints;
- missed updates;
- resync from API/read model.

Never grant entitlement, mark payment final, or mutate ledger state solely because a SignalR message was received.

## 8. gRPC-Web scope

gRPC-Web is **not the standard Tunner browser transport**. It is an optimization/tool for selected Tunner-owned web capabilities.

A gRPC-Web adoption ADR must state:

- why REST is insufficient for the use case;
- unary vs server-streaming requirement;
- browser/proxy compatibility;
- authentication/authorization model;
- observability/error mapping;
- generated client lifecycle;
- fallback/degradation behavior.

Do not expose gRPC-Web as a mandatory Connected Product integration requirement.

## 9. Native gRPC scope

Native gRPC is reserved for synchronous internal calls across a real process boundary. It is not used inside the same process merely to imitate microservices.

Public Connected Product contracts remain REST/webhook based unless a future approved Product Decision deliberately adds another public protocol.

## 10. REST/API scope

REST/JSON + OpenAPI is the broadest compatibility surface and therefore remains Tunner's canonical synchronous external contract.

Use stable resource/operation identifiers, idempotency where required, explicit API/contract versions, RFC-aligned OAuth security, consistent problem/error contracts and environment isolation.

The .NET and TypeScript SDKs make this easier to consume but never become the server authority.

## 11. PostgreSQL vs RabbitMQ vs Redis vs object storage

| Data/behavior | PostgreSQL | RabbitMQ | Redis | Object storage |
|---|---:|---:|---:|---:|
| authoritative Account state | YES | NO | cache only | NO |
| payment/ledger truth | YES | NO | NO | evidence/document only |
| atomic outbox/inbox | YES | transports after commit | NO | NO |
| long-lived business timer | YES | optional wake-up transport | NO | NO |
| async event transport | event record/source | YES | NO | NO |
| distributed cache | source behind cache | NO | YES | NO |
| SignalR scale-out | NO | NO | YES | NO |
| large attachments/exports | metadata only | NO | NO | YES |
| release/test evidence metadata | YES/manifest | NO | NO | binary artifacts YES |

## 12. Browser communication matrix

| Need | Default | Why |
|---|---|---|
| form/wizard command | REST/JSON | simple, debuggable, canonical |
| page/read model query | REST/JSON | cacheable/observable/SDK-friendly |
| live status update | SignalR | bidirectional realtime connection handling |
| operation progress | SignalR + API resync | realtime hint + authoritative recovery |
| typed/high-frequency internal browser RPC | gRPC-Web when ADR-approved | binary/typed/server-streaming advantage |
| durable business event | never browser transport | backend outbox/RabbitMQ + persisted state |

## 13. Connected Product communication matrix

| Need | Protocol |
|---|---|
| synchronous command/query | HTTPS REST/JSON + SDK |
| OAuth/OIDC flows | standards-based HTTPS endpoints |
| lifecycle/result notification | signed HTTPS webhook/event |
| replay/redrive | Tunner delivery API/operations; same event lineage |
| integration diagnostics | REST/JSON + SDK/portal |
| realtime Product UI | Product-owned; Tunner does not require SignalR |

## 14. Local Docker reference topology

```text
User/Admin Browser
       │
       ├── HTTPS REST/JSON
       ├── SignalR
       └── selective gRPC-Web
              │
          Tunner.Api
              │
      ┌───────┼────────┐
      │       │        │
 PostgreSQL  Redis   Object Storage
      │              (Cloudflare R2/S3-compatible)
    Outbox
      │
 Tunner.Worker
      │
   RabbitMQ
      │
 consumers / webhook delivery / background reactions

OpenBao supplies secrets.
OpenTelemetry instruments API/Worker and dependencies.
```

## 15. Failure semantics

- PostgreSQL unavailable: authoritative writes fail safely; no fake success.
- RabbitMQ unavailable after domain commit: outbox remains pending; dispatcher retries; committed business state is preserved.
- Redis unavailable: cache bypass/degraded mode; no business truth lost.
- Object storage unavailable: file-dependent operation remains pending/failed according to owner-domain contract; relational metadata cannot claim an object is usable when it is not.
- SignalR unavailable: UI falls back to refresh/poll/reconnect as designed; authoritative operations remain available through API unless the feature explicitly requires live transport.
- gRPC-Web unavailable: only the ADR-approved capability is affected; ordinary canonical API remains independent.

## 16. Required implementation ADRs

Before P0/P1 coding, approve at least:

- `ADR — RabbitMQ internal messaging topology`;
- `ADR — Redis caching and SignalR backplane`;
- `ADR-0012 — Cloudflare R2 primary object storage behind S3-compatible IObjectStorage`;
- `ADR — REST, SignalR, gRPC-Web and native gRPC protocol boundaries`;
- `ADR — local observability reference stack (Grafana OTEL-LGTM dev/test; OpenTelemetry contract)`;
- frontend runtime and API-edge/BFF classification.

## 17. Acceptance tests

| ID | Given | When | Then |
|---|---|---|---|
| TECH-001 | Product integration | normal synchronous call | REST/OpenAPI contract is sufficient; RabbitMQ/SignalR/gRPC-Web are not required by Product |
| TECH-002 | committed outbox + RabbitMQ outage | dispatcher runs | message remains pending and later publishes without repeating owner business mutation |
| TECH-003 | Redis flushed | request executes | authoritative state remains correct; cache repopulates safely |
| TECH-004 | SignalR disconnect | browser reconnects | client resyncs from authoritative API/read model |
| TECH-005 | gRPC-Web endpoint disabled | ordinary Tunner web command | REST path continues unaffected unless ADR explicitly scoped feature otherwise |
| TECH-006 | object uploaded with invalid checksum | validation runs | object is not marked usable and evidence records rejection/quarantine |
| TECH-007 | internal modules in same process | developer proposes gRPC | architecture test/review rejects unnecessary network boundary absent ADR |

## 18. Development rule

If a developer or AI agent cannot determine the transport from this document, the item is blocked as `CONTRACT_REQUIRED` or `TECH_DECISION_REQUIRED`. It must not choose a protocol by preference.

## 2A. Browser security boundary

The User UI and Admin/Support UI each terminate at their own ASP.NET Core BFF. Browser JavaScript receives a secure application session cookie rather than OAuth access/refresh tokens. The BFF owns token handling/server-side OAuth interactions and calls Tunner application/API contracts using the authenticated context. SignalR connections follow the same authenticated browser boundary.

## 2B. Identity and external login

ASP.NET Core Identity + OpenIddict is the selected identity/OAuth implementation foundation. Google and Microsoft are the first external-login adapters. External provider identity is normalized into Tunner-owned Account identity/linkage records and may not bypass Tunner verification, policy, recovery, session, step-up or audit requirements.

## 2C. Transactional email

Resend is the primary production transactional-email adapter through its official .NET SDK. Local/test uses Mailpit. Tunner supplies a stable delivery idempotency key, verifies Resend webhook signatures, and normalizes delivery/failure/complaint/unsubscribe evidence into Tunner Notification state. Resend's provider-side idempotency window does not replace Tunner's durable outbox/delivery duplicate prevention. Mailgun remains an approved alternate but is deferred. Marketing communication remains governed by separate consent/preference policy.

## 2D. Initial payment provider

Stripe is the selected initial payment provider. Use a stable GA release of the official `Stripe.net` SDK behind Tunner payment-provider contracts.

- Stripe SDK/API types do not cross into Tunner domain entities or public contracts.
- Mutating provider calls use stable Tunner idempotency/correlation identity.
- Stripe webhook signatures are verified and provider event IDs are deduplicated through inbox/evidence state.
- An unknown timeout outcome is reconciled before a new external side effect is authorized.
- Preview/beta Stripe SDK/API features require a separate ADR.
