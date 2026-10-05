# P1 Platform Foundation Refinement

## Purpose and authority boundary

This is the refinement output for `TUN-P1-001`. It decomposes the approved P1 headline backlog without assigning Product, financial, identity, Connected Product, provider-finality, or UI behavior that the authority has not already defined.

The governing sources are the effective Development Planes document, Engineering & Technology Foundation, Repository/Solution & Module Architecture, Runtime Communication & Infrastructure Technology Scope, and accepted ADR-0001, ADR-0002, ADR-0004, and ADR-0012. P0 is closed and integrated into protected `main`; this makes P1 scheduling eligible, but does not turn an incomplete Product requirement into an implementation assumption.

## AMD-0004 capability-oriented execution control

AMD-0004 is active for all P1 planning and implementation. `TUN-FLOW-*` identifiers remain traceability authority, not one-to-one implementation stacks. Before a helper, formatter, parser, normalizer, or canonicalizer is added, its governed work item searches and reuses or extends compatible existing capability. Domain/business semantics, technical canonicalization, and presentation formatting stay separate.

The executable backlog and focused audit are maintained in `docs/development/P1_CAPABILITY_DECOMPOSITION_AND_PRIMITIVE_REUSE.md`. No generic shared library is created by this planning control.
## First materialized P1 work item

`TUN-P1-002 — Platform host composition and module bootstrap` is the first materialized implementation candidate. It is limited to an application-only solution, the authority-required API/Worker deployable composition, and module-registration/dependency direction. It creates no Product endpoint, user/account behavior, persistence schema, external transport, provider call, UI, authentication flow, or environment/residency behavior. It remains `DRAFT` until this refinement reaches `DONE`, then must pass its own lifecycle, role, context, and evidence gates.

It is first because the authority explicitly defines API and Worker as the initial deployable units and says their composition roots reference modules. That is a bounded structural prerequisite for later runtime adapters while avoiding a choice of Product/domain implementation order.

## Atomic candidate backlog

| Candidate | Authority-backed scope | Explicit boundary before implementation |
| --- | --- | --- |
| Platform host composition and module bootstrap | API/Worker composition roots, module registration, dependency direction | Materialized as `TUN-P1-002`; no Product endpoints or business modules. |
| PostgreSQL transaction and outbox primitive | Atomic authoritative rows plus outbox insertion; owner-table rule | Completed as `TUN-P1-003`: only the Platform Persistence/Outbox table and a narrow owner-context transaction coordinator; no Product/domain schema, transport, or consumer behavior. |
| RabbitMQ dispatcher and consumer topology | Post-commit dispatch, publisher confirms, consumer handling | No broker-as-authority, Connected Product broker access, or workload topology beyond an approved scope. |
| Durable work and inbox primitive | Durable leasing, retry eligibility, inbox/idempotency mechanics | No long-lived business timer or workflow semantics without an owner-domain scope. |
| Redis cache baseline | Non-authoritative cache/ephemeral coordination safeguards | Redis may not hold identity, finance, audit, or durable Product truth. |
| Cloudflare R2 object-storage adapter | S3-compatible `IObjectStorage`, R2 primary adapter, deterministic fake/contract-test boundary | Use only capabilities verified in current R2 source evidence; no object becomes usable before owner-domain validation/scanning policy. |
| SignalR realtime adapter baseline | Tunner-owned realtime hints and API/read-model resync boundary | No entitlement/finality/mutation authority in realtime messages. |
| gRPC/gRPC-Web protocol eligibility boundary and conditional scaffolding | REST default, same-process typed calls, selective gRPC only at a justified boundary | No runtime implementation unless an approved feature/process trigger exists; a feature-specific ADR is required before any gRPC-Web or native gRPC endpoint/client is added. |
| Authentication and authorization primitives | Technical authorization/idempotency/expected-version primitives | No Account/Identity implementation, credential flow, OAuth server, external login, session, or recovery behavior; those are P2-gated. |
| Policy/effective-version resolver | Shared technical resolver mechanics | No policy values, commercial decision, Product behavior, or domain authority is invented. |
| Audit append primitive | Append/correlation mechanics | No Audit/Governance Product workflow, approval, retention, or investigation semantics beyond cited authority. |
| Provider-adapter baseline | Provider-neutral boundary and correlation/idempotency mechanics | No P7 payment-domain behavior or provider-finality claim; `P1-PAY-ADAPTER-001` remains the authority relocation reference. |
| Platform environment model | Technical runtime/configuration environment modeling | No Connected Product environment, `data_region`, or residency behavior; that is P3-gated. |
| Error/problem standard | RFC-aligned problem/error contract mechanics | No domain error taxonomy or Product-specific contract is invented. |
| OpenAPI contract publishing | OpenAPI 3.1 executable contract pipeline | Completed as `TUN-P1-004`: deterministic .NET 10 build-time OpenAPI 3.1 generation and local contract verification only; endpoint/domain content still needs its owning authority. |
| User/Admin UI foundation | React/TypeScript/Vite/TailAdmin shared governed UI foundation | Materialize under `P1-UI-001` only after canonical UX/Figma scope is selected; no screen/layout is inferred. |
| User/Admin BFF foundation | Separate ASP.NET Core BFFs and secure cookie/session baseline | Materialize under `P1-BFF-001`; no Account/Identity behavior or browser-token storage is introduced. |

## Intentional later-plane exclusions

- `P2-ID-001` (ASP.NET Core Identity, OpenIddict, and external-login adapters) remains P2.
- `P3-ENV-001` (Connected Product `data_region` and residency-policy behavior) remains P3.
- `P4-NOTIF-001` (Resend/Mailpit notification adapter behavior) remains P4.
- Financial/provider-finality behavior remains P7 even where a P1 provider-neutral contract is later needed.

## Refinement gates for later candidates

Each candidate must be materialized as its own work-item record before implementation, with a precise authority subset, impact classification, calculated mandatory roles, bounded context, acceptance tests, evidence paths, and only verified dependencies. The following are hard boundaries rather than optional guidance:

- UI work must cite the selected canonical UX/Figma source; otherwise it enters `UI_REQUIRED`.
- gRPC-Web/native gRPC feature work must cite the feature/process-boundary ADR. In the absence of a concrete trigger, the protocol-eligibility audit records `NOT_PRESENT`; it does not enter `CONTRACT_REQUIRED` or create scaffolding.
- R2 implementation must refresh/verify current primary-source compatibility evidence before selecting capabilities.
- Any external library, provider, protocol, or standard change activates source research and its required specialist review.
- Any P2/P3/P4/P7 behavior discovered while working P1 is recorded as a separate later-plane item or decision, not absorbed into P1.

## Validation expectations for this refinement

This document and the P1 work records are validated by authority verification, governance schema validation, calculated-role review completeness, expanded-context verification, link/document validation, and the P1-001 evidence manifests. They do not claim API/Worker runtime health, production readiness, Product acceptance, or protected-main integration for unimplemented P1 candidates.