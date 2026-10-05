# P1 capability-oriented decomposition and technical-primitives reuse

## Authority and boundary

This executable P1 backlog applies AMD-0004. It is capability-oriented: a `TUN-FLOW-*` identifier traces authoritative behavior where applicable, but never dictates an isolated class, endpoint, project, table, worker, or frontend tree. No record authorizes Product/domain behavior not already defined by its cited authority.

Before a shared formatter, parser, normalizer, canonicalizer, or deterministic helper is added, the governing work item searches existing BuildingBlocks, Platform capabilities, owning-module capabilities, frontend shared packages, and framework capabilities. A cohesive reusable capability is added only when an approved use case requires it.

## Existing-code audit

The 2026-10-02 P1 source audit found no money, currency, date/time, percentage, slug, text, Unicode, whitespace, URL/path, filename, or generic formatting/canonicalization implementation in the current P1 code. No duplicate shared primitive is present to remediate.

`src/Tunner.Observability/TelemetryAttributePolicy.cs` validates opaque telemetry identifiers. It remains an Observability-owned safety boundary, not a candidate generic canonicalization capability, because its acceptance criteria are telemetry-specific and security-sensitive.

No `Common`, `Utils`, `Helpers`, or equivalent dumping-ground project/namespace is introduced.

## Executable capability backlog

| Work item | Capability boundary | Dependency eligibility and explicit gate |
| --- | --- | --- |
| TUN-P1-005 | Capability-oriented backlog and reuse audit | Authority-change refinement; no Product behavior or shared code. |
| TUN-P1-006 | Post-commit RabbitMQ dispatch and consumer topology | Requires transactional outbox and P1-005 reuse audit. |
| TUN-P1-007 | Durable work, inbox, retry mechanics | Requires outbox, messaging topology, and P1-005. |
| TUN-P1-008 | Redis cache and ephemeral coordination | Requires host composition and P1-005. |
| TUN-P1-009 | Cloudflare R2 object-storage adapter | Requires host composition, P1-005, and current R2 primary-source evidence. |
| TUN-P1-010 | SignalR realtime hints/resynchronization | Completed as the reusable authenticated, server-scoped, non-authoritative browser-realtime foundation. No BFF host, domain or Product signal is defined. |
| TUN-P1-011 | gRPC/gRPC-Web protocol eligibility boundary and conditional scaffolding | Completed eligibility audit: no runtime implementation unless an approved feature/process trigger exists; future adoption requires a feature-specific ADR. |
| TUN-P1-021 | Global-authority synchronization and implemented-state conformance audit | Completed; preserved aligned foundations and corrected only verified authority/context/governance/documentation drift. |
| TUN-P1-012 | Technical authorization/idempotency/expected-version primitives | Requires an authorization contract boundary. |
| TUN-P1-013 | Effective-policy version resolver mechanics | Requires an owner policy contract. |
| TUN-P1-014 | Audit append/correlation primitive | Document 28 section 9 and Document 32 define the approved audit/operation contract; append/integrity mechanics only, with owner-controlled semantics. |
| TUN-P1-015 | Provider-neutral adapter/correlation boundary | Documents 23/32 and accepted ADR-0014 define stable correlation/idempotency and typed provider evidence; owner execution/finality stays outside this foundation. |
| TUN-P1-016 | Platform environment configuration model | Document 06 defines engineering classes and deploy-time endpoint/secret-reference separation; explicit immutable module configuration only, without Product/deployment/secret-resolution behavior. |
| TUN-P1-017 | Problem/error technical standard | Requires a public error-contract boundary. |
| TUN-P1-018 | User/Admin shared UI foundation | UI_REQUIRED until canonical UX/Figma scope is selected. |
| TUN-P1-019 | User/Admin BFF technical foundation | SECURITY_REVIEW_REQUIRED until BFF security/session review. |

The table is not an execution ranking. `tunner governance next` determines readiness from recorded dependencies, evidence, calculated roles, and genuine blockers.
