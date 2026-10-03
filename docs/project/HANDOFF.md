# HANDOFF

## Milestone

- ID: P1 — Platform Foundation
- Status: ACTIVE
- Completed P1 work: TUN-P1-001 — authority-backed refinement; TUN-P1-002 — Platform host composition and module bootstrap; TUN-P1-003 — transactional persistence and outbox primitive; TUN-P1-004 — OpenAPI 3.1 executable contract-publishing foundation; TUN-P1-005 — AMD-0004 capability-oriented backlog and technical-primitives reuse audit. All have passing validation and role-review gates.
- Current P1 work: TUN-P1-006 through TUN-P1-011 are DONE as technical-only foundations after their passing validation gates and TUN-P1-020’s truthful lifecycle remediation. TUN-P1-020 is DONE: it adds an explicit, schema-validated `product_acceptance_required: false` opt-out that permits only VALIDATION to DONE, while preserving normal Product-Acceptance progression for all other work items. No Product, provider configuration, credential, deployment, release, or runtime behavior was added.
- P0 historical integration: change/TUN-P0-027-role-activation merged by PR #10 at 7bf72b9.
- P1-002 result: application-only `Tunner.Platform.slnx`, governance-only `Tunner.Governance.sln`, technical API `/health`, empty Worker composition root, explicit module registration, and deterministic architecture/boundary checks.
- P1-003 result: `platform.outbox_messages`, a narrow owner-context coordinator, a forward-only migration, and two real PostgreSQL atomicity tests. It excludes Product/domain tables, RabbitMQ dispatch/consumers, inbox/durable work, provider behavior, and Product APIs.

- AMD-0005 is active and GLOBAL; its canonical Drive copy and all amended effective overlays are mirrored with provenance and hash verification. TUN-P1-021 is DONE: Drive synchronization, P0/P1 conformance review, active-amendment context inclusion, Authority Resolution Pass enforcement, role reviews, and final validation are recorded. The audit found no currently eligible new P1 implementation item; P1-012 through P1-018 retain genuine contract/selected-UX blockers, and P1-019 retains its security-review gate.
- AMD-0004 authority mirror is hash-verified from canonical Drive Markdown. P0 remains closed; P1 planning must reuse cohesive technical primitives where approved and never treat flows as one-to-one implementation stacks.

## Verified local evidence

- `tunner authority verify`: VERIFIED for 79 hash-checked artifacts.
- `tunner governance validate`: valid with no diagnostics.
- All 26 declared P0 work items are `DONE`; the close-check re-evaluates every individual final gate as `READY`.
- The P0 governance-engineer, tester/QA-engineer, and auditor reviews pass in `governance/evidence/P0-MILESTONE-ROLE-REVIEWS.json`.
- `dotnet build Tunner.Governance.sln --no-restore` and the governance functional suite pass with zero build warnings/errors.
- `tunner governance milestone close-check P0` returns machine-readable `PASS`; state progression was checked at every policy transition through `CLOSED`.
- TUN-P1-003 operator evidence: application restore/build completed with zero warnings/errors, four unit checks and two real PostgreSQL atomicity checks passed, and the non-production forward-only migration rehearsal applied `20261001230000_P1TransactionalOutbox`.
- TUN-P1-004 operator evidence: application restore/build completed with zero warnings/errors; build-time generation produced `obj/openapi/tunner-openapi.json` as OpenAPI 3.1; source validation passed; and all 5 architecture checks passed. No runtime document route, Product endpoint, or SDK behavior was added.
- TUN-P1-005 operator evidence: authority verification passed for 79 artifacts; governance validation, documentation, portability, role-review, and P1-005 readiness gates passed. Its reuse audit found no shared technical primitive to add or remediate, and it added no Product behavior.

## Boundary and exact next action

- P0 is integrated into protected `main` by approved PR #10; this remains a governance outcome, not a production release. Existing human approval requirements remain intact for future changes.
- TUN-P1-006 through TUN-P1-009 are truthfully complete technical-only foundations. Their original boundaries remain unchanged: they do not authorize Product endpoints/events/semantics, consumer or provider operations, credentials, runtime configuration, deployment, release, or protected-main integration.
- Exact next action: run `tunner governance next`; no new P1 implementation item is currently eligible. The next implementation requires the relevant approved authority boundary, beginning with P1-012 authorization contract scope. The PostgreSQL/Redis Testcontainers suite passed 8/8 with `TUNNER_RUN_CONTAINER_TESTS=1`; the prior non-blocking TEST_EVIDENCE_GAP is remediated. No Product or architecture decision was made.

## 2026-10-02 validation remediation and P1-010 refinement

- DEF-TUN-P1-003-001 is CLOSED with fresh static-validation remediation evidence. The shared migration context may contain later independently governed platform mappings; P1-003 continues to own only OutboxMessage and its narrow transaction boundary.
- P1-010 is DONE as a technical-only foundation: Platform supplies an authenticated-policy BFF mapping extension, deny-by-default server-derived audience resolution, explicit origin/rate protection hooks, and a payload-minimal resynchronization hint. The current API host is not exposed as realtime; later BFF, identity, and domain owners retain their behavior.
- P1-011 is DONE as a technical-only protocol-boundary evaluation. ADR-0004 and Document 15 remain the authority: REST/JSON through BFF for browser commands/queries, SignalR for browser realtime, REST/OpenAPI plus signed webhooks/events for Connected Products, typed in-process contracts in the cohesive process, and selective gRPC only after its actual trigger and ADR. No gRPC runtime artifact was added.
