# PROJECT_CONTEXT

## Purpose

Repository-native, concise context for Tunner. It is derived from approved authority and does not replace it.

## Product and authority

- Name: Tunner Platform
- Effective authority: Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002 + AMD-0003 + AMD-0004 + AMD-0005.
- Baseline status: ACTIVE / LOCKED.
- Active amendments: AMD-0001 (approved, effective 2026-09-29), AMD-0002 (approved, effective 2026-09-30), AMD-0003 (approved, effective 2026-09-30), AMD-0004 (approved, effective 2026-10-02), and AMD-0005 (approved, effective 2026-10-03).
- P0: CLOSED after the machine-verifiable P0 enablement gate passed and integrated by PR #10.
- P1: ACTIVE. TUN-P1-001 through TUN-P1-011 are complete local foundations with passing scope-specific validation and specialist-review gates. TUN-P1-006 through TUN-P1-009 remain technical-only and never claim Product Acceptance. TUN-P1-020 is complete: its schema-validated, explicit `product_acceptance_required: false` declaration permits only an evidence-backed technical-only transition from VALIDATION to DONE; the normal Product-Acceptance lifecycle remains mandatory for every work item without that declaration. TUN-P1-010 is DONE as a technical-only reusable Tunner-owned browser realtime foundation under ADR-0002, ADR-0004, and Documents 01/07/08/09/15. It creates no BFF host exposure, Product-facing SignalR contract, domain event semantics, or authoritative realtime state. TUN-P1-011 is DONE as the corresponding gRPC/gRPC-Web eligibility boundary: no concrete activation trigger is present, no gRPC runtime artifact exists, and a future feature/process-specific ADR remains mandatory before adoption.
- Local authority mirror: `docs/authority/`; TUN-P0-033 is DONE and `tunner authority verify` hash-verifies all 79 mirrored authority artifacts.
- External canonical locations and original Drive IDs are recorded only in `docs/authority/current-authority.json` and artifact provenance; local mirrored documents are the reproducible working source.
- External-source reconciliation: PASS — the canonical Development Documentation and Decisions inventories were re-read on 2026-10-03. The active Drive authority remains Baseline 1.6.0 plus AMD-0001 through AMD-0005; no competing local authority copy is maintained.

- AMD-0005 is active and GLOBAL. It requires a recorded Authority Resolution Pass before a missing-authority decision/blocker, rejects duplicate decisions when active authority already resolves the question, and treats an untriggered selective capability as non-blocking. TUN-P1-021 is DONE as the evidence-backed retrospective conformance audit. It preserves valid code, corrected active-amendment context and Authority Resolution Pass enforcement, and records the non-blocking local-container integration-test evidence gap.

- AMD-0004 is active as a technical-practice amendment. It requires capability-oriented P1 planning, deterministic-mechanic reuse, and strict separation of domain meaning, technical canonicalization, and presentation formatting. It does not reopen P0 or introduce Product behavior.

## Architecture and boundaries

- P0 owns the governance/development control plane and is closed. TUN-P1-006 owns only a technical post-commit outbox read/conditional-mark boundary, generic-envelope RabbitMQ properties/headers, a durable internal topic exchange declaration, and publisher-confirm tracking. It deliberately declares no queue, binding, consumer, inbox/idempotency, retry/redrive, domain event catalog, Product API, runtime registration, or environment configuration.
- Current P1 scope is limited to the authority-backed platform foundation. TUN-P1-002 completed application-only host composition and module registration. TUN-P1-003 owns only the shared `platform.outbox_messages` record and narrow owner-context transaction mechanics. TUN-P1-004 completed only the technical OpenAPI 3.1 build-time generation/publication pipeline. TUN-P1-005 records AMD-0004 reuse and canonicalization rules, but does not authorize a generic helper library or Product behavior. These items did not invent Product/domain endpoints, schemas, security semantics, SDK behavior, runtime document routes, or later Product adapters.
- Governance/context tooling is isolated in the standalone `Tunner.Governance.sln`; production application projects must not depend on it.
- Product modules, user/admin UI, providers, financial behavior, contracts, and business workflows remain outside P0 unless required only as non-behavioral pipeline placeholders.
- P0-006 local Docker dependencies are local/test-only. No committed credential or OpenBao root token is permitted; P0-007 provides the credential-free local initialization/policy/scan foundation; later governed work owns Product-facing secret injection.
- P0-009 provides a technical OpenTelemetry/OTLP baseline only: opaque allow-listed correlation values, pre-export redaction, and a loopback-only local development/test collector contract. It does not authorize Product telemetry, billing/usage metering, production observability, or deployment behavior.
- P0-011 provides a repository-native local evidence-manifest generator only. Its write-once manifests record controlled repository-path hashes, verified authority summary, current Git identity, and explicit limitations; they are not release, Product, deployment, protected-main, or external-publication authority.

## Non-negotiable rules

- Approved authority outranks FigJam, Figma, and implementation code.
- Do not invent Product, financial, legal, compliance, provider, contract, or UX behavior.
- Governance controls orchestration; no bootstrap exception survives B3.
- Project state must be reconstructable without chat history.
- Use the smallest sufficient authority/context and return `INSUFFICIENT_CONTEXT` when necessary.
- PR approval gates protected-main integration/merge only; continue governance-eligible local/branch work while approval is pending unless a dependency explicitly requires `MERGED_TO_MAIN` or another genuine human gate.
- Repository location is runtime context, not authority. Persist in-repository references relative to `<repo-root>` with `/` separators; resolve the root dynamically and never commit a host checkout path.
