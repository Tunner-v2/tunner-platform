> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 01 — Engineering & Technology Foundation

## 1. Architecture posture

Tunner starts as a **cohesive modular .NET platform**, not a microservice estate.

The architecture preserves bounded domain ownership so modules can be extracted later without rewriting the business model. Initial runtime boundaries are deliberately small.

### Initial deployable units

1. `Tunner.Api` — synchronous HTTP/OAuth/application entry point.
2. `Tunner.Worker` — outbox dispatch, webhook/event delivery, durable work, timers, reconciliation and background processing.
3. User web application.
4. Admin/Support web application.
5. PostgreSQL.
6. OpenBao for local/reference secrets.
7. RabbitMQ for internal asynchronous transport.
8. Redis for distributed cache and SignalR scale-out/backplane when required.
9. Cloudflare R2 as primary object storage behind the S3-compatible `IObjectStorage` contract; use the official AWS SDK for .NET S3 client against R2.
10. OpenBao for secrets.
11. OpenTelemetry Collector + local observability backend.

A domain module is **not** automatically a deployable service.

## 2. Runtime baseline

- .NET 10 LTS.
- C# 14.
- ASP.NET Core 10.
- EF Core 10.
- PostgreSQL 18.
- Npgsql EF provider 10.x-compatible.
- Nullable reference types enabled.
- Treat warnings as errors for Tunner-owned projects, with explicitly documented exceptions.
- Central package management.
- Deterministic builds where practical.

### Why

.NET 10 is supported until November 2028 and aligns with EF Core 10 LTS. PostgreSQL 18 is supported through November 2030.

## 2A. Web application and BFF baseline

- User UI: React + TypeScript + Vite + TailAdmin React.
- Admin/Support UI: React + TypeScript + Vite + TailAdmin React.
- Each UI has a separate ASP.NET Core BFF boundary.
- Browser authentication uses secure HttpOnly/SameSite cookies at the BFF; OAuth/OIDC access and refresh tokens are not persisted in browser JavaScript storage.
- BFFs expose only UI-appropriate endpoints/projections and invoke Tunner application/domain contracts; they do not become business authority.
- YARP may be used inside a BFF where proxy/routing adds concrete value, but a generic proxy layer is not mandatory.

## 2B. Identity implementation baseline

- ASP.NET Core Identity owns Tunner account credential/user-security primitives.
- OpenIddict is the selected OAuth 2.x / OpenID Connect authorization-server/client stack.
- Tunner-owned identity policy and domain state remain authoritative; framework tables/types are implementation mechanisms, not the business model.
- First external identity providers: Google and Microsoft, implemented through provider adapters and normalized Tunner account-linking rules.
- MVP identity methods include password, verified email, passkeys/WebAuthn, TOTP authenticator-app MFA, recovery codes, session/device management and step-up authentication.

## 3. Persistence model

### Primary database

PostgreSQL is the authoritative relational persistence baseline because Tunner requires:

- transactional consistency;
- strong constraints;
- financial/accounting suitability;
- JSON support where genuinely appropriate;
- broad cloud-managed availability;
- vendor-neutral self-hosting;
- robust concurrency and indexing.

### Module ownership

A common physical PostgreSQL cluster/database is acceptable initially.

Each table has exactly one logical owner module. Other modules:

- call the owner contract;
- consume events;
- use explicitly published read models;
- never mutate foreign-domain tables directly.

### Migration rule

- forward-only migrations by default;
- migration files are immutable after release;
- production rollback is usually application rollback + forward-fix, not destructive schema downgrade;
- migration rehearsals run against realistic prior schemas.

## 4. Durable execution and RabbitMQ transport

The runtime baseline uses **PostgreSQL transactional outbox/inbox + RabbitMQ 4.3 current supported patch**. RabbitMQ is an internal transport; it does not become business authority.

Canonical path:

`domain transaction → authoritative rows + atomic outbox → COMMIT → dispatcher → RabbitMQ → consumer → consumer inbox/idempotency → owner-domain handler`

Rules:

- PostgreSQL commit is the publisher-domain finality boundary.
- Publisher confirms are required when dispatching outbox messages to RabbitMQ.
- Critical long-lived production queues use quorum queues unless an ADR documents another queue type for a specific workload.
- Subscriber failure cannot roll back the publisher.
- At-least-once delivery is assumed; consumers are idempotent.
- Redrive/replay preserves event identity and does not recreate the originating business side effect.
- RabbitMQ is **not** used as the canonical store for long-lived business timers. Due dates, workflow/timer state and retry eligibility that must survive transport replacement remain durable in PostgreSQL.
- RabbitMQ delayed retry may be used for bounded transport-level retry where the owning-domain semantics permit it.
- Connected Products never connect directly to Tunner RabbitMQ. Connected Product integration uses Tunner APIs/webhooks/events contracts.

## 5. Observability

Use OpenTelemetry as the instrumentation standard for:

- traces;
- metrics;
- logs/correlation.

Every external request and durable operation must be traceable by stable correlation/causation identifiers.

Minimum standard attributes:

- `service.name`
- environment
- release/version
- trace ID
- request/operation ID
- causation ID where applicable
- owning module
- Product/environment ID when safe and non-sensitive

Never place secrets, raw payment data or unrestricted PII in telemetry.

## 5A. Local observability reference

Development/test uses Grafana `otel-lgtm` as the local reference backend: OpenTelemetry Collector + Grafana + Loki + metrics backend + Tempo in a Docker-friendly distribution. It is not the production observability authority. Application code exports OpenTelemetry/OTLP and must remain backend-neutral.

## 6. Resilient HTTP

Use typed/named `HttpClient` integration with standard resilience policy.

Retry policy is technical only where the domain says retry is semantically safe.

A generic HTTP retry must never duplicate:

- payment submission;
- refund;
- payout/transfer;
- identity mutation;
- other non-idempotent external side effect.

## 7. Docker baseline

Docker is mandatory for local infrastructure and release-image reproducibility.

Image rules:

- multi-stage builds;
- minimal runtime image;
- non-root runtime user where supported;
- no SDK/compiler in production image;
- no secrets in image layers;
- no secret build arguments;
- health/readiness behavior;
- pinned base-image strategy with governed patching;
- image vulnerability scanning;
- immutable digest recorded at release.

## 8. .NET Aspire usage

Aspire Service Defaults may be used to standardize OpenTelemetry, health checks and resilient HTTP helpers.

Docker Compose remains the required local environment contract.

Do not make Aspire orchestration a production dependency or business authority.

## 9. Shared platform primitives

Implement once:

- authorization primitives;
- idempotency;
- expected-version concurrency;
- outbox/inbox;
- durable work leasing;
- retry scheduling;
- audit append;
- approval/SoD integration;
- policy/effective-version resolution;
- correlation/causation;
- provider adapter contracts;
- problem/error contracts;
- observability.

Domain modules supply semantics; infrastructure supplies mechanics.

## 10. Prohibited foundation patterns

- service-per-flowchart;
- repository abstraction wrapping EF Core with no domain value;
- generic workflow engine before concrete repeated workflows;
- separate database per box;
- shared mutable “common domain model” across modules;
- global utility project containing business logic;
- hidden network calls inside entity/domain model methods;
- application service directly editing another domain’s tables.

## 11. Example — payment timeout

The HTTP layer receives a provider timeout after a payment submission.

**Wrong:** standard retry middleware immediately retries.

**Correct:** Finance/Payment marks the attempt `UNKNOWN_EXTERNAL_STATE`, schedules reconciliation, and prevents another provider attempt until finality is established.

## 12. Foundation acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| ENG-001 | API + Worker + PostgreSQL + OpenBao started by standard dev script | healthy without manual configuration steps |
| ENG-002 | domain commit and outbox insert | both commit atomically |
| ENG-003 | Worker crashes after claiming durable work | work becomes reclaimable without duplicate business mutation |
| ENG-004 | one subscriber fails | publisher and unrelated subscribers remain committed |
| ENG-005 | secret requested | resolved from vault; no secret value exists in repository/app config/database |
| ENG-006 | service telemetry exported | request → domain operation → background continuation traceable by correlation |

## 13. Distributed cache — Redis

Redis 8.x is the selected distributed-cache reference implementation, accessed through `IDistributedCache` / StackExchange.Redis and Tunner-owned cache abstractions where richer semantics are required.

Allowed uses include:

- derived configuration/policy cache;
- integration metadata/cache;
- safe authorization/capability calculation cache;
- API protection/rate counters;
- short-lived distributed coordination when explicitly designed;
- ASP.NET Core SignalR backplane for horizontally scaled Tunner web hosts.

Redis is never authoritative for identity, billing authority, subscriptions, payments, ledger, invoices, approvals, audit or commercial history. Redis loss must cause cache miss/degraded performance, not loss of business truth.

Redis licensing must remain in the dependency/license register and be rechecked before release.

## 14. Object storage

Use an S3-compatible `IObjectStorage` contract. **Cloudflare R2** is the selected primary object store. The infrastructure adapter uses the official AWS SDK for .NET S3 client configured for the R2 endpoint. Domain modules depend only on `IObjectStorage`, so a future storage-provider change does not alter business contracts.

R2's S3 API is a compatibility surface, not an assertion of complete AWS S3 parity. Implementation must use only capabilities verified by the current R2 compatibility matrix. Tunner stores its own SHA-256 content hash and lifecycle/retention metadata in PostgreSQL rather than treating ETags or provider metadata as business authority. R2 bucket lifecycle/lock and jurisdiction controls are infrastructure policy, not domain truth.

Use object storage for large/binary artifacts such as:

- privacy exports;
- support attachments;
- generated documents/evidence packs;
- archived files;
- import/export payloads;
- other large immutable or versioned artifacts.

PostgreSQL stores authoritative metadata, ownership, purpose, checksum, object key/version, retention/hold references and lifecycle state.

Required storage capabilities when applicable include checksum validation, object versioning, presigned access, retention/legal hold, encryption and lifecycle policy. Uploads must be content-type/size validated and pass malware/content scanning before becoming usable where the threat model requires it.

## 14A. Selected provider SDKs

Use stable, officially maintained packages where available:

- `Stripe.net` for the initial Stripe payment adapter;
- `Resend` for the primary transactional-email adapter;
- official AWS SDK for .NET S3 client for Cloudflare R2;
- provider-neutral Tunner interfaces remain the application boundary.

Preview/beta provider SDKs are not allowed in the active baseline without a specific ADR. Provider SDK types may not leak into domain entities, public Tunner contracts or persisted business state.

## 15. Communication technology rule

- **REST/JSON + OpenAPI:** default public Connected Product API and default Tunner web command/query transport.
- **SignalR:** Tunner-owned browser realtime updates, progress, notifications and live operational views. Realtime messages are hints/updates; clients resync authoritative state through API/read models after reconnect.
- **gRPC-Web:** selective Tunner-owned browser typed RPC/server-streaming where it provides measurable value. It is not the default CRUD transport and not used for durable realtime semantics.
- **native gRPC:** only across justified internal process/service boundaries; in-process module calls remain the default inside the modular platform.
- **RabbitMQ:** durable internal asynchronous transport after owner-domain commit.
- **Redis:** cache/ephemeral coordination/SignalR backplane, not durable business messaging.
- **Webhooks:** external Connected Product asynchronous delivery.

The authoritative detailed scope is `15_Runtime_Communication_and_Infrastructure_Technology_Scope.md`.

## 16. Official-first dependency policy

Tunner optimizes delivery speed by reusing mature infrastructure libraries while retaining Tunner business authority in Tunner code.

Selection precedence:

1. .NET / ASP.NET Core built-in capability;
2. official Microsoft/vendor/foundation-maintained package;
3. mature community package only when the first two do not satisfy the requirement and maintenance/license/security review passes;
4. custom infrastructure implementation only when no acceptable maintained package exists or Tunner-specific behavior is itself the product/domain requirement.

Stable GA/supported releases are required. Preview/beta dependencies require a specific ADR and expiry/review trigger.

Selected/approved package families for implementation include:

- ASP.NET Core / ASP.NET Core Identity / SignalR / rate limiting / ProblemDetails / OpenAPI tooling;
- EF Core 10 + Npgsql PostgreSQL provider;
- OpenIddict;
- `RabbitMQ.Client` official client;
- `Microsoft.Extensions.Caching.StackExchangeRedis` / `StackExchange.Redis`;
- `Microsoft.Extensions.Http.Resilience`;
- OpenTelemetry .NET packages;
- YARP only where a BFF/proxy requirement exists;
- official gRPC packages only where the approved protocol boundary uses gRPC;
- official `Stripe.net`;
- official `Resend` .NET SDK;
- AWS SDK for .NET S3 client against Cloudflare R2;
- Testcontainers for .NET for containerized integration tests;
- Microsoft Playwright for browser/end-to-end tests;
- Microsoft Kiota for generated transport clients from OpenAPI where it reduces manual SDK duplication;
- React/TypeScript/Vite/TailAdmin, React Router, TanStack Query, React Hook Form and Zod for the approved frontend architecture.

Dependency versions are centralized (`Directory.Packages.props` for .NET and committed package-manager lockfiles for web apps). Dependabot/security tooling proposes updates through pull requests; major/runtime-sensitive updates are never silently auto-merged.

Do not introduce MediatR, AutoMapper, MassTransit, Hangfire, Quartz, Temporal, Elasticsearch/OpenSearch, Redux or a generic workflow framework by default. Any such dependency needs a concrete capability gap and an ADR showing why existing platform primitives are insufficient.


## Global presentation-content foundation

Tunner implements governed presentation content as a cohesive cross-cutting module inside the modular platform, not as a separate microservice by default and not as an external CMS authority. PostgreSQL is authoritative for definitions, variants, immutable versions/publications and proposals; Redis may cache resolved published content but is non-authoritative. ASP.NET Core localization/culture primitives may support locale mechanics, while Tunner owns semantic content keys, context resolution, authorization, publication/version/audit policy and domain-boundary enforcement.

React applications consume content through reusable component/resolver contracts. The initial baseline does not require a general-purpose CMS framework. A third-party/headless CMS may be evaluated later behind an adapter only if it does not become authority for Tunner business semantics, identity/authorization, audit/version governance or production publication policy. Model providers are likewise behind a provider-neutral content-assistance interface and may create proposals only.
