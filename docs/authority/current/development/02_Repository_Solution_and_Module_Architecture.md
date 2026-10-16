> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 02 — Repository, Solution & Module Architecture

## 1. Repository strategy

Use one governed Git repository initially so code, documentation, schemas, migrations, SDK contracts and evidence-producing automation evolve atomically.

Governance/docs/tools remain **outside the application solution**.

```text
<repo-root>/
├── .tunner-root
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


## Shared technical capability organization

Shared code is organized by cohesive technical capability, not by workflow and not in a catch-all helper namespace.

Representative structure may evolve during implementation while preserving these boundaries:

```text
src/
  BuildingBlocks/
    Money/
    Time/
    Text/
    Identifiers/
    Serialization/

web/
  packages/
    formatting/
    ui/
    contracts/
```

The names above are examples, not a requirement to create empty projects/folders. Create a shared capability only when actual repeated technical behavior exists or a known cross-cutting contract requires it.

### Reuse/discovery rule

Before implementing deterministic technical mechanics:

1. search `BuildingBlocks`, `Platform`, the owning module and frontend shared packages;
2. reuse or extend an existing compatible primitive;
3. if semantics differ, keep the domain behavior separate;
4. if mechanics are duplicated, consolidate them behind a cohesive capability;
5. do not introduce screen-local/module-local formatters or normalizers when a governed shared capability already owns that concern.

Flow-to-code mapping is many-to-many. A `TUN-FLOW-*` reference proves behavior/traceability; it does not require a dedicated service, controller, repository, table, worker, component tree or copy of shared mechanics.

### Server/client authority

- persisted/external/security-relevant canonical values are validated/canonicalized authoritatively on the server;
- browser utilities may provide equivalent previews/display behavior but do not become business or persistence authority;
- common .NET/TypeScript behavior uses shared fixtures/test vectors or generated contracts to prove conformance rather than forcing source-code sharing across runtimes.

Additional architecture acceptance cases:

| ID | Given | When | Then |
|---|---|---|---|
| REP-005 | another flow needs an existing technical formatter/normalizer | developer implements | existing shared capability is reused/extended rather than copied |
| REP-006 | two domains have similarly named business validation | DRY refactor proposed | common mechanics may be shared; domain policy remains owner-specific |
| REP-007 | .NET and TypeScript implement one canonical rule | contract tests run | shared vectors produce semantically equivalent results |
| REP-008 | UI formats authoritative Money | formatter runs | amount/currency remain unchanged and no financial calculation occurs |
| REP-009 | persisted slug is created/changed | client previews and server accepts | server performs authoritative canonicalization and owning domain applies uniqueness/mutability policy |

## Location-independent repository path policy

The Tunner repository is relocatable. No committed code, configuration, governance record, evidence manifest, build definition, script, solution/project reference or generated project state may depend on a developer-specific drive letter, user-profile directory, checkout directory, CI workspace directory, container mount path or other machine-specific absolute filesystem path.

Rules:

1. Repository-internal persisted paths are stored relative to the repository root and use `/` as the canonical logical separator.
2. A committed `.tunner-root` marker identifies the repository root independently of the current working directory.
3. Tooling resolves the repository root dynamically. The supported resolution order is:
   - explicit `--repo-root` override when supplied and validated;
   - `TUNNER_REPO_ROOT` environment override when supplied and validated;
   - Git top-level discovery when Git metadata is available;
   - upward search for `.tunner-root`;
   - otherwise fail with an actionable error. Never guess.
4. Runtime code may internally resolve an absolute path after root discovery, but such machine-specific absolute values must not be hardcoded or persisted as repository authority/configuration.
5. Scripts must not assume they were launched from the repository root; they resolve root from the standard resolver or their own repository-relative location.
6. Solution/project references, build props, Docker/Compose bind mounts, test/evidence paths and CI scripts use repository-relative paths or runtime-provided workspace variables.
7. External resource identifiers such as Google Drive URLs/IDs, GitHub URLs, Figma URLs, provider URIs and network endpoints are not filesystem paths and are governed separately.
8. Explicit external filesystem locations, when genuinely required, are configuration/runtime inputs and must not be silently converted into committed machine-specific paths.
9. Path resolution rejects traversal outside the repository root unless the operation explicitly declares an authorized external-path capability.
10. Windows, Linux, container and CI execution must resolve the same logical repository artifacts regardless of checkout location.

Acceptance:

- moving/cloning the repository to another parent directory or drive does not require source/config edits;
- `tunner authority`, `tunner governance`, `tunner context`, `tunner-dev` and `tunner-test` work from a nested working directory;
- repository validation rejects committed machine-specific absolute paths in governed path-bearing files unless explicitly allow-listed as external identifiers.
