> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 02 — Repository, Solution & Module Architecture

## 1. Repository strategy

Use one governed Git repository initially so code, documentation, schemas, migrations, SDK contracts and evidence-producing automation evolve atomically.

Governance/docs/tools remain **outside the application solution**.

```text
/
├── README.md
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── .gitignore
│
├── docs/
│   ├── authority/
│   ├── development/
│   ├── architecture/
│   ├── contracts/
│   ├── decisions/
│   ├── compliance/
│   ├── testing/
│   ├── releases/
│   └── context/
│
├── governance/
│   ├── state/
│   ├── milestones/
│   ├── sprints/
│   ├── work-items/
│   ├── defects/
│   ├── amendments/
│   ├── decisions/
│   ├── evidence/
│   └── schemas/
│
├── tools/
│   ├── tunner-governance/
│   ├── tunner-context/
│   ├── tunner-dev/
│   └── tunner-test/
│
├── contracts/
│   ├── openapi/
│   ├── asyncapi/
│   ├── json-schema/
│   ├── proto/
│   └── examples/
│
├── src/
│   ├── Tunner.slnx
│   ├── Tunner.Api/
│   ├── Tunner.Worker/
│   ├── Tunner.ServiceDefaults/
│   ├── BuildingBlocks/
│   ├── Platform/
│   │   ├── Messaging/
│   │   ├── Caching/
│   │   ├── ObjectStorage/
│   │   ├── Realtime/
│   │   ├── Rpc/
│   │   └── Observability/
│   └── Modules/
│
├── sdk/
│   ├── dotnet/
│   ├── typescript/
│   └── generated/
│
├── tests/
│   ├── architecture/
│   ├── integration/
│   ├── contract/
│   ├── end-to-end/
│   ├── security/
│   └── performance/
│
├── deploy/
│   ├── docker/
│   └── environments/
│
└── scripts/
```

The exact names can be refined during P0, but the separation of concerns is authoritative.

## 2. Suggested domain modules

### Core identity/business foundation

- `Identity`
- `Accounts`
- `ProductIntegration`
- `BillingAccounts`
- `Commercial`
- `Subscriptions`

### Financial domains

- `Payments`
- `Invoicing`
- `Ledger`
- `Settlement`

These may initially be grouped in fewer physical projects if boundaries remain testable.

### Shared platform domains

- `Providers`
- `Notifications`
- `Support`
- `RiskSecurity`
- `AuditGovernance`
- `InternalAccess`
- `PlatformOperations`
- `DataPrivacy`

## 3. Module internal structure

Use complexity-proportional organization.

For a complex domain:

```text
Module/
├── Domain/
├── Application/
├── Infrastructure/
├── Contracts/
└── ModuleRegistration.cs
```

For a simple capability, fewer folders/projects are preferable.

Do not use ceremony as a substitute for business clarity.

## 4. Dependency direction

- Domain does not depend on Infrastructure.
- Application coordinates Domain + explicit ports/contracts.
- Infrastructure implements external persistence/provider concerns.
- API/Worker composition roots reference modules.
- One module must not reference another module’s Infrastructure.
- Cross-module calls use published application contracts or events.

## 5. Building blocks

`BuildingBlocks` is strictly technical.

Allowed:

- Result/problem types;
- clock abstraction where needed for deterministic tests;
- IDs/correlation;
- transactional abstractions;
- outbox infrastructure;
- common serialization/version primitives.

Not allowed:

- “shared customer” business entity;
- generic subscription domain object;
- Product entitlement;
- shared finance rules.

## 6. Database ownership

Prefer schema ownership such as:

```text
identity.*
integration.*
billing.*
commercial.*
subscription.*
finance.*
notification.*
audit.*
```

This is logical ownership, not a guarantee of one schema per final module.

Database roles should prevent accidental cross-module writes when practical.

## 7. Comments and documentation in code

Code should be clean and self-explanatory.

Comments explain:

- why a non-obvious invariant exists;
- external-provider behavior;
- security/financial safety reason;
- compatibility constraints;
- references to relevant RFC/flow/ADR where useful.

Do not comment obvious syntax.

## 8. No duplication rule

Before adding a helper/framework:

1. search existing Tunner capability;
2. classify business-specific vs technical;
3. reuse technical primitive;
4. do not merge separate business meanings merely to remove similar-looking code.

“DRY” must not combine different domain authority.

## 9. Architecture tests

Automated architecture tests should verify:

- module dependency rules;
- no Infrastructure-to-foreign-Domain leakage;
- no forbidden project references;
- controller/endpoints do not reference persistence directly;
- domain modules do not depend on web/UI projects;
- governance/tools are not referenced by production solution.

## 10. Acceptance cases

| ID | Given | When | Then |
|---|---|---|---|
| REP-001 | new module | project added | architecture test enforces dependency direction |
| REP-002 | Finance needs subscription information | developer implements | uses Subscription contract/reference, not direct table mutation |
| REP-003 | similar validation exists in two domains | refactor proposed | only technical commonality is shared; business semantics remain owner-specific |
| REP-004 | governance CLI exists | solution builds | CLI is outside `Tunner.slnx` production solution |

## 10. Runtime infrastructure dependency boundaries

Business modules must not depend directly on RabbitMQ, Redis, Cloudflare R2/AWS S3 SDKs, Resend/Stripe SDKs, SignalR hubs or gRPC channel construction. Those technologies are implemented in Platform/Infrastructure adapters.

Examples:

- module publishes an owner-domain event/outbox record; messaging infrastructure dispatches it through RabbitMQ;
- module requests cache through a bounded cache contract; Redis implements it;
- module stores an attachment through `IObjectStorage`; the Cloudflare R2 S3-compatible adapter implements it;
- module produces a realtime application notification; the Realtime adapter may deliver it by SignalR;
- internal network RPC is exposed only from a deliberate process boundary and implemented with gRPC.

This prevents technical products from becoming domain dependencies and keeps cloud/provider replacement practical.

### Web/BFF projects

```text
src/
  Tunner.User.Bff/
  Tunner.Admin.Bff/

web/
  tunner-user/       # React + TypeScript + Vite + TailAdmin
  tunner-admin/      # React + TypeScript + Vite + TailAdmin
  packages/
    ui/              # governed shared components/tokens only
    contracts/       # generated browser-safe client contracts
```

Browser applications do not contain domain authority. BFF projects do not duplicate domain business logic; they provide browser-session security, composition and UI-specific projections/commands.

### Repository automation

`.github/` contains GitHub Ruleset guidance, CODEOWNERS, issue/PR templates and reusable GitHub Actions workflows. Deployment workflows use OIDC federation when the selected cloud supports it instead of long-lived cloud credentials.


## Presentation Content module

Create a logical `Content` / `PresentationContent` module in the modular solution. It owns semantic `ContentKey` definitions, variants, immutable versions, publication/effective resolution, deterministic fallback metadata, content proposals and content-governance audit integration. It exposes application contracts to User/Admin BFFs and notification rendering without allowing other modules to edit its tables directly.

The module is not a separate deployable service initially. Domain modules remain authoritative for states, permissions, prices, eligibility and workflow semantics; they provide safe presentation facts/capabilities that reusable UI components combine with resolved content. Admin contextual editing is an application workflow over this module, not generic table CRUD.
