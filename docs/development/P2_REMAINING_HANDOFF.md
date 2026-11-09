# P2 remaining work — 2026-11-09

Branch: codex/p2-006-assurance-20261108. Validated correction source: c208fdd589ac5bcac9055eb39f8cbe184ccdd123.

Required P2-001..007 audit completed before corrections: [audit](P2_001_007_AUDIT_20261109.md). P2-001 historical DONE retained; P2-002..006 VALIDATION; P2-007..019 remain pending.

Two confirmed workflow defects are corrected: exact-request completion recovery and expired registration pointer renewal. Five new regressions failed before fixes and passed after them. Executed full148 Identity,81 browser,31 unit/theme,237 architecture and7 starter checks; build/typecheck pass. Per-component execution commits and exact unchanged-input equivalence are retained in governance/evidence/p2-recovery-20261109/test-index.json. No claim that every command reran at the same SHA.

Three technical UI RVs require native final checks/closure after evidence commit. Existing Figma scopes/consents retained; no new external edits.13 preexisting dirty paths stay hash-identical and unabsorbed. Local branch remains unpublished; prior security PR21 stays separate.

Exact next action: repair DEF-TUN-P2-017-GOV-001 through later TUN-P0-004 Revision lineage, with failing native regressions before implementation; refresh stale active P2 source proof before relying on effective LOCAL_VALIDATED dependencies to start later features. Governance green from the known status bypass is insufficient. Then continue eligible P2-007 and content/configuration019 preparation and all remaining slices, using current FRDs/PDRs/AMD supersession and read-only Figma/FigJam.

Open content gap DEF-TUN-P2-019-CONTENT-001 requires persistent versioned validated publication and secure role-based administration, separated from policy authority. Design/policy/performance TODOs stay assigned; no synthetic production configuration. Product acceptance, protected-main merge, production enablement and release remain separate gates.
