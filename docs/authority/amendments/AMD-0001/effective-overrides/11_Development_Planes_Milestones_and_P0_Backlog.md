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

## 8A. Previously listed P0 feature/platform work — relocated

The former P0-016 through P0-025 list mixed governance bootstrap with product/platform implementation. AMD-0001 corrects that sequencing before development begins.

The following work is **not a P0 governance-closure requirement** and must execute only after the P0 feature-development enablement gate:

- **P1-UI-001** — React/TypeScript/Vite/TailAdmin User/Admin application foundation with shared governed UI package. Formerly `TUN-P0-016`.
- **P1-BFF-001** — User/Admin ASP.NET Core BFF foundation and secure cookie/session baseline. Formerly `TUN-P0-017`.
- **P2-ID-001** — ASP.NET Core Identity + OpenIddict implementation and external-login adapters. Formerly `TUN-P0-018`.
- **P4-NOTIF-001** — Resend production notification adapter and Mailpit local/test integration. Formerly `TUN-P0-020`.
- **P3-ENV-001** — Connected Product `data_region` / residency-policy behavior. Formerly `TUN-P0-021`.
- **P1-API-001** — OpenAPI 3.1 executable contract pipeline. Formerly `TUN-P0-023`.
- **P1-STOR-001** — Cloudflare R2 `IObjectStorage` implementation/contract tests. Formerly `TUN-P0-024`.
- **P1-PAY-ADAPTER-001** — provider-neutral payment-adapter foundation/Stripe ingress scaffolding without P7 payment-domain behavior. Formerly `TUN-P0-025`.

`TUN-P0-022` was a resolved pre-P0 decision marker, not implementation work.

The GitHub control work remains in P0 because it creates the governed development execution surface:

### TUN-P0-019 — GitHub repository governance and CI execution surface

Deliver after TUN-P0-001 creates/initializes the repository and a remote destination is available:

- canonical GitHub repository;
- protected `main`;
- Rulesets;
- CODEOWNERS;
- required checks;
- reusable Actions workflows;
- security/supply-chain checks;
- artifact/evidence hooks;
- canonical repository/workflow URLs registered in document 13 and project context.

A repository/Actions URL cannot be required before this work creates it.

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

### TUN-P0-033 — Authority mirror/import verification

Deliver:

- `tunner authority status/import/verify/diff`;
- immutable baseline provenance record;
- active-amendment provenance/hash registry;
- repository authority mirror;
- drift detection with blocking behavior;
- context-build dependency on successful authority verification.

Acceptance:

- exact baseline + AMD-0001 import verifies;
- changed/corrupted authority mirror fails verification;
- no tool silently resolves Drive/repository semantic drift;
- fresh-agent startup can identify the exact baseline/amendment provenance offline from the repository mirror.

### TUN-P0-034 — Governance self-hosting cutover

Deliver:

- bootstrap work-record schema/process for the pre-tool phase;
- machine-detectable bootstrap commit/work metadata;
- import/replay of P0 bootstrap history into `tunner-governance`;
- verification of mandatory review/evidence requirements;
- self-hosting cutover evidence record;
- permanent disabling of unrestricted bootstrap mode after cutover.

Acceptance:

- bootstrap history replays without unexplained bypass;
- any exception is explicit and resolved/blocking;
- Product feature work cannot use bootstrap mode;
- subsequent P0 work is governed by the tool itself.

## 10. Feature-development enablement gate

Documentation activation authorizes **P0 only**.

P1–P8 feature/platform development may begin only after the P0 implementation and evidence gates in document 36 pass. Required P0 work includes repository/Git governance and developer-system foundations (`TUN-P0-001`–`015`, with `TUN-P0-019` for the GitHub execution surface) plus governance-control-plane additions `TUN-P0-026`–`034`. Items explicitly relocated in section 8A are excluded from the P0 closure gate.

This is not a manual approval chain. Once machine-verifiable P0 gates pass and no governed blocker exists, `tunner governance next` may expose the next authorized P1/P2/P3 work according to backlog/dependency policy.
