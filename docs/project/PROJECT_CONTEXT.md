# PROJECT_CONTEXT

## Purpose

Repository-native, concise context for Tunner. It is derived from approved authority and does not replace it.

## Product and authority

- Name: Tunner Platform
- Effective authority: Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001.
- Baseline status: ACTIVE / LOCKED.
- Active amendment: AMD-0001 (approved, effective 2026-09-29).
- P0: authorized. P1–P8: blocked pending the P0 enablement gate.
- Local authority mirror: `docs/authority/`; local bundle verification passed on 2026-09-29.
- External canonical locations: Development Documentation, Pre-P0 package, and decisions links are recorded in `docs/authority/current-authority.json`.
- External-source access: PASS — live Drive inventories are readable for Development Documentation, the Pre-P0 package, Decisions, and the FRD/Authority root; controlling authority and architecture-guideline files were fetched on 2026-09-30. A complete file-by-file Drive-to-mirror comparison remains TUN-P0-033 scope.

## Architecture and boundaries

- P0 owns only the governance/development control plane.
- Governance tooling is isolated in `tools/`; production application projects must not depend on it.
- Product modules, user/admin UI, providers, financial behavior, contracts, and business workflows remain outside P0 unless required only as non-behavioral pipeline placeholders.

## Non-negotiable rules

- Approved authority outranks FigJam, Figma, and implementation code.
- Do not invent Product, financial, legal, compliance, provider, contract, or UX behavior.
- Governance controls orchestration; no bootstrap exception survives B3.
- Project state must be reconstructable without chat history.
- Use the smallest sufficient authority/context and return `INSUFFICIENT_CONTEXT` when necessary.
