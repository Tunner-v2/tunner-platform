> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 11 — Development Planes, Milestones & P0 Backlog

## 1. Planes

### P0 — Governance & Development System

Purpose: create the system that governs development.

### P1 — Platform Foundation

Persistence, execution, eventing, observability, config, security primitives, API foundation.

### P2 — Account & Identity

Fully functional Tunner Account/Identity including registration, verified contacts, authentication, MFA/passkeys, sessions and recovery.

### P3 — Connected Product & SDK Foundation

Product registry, environments, clients, OAuth integration, capabilities, SDKs, events/webhooks, diagnostics.

### P4 — Shared Platform Services

Providers, Notifications, Support, Audit/Governance foundations.

### P5 — Commercial & Billing Subscription Core

Billing Account, commercial projection/versioning, Tunner Billing Subscription.

### P6 — Operational Maturity

Security Ops, Privacy, Risk, Internal Access, Platform Reliability and automation maturation.

### P7 — Finance & Accounting

Full Payments, Invoices/Credits, Ledger, Reconciliation, Settlement, Finance controls.

### P8 — Production Readiness

Compliance validation, performance, DR, release hardening, external assessment/readiness.

SDK is a parallel workstream across P1-P7.

## 2. P0 completion goal

At P0 completion, a new human or AI contributor can:

1. clone repo;
2. run one doctor/setup path;
3. start dependencies;
4. run validation/tests;
5. understand current authority and work state;
6. create governed work;
7. produce evidence;
8. hand off without chat history.

## 3. P0 backlog

### TUN-P0-001 — Repository bootstrap

Deliver:

- root structure;
- Git ignore/editor config;
- .NET SDK pin;
- central package management;
- build props;
- README;
- docs/governance/tools separation.

Acceptance:

- clean clone has deterministic structure;
- production solution does not reference governance tooling.

### TUN-P0-002 — Git & contribution policy

Deliver:

- branch policy;
- commit convention;
- PR template;
- CODEOWNERS strategy;
- release tagging;
- amendment/ADR linkage.

Acceptance:

- sample invalid PR fails governance check.

### TUN-P0-003 — Governance schemas

Deliver schema-validated:

- milestone;
- sprint;
- work item;
- defect;
- reopen;
- decision;
- amendment;
- gate;
- release/evidence manifest.

### TUN-P0-004 — `tunner-governance` MVP

Commands:

- validate;
- status;
- next;
- gate check;
- block/unblock;
- defect/reopen;
- release check.

No web UI required.

### TUN-P0-005 — `tunner-context` MVP

Build bounded context pack from manifests + Git.

Acceptance:

- another AI/human can identify current task, authority, open blockers and relevant flows from generated pack.

### TUN-P0-006 — Docker development foundation

Compose:

- PostgreSQL;
- RabbitMQ;
- Redis;
- OpenBao;
- Mailpit;
- OpenTelemetry Collector and selected local observability components;
- placeholders/health for API + Worker.

Acceptance:

- `tunner-dev start` from clean machine prerequisites produces the healthy local core environment without requiring an object-store emulator. R2 provider contract tests run only when scoped non-production R2 credentials are available.

### TUN-P0-007 — Secrets foundation

- vault abstraction;
- OpenBao local policies;
- bootstrap workflow;
- secret scan;
- no application secrets in repository.

### TUN-P0-008 — Database/migrations

- PostgreSQL 18;
- EF Core/Npgsql;
- migration project/process;
- transaction patterns;
- migration rehearsal.

### TUN-P0-009 — Observability baseline

- OpenTelemetry;
- correlation;
- local trace/log/metric viewing;
- redaction rules.

### TUN-P0-010 — Test harness

- unit test standard;
- Testcontainers integration baseline;
- contract-test directory;
- Playwright skeleton;
- environment test command.

### TUN-P0-011 — Evidence generator

Generate release/milestone evidence manifest.

### TUN-P0-012 — R&D/source registry

Tool/check that material ADR/work references current source evidence.

### TUN-P0-013 — Security/supply-chain pipeline baseline

- SAST;
- SCA/dependency scan;
- secret scan;
- container scan;
- SBOM;
- artifact checksum/provenance hooks.

### TUN-P0-014 — Developer automation

`tunner-dev doctor/setup/start/stop/reset/health/test`

### TUN-P0-015 — Documentation validation

- links;
- IDs;
- duplicate IDs;
- schema validation;
- authority headers;
- stale source review dates.

## 4. P0 non-goals

Do not implement:

- full Account UI;
- generic workflow DSL;
- production RabbitMQ cluster/HA tuning beyond the selected messaging contract;
- Finance;
- all Connected Product workflows;
- generalized plugin marketplace;
- production cloud IaC before cloud selection.

## 5. P1 headline backlog

- API/Worker composition;
- module bootstrap conventions;
- database transaction/outbox;
- RabbitMQ dispatcher/consumer topology + publisher confirms;
- durable work/inbox;
- Redis cache baseline and non-authority safeguards;
- S3-compatible object-storage abstraction with Cloudflare R2 primary adapter;
- SignalR realtime adapter baseline;
- gRPC/gRPC-Web policy and selective scaffolding;
- authn/authz primitives;
- policy/version resolver;
- audit append;
- provider adapter baseline;
- environment model;
- error/problem standard;
- contract publishing pipeline.

## 6. P2 preparation gate

Before Account & Identity coding, produce the Account & Identity FRD with:

- canonical state model;
- flows;
- data schema;
- APIs;
- OAuth/passkey/MFA rules;
- verified-contact lifecycle;
- sessions/devices;
- recovery;
- notifications;
- audit;
- UI requirements;
- acceptance cases.

## 7. Milestone evidence example

P0 can close only if a clean environment executes:

```text
tunner-dev doctor
tunner-dev setup
tunner-dev start
tunner governance validate
tunner-test dev
tunner context build
tunner governance milestone close-check P0
```

with machine-readable PASS evidence.

## 8. Technology foundation closure under whole-project documentation gate

The technology foundation decisions below are closed, but they no longer authorize implementation by themselves. Whole-project documentation closure under document 16 is mandatory before any development plane begins. Accepted ADR/PDR records cover:

- RabbitMQ transport/topology and PostgreSQL outbox/inbox;
- Redis distributed cache and SignalR backplane;
- Cloudflare R2 primary object storage behind S3-compatible `IObjectStorage`;
- REST vs SignalR vs gRPC-Web vs native gRPC scope;
- local observability reference stack;
- React/TypeScript/Vite/TailAdmin frontend;
- separate User/Admin ASP.NET Core BFFs;
- ASP.NET Core Identity + OpenIddict;
- GitHub + GitHub Actions;
- Resend/Mailpit email strategy; Mailgun approved alternate deferred;
- OpenAPI 3.1 executable baseline;
- provider/bank evidence establishes external payout finality; Tunner Finance remains limited to the approved payable, settlement, payout, reconciliation and accounting-control contracts;
- 18+ Tunner Accounts only.

The whole-project documentation-closure gate in document 16 has passed for baseline 1.6.0. **P0 is authorized.** P1–P8 remain blocked until the P0 feature-development enablement gate in section 10 passes.

## Additional P0 closure work

- **TUN-P0-016** — Scaffold React/TypeScript/Vite/TailAdmin User and Admin applications with shared governed UI package.
- **TUN-P0-017** — Scaffold separate User/Admin ASP.NET Core BFFs and cookie/session security baseline.
- **TUN-P0-018** — Establish ASP.NET Core Identity + OpenIddict foundation and external-provider adapter contracts; Google + Microsoft first adapters.
- **TUN-P0-019** — Configure GitHub repository Rulesets, CODEOWNERS, required checks, reusable Actions workflows, artifact attestations and release evidence.
- **TUN-P0-020** — Add Mailpit local/test email plus the Resend primary adapter using the official .NET SDK, stable Tunner delivery idempotency and verified webhook ingestion. Keep Mailgun as a deferred alternate only.
- **TUN-P0-021** — Add `data_region` / residency-policy metadata to Connected Product environment foundation.
- **TUN-P0-022 — RESOLVED PRE-P0** — Product Decisions PDR-0005–PDR-0008 establish global reach, Tunner-managed merchant/payment orchestration, DOB-based 18+ eligibility and the Connected Product service-scope boundary.
- **TUN-P0-023** — Lock OpenAPI 3.1 executable-contract baseline for .NET 10 and document 3.2 migration trigger.
- **TUN-P0-024** — Implement `IObjectStorage` Cloudflare R2 adapter with the official AWS SDK for .NET; add dedicated dev/test R2 contract tests, SHA-256 verification, presigned-URL controls and residency-capability checks. R2 is external to Docker Compose.
- **TUN-P0-025** — Establish provider-neutral payment adapter boundary and pin the stable official `Stripe.net` SDK; scaffold signed/idempotent Stripe webhook ingress without implementing P5 business payment flows.

### Finance-plane platform boundary
P7 Finance & Accounting implements Tunner-managed financial activity: Tunner fees, provider costs, tax evidence, Product payable, settlement, payout, reconciliation and the platform financial-control ledger.


## 9. P0 governance-control-plane additions

The following are mandatory before feature-development planes P1–P8 proceed:

### TUN-P0-026 — Agent skill registry and entry contract

Deliver:

- `.agent/AGENTS.md` as a concise repository map/entry contract;
- all mandatory role skill directories with `SKILL.md` manifests from document 12;
- skill schema/validation;
- role/version/hash inclusion in evidence/context manifests.

### TUN-P0-027 — Role activation and review-matrix engine

Deliver:

- impact classification;
- automatic required-role calculation;
- mandatory-review enforcement;
- role findings/evidence records;
- scoped blocking semantics.

### TUN-P0-028 — Governance-controlled orchestrator

Deliver:

- `tunner-orchestrator` internal engine;
- unified `tunner work ...` CLI surface;
- no bypass of governance eligibility/gates;
- deterministic stop/block/handoff behavior.

### TUN-P0-029 — Adaptive context index and retrieval engine

Deliver:

- authority/repository index;
- dependency graph;
- work-item/flow/contract/decision mappings;
- `CORE/TASK/EXPANDED/FULL_AUDIT` modes;
- configurable context/token budgets;
- access-scope vs prompt-scope separation;
- source-hash freshness/invalidation;
- incremental escalation and `INSUFFICIENT_CONTEXT`.

### TUN-P0-030 — Persistent project memory/state

Deliver repository-native:

- `PROJECT_CONTEXT.md`;
- `CURRENT_STATE.md`;
- `DECISION_INDEX.md`;
- `RISK_REGISTER.md`;
- `HANDOFF.md`;
- milestone/sprint/work-item/TODO/defect/evidence records.

No critical state may exist only in a chat.

### TUN-P0-031 — Governance/context cost and quality telemetry

Record per governed AI execution, without storing secrets:

- context mode;
- artifacts included/excluded;
- reason for expansion;
- approximate input/context usage where provider telemetry permits;
- cache hits/misses;
- stale-context regeneration;
- role activation;
- outcome/gate status.

Purpose: optimize context quality and development cost without weakening authority coverage.

### TUN-P0-032 — Fresh-agent recovery test

Acceptance:

- start an AI agent/session with no prior Tunner conversation;
- provide repository access only;
- agent resolves current authority, current milestone/work item, applicable roles, blockers, tests/evidence and exact next action;
- agent receives bounded context by default;
- agent can escalate to repository-wide investigation through governance;
- result is machine-recorded PASS evidence.

## 10. Feature-development enablement gate

Documentation activation authorizes **P0 only**.

P1–P8 feature/platform development may begin only after mandatory P0 governance-control-plane items pass their acceptance/evidence gates, including at minimum TUN-P0-003, 004, 005, 010–015 and 026–032.

This is not a manual approval chain. Once machine-verifiable P0 gates pass and no governed blocker exists, `tunner governance next` may expose the next authorized P1/P2/P3 work according to backlog/dependency policy.
