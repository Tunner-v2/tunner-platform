# P2-006 — versioned assurance and operation-bound verification

Implemented framework: explicit versioned assurance selection, persistent exact-operation one-use password proof, current caller/session/restriction checks, transactional local owner mutation/outbox, guarded User BFF bridge and approved same-screen verification UI. The isolated browser starter packages a Development-only test carrier; no Account settings are changed.

Authority: locked Baseline1.6.0 plus active amendments/PDRs; FRD17§§7–8, catalog32§§17–18, ADR0006, PDR0011/AMD0007, AMD0008/0009. AMD0005 resolution is in P2_006_UI_AUTHORITY_REVIEW.md.196 current artifacts verified before implementation; governance reported TUN-P2-006 actionable. Original P0/P1 closure is preserved.

Current source validation: a2df3359f8092e1e816125fe384e7a8a86785c68. All executable behavior was tested at d141823d792ecbd11eedc0632d562c3a4f58431f; the sole later source delta classifies the local test host reference in the inventory. Typed inventory/unit tests were rerun at the current SHA.147 backend,31 unit/theme,77 browser and7 starter checks pass; no failed/skipped/flaky cases. Exact raw/portable provenance and screenshots: governance/evidence/ui/step-up-20261109/test-index.json.

Approved UI: Figma98:27 chooser plus exact three scoped Product approvals (availability, password detail/copy, responsive/interaction states). Reuse one shared Zod/form/error capability and native modal. Actual independent local test carrier uses guarded /browser-testing/step-up; the modal never changes the owning pathname. Figma/Drive are read-only. The test carrier is not an Account Center implementation.

Production prerequisites remain explicit trusted assurance policy/method mappings/lifetimes, published content provenance and a transactional owner/completion ledger for each future sensitive action. No synthetic fixture may activate production. Passkeys/TOTP and later Account features remain separately governed work; no arbitrary or weaker fallback is added.

Historical policy/proof slice receipts remain under p2-006-policy-20261108 and p2-006-proof-20261108. The current complete engineering report is P2_006_IMPLEMENTATION_REPORT.md. Native source/UI/revision gates control readiness; scoped visual approval does not assert whole-P2 Product acceptance, protected-main merge or release.
