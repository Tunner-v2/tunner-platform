# PROJECT_CONTEXT

## Purpose

Repository-native, concise context for Tunner. It is derived from approved authority and does not replace it.

## Product and authority

- Name: Tunner Platform
- Effective authority: Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- Baseline status: ACTIVE / LOCKED.
- Active amendments: AMD-0001 (approved, effective 2026-09-29) and AMD-0002 (approved, effective 2026-09-30).
- P0: authorized. P1–P8: blocked pending the P0 enablement gate.
- Local authority mirror: `docs/authority/`; local bundle verification passed on 2026-09-29.
- External canonical locations: Development Documentation, Pre-P0 package, and decisions links are recorded in `docs/authority/current-authority.json`.
- External-source access: PASS — live Drive inventories are readable for Development Documentation, the Pre-P0 package, Decisions, and the FRD/Authority root; controlling authority and architecture-guideline files were fetched on 2026-09-30. A complete file-by-file Drive-to-mirror comparison remains TUN-P0-033 scope.

## Architecture and boundaries

- P0 owns only the governance/development control plane.
- Governance/context tooling is isolated in the standalone `Tunner.Governance.sln`; production application projects must not depend on it.
- Product modules, user/admin UI, providers, financial behavior, contracts, and business workflows remain outside P0 unless required only as non-behavioral pipeline placeholders.
- P0-006 local Docker dependencies are local/test-only. No committed credential or OpenBao root token is permitted; P0-007 provides the credential-free local initialization/policy/scan foundation; later governed work owns Product-facing secret injection.

## Non-negotiable rules

- Approved authority outranks FigJam, Figma, and implementation code.
- Do not invent Product, financial, legal, compliance, provider, contract, or UX behavior.
- Governance controls orchestration; no bootstrap exception survives B3.
- Project state must be reconstructable without chat history.
- Use the smallest sufficient authority/context and return `INSUFFICIENT_CONTEXT` when necessary.
- PR approval gates protected-main integration/merge only; continue governance-eligible local/branch work while approval is pending unless a dependency explicitly requires `MERGED_TO_MAIN` or another genuine human gate.
