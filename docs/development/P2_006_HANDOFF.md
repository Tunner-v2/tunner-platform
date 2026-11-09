# P2-006 current handoff

Branch codex/p2-006-assurance-20261108. Engineering source a2df3359f8092e1e816125fe384e7a8a86785c68; full executable test source d141823d792ecbd11eedc0632d562c3a4f58431f, followed only by typed inventory metadata validation. Work is CODE_REVIEW until native final gates and required compatibility revision closures pass. Do not infer DONE/Product or release approval.

Implemented the versioned policy, persistent exact-operation one-use proof, trusted User BFF bridge, approved same-screen chooser/password UI, shared form/modal fixes and guarded Development-only browser carrier. Detailed authority, inventory, defects, tests, file ownership and boundaries: P2_006_IMPLEMENTATION_REPORT.md.

Actual results:147 backend,31 unit/theme,77 browser and7 starter checks PASS,0 fail/skip/flaky. Current source/reference screenshots and raw/portable hashes: governance/evidence/ui/step-up-20261109/test-index.json and validated-review.html. Three exact visual adaptations have direct Product consent; Figma and Drive stay read-only. All activated specialist assessments are executing-agent reviews, not independent human review.

Local browser: https://localhost:24443/registration -> inbox http://127.0.0.1:28025 -> Personal Information -> Account Ready -> https://localhost:24443/login -> https://localhost:24443/browser-testing/step-up. Sign in first; select Other method and enter the current password. This records only a local test result. Fresh sessions can exercise a new result. Local PostgreSQL127.0.0.1:25432 ->5432 uses tunner-security/postgres:18.6-r1; Mailpit28025/21025 uses tunner-security/mailpit:1.33.1-r1. Existing data/keys/ports preserved; no monitoring rebuild or other-project cleanup.

Production activation still requires explicit approved assurance mappings/lifetimes, published copy and an owner operation/completion ledger. No synthetic local provider is enabled in production. Current browser flow does not implement future passkey/TOTP/Account features.

Exact next action: commit current evidence, run current scoped/global UI gates, close P1-018-RV003 and the two P2-004 UI compatibility revisions only after native close-check passes, rerun P2 commit-readiness audit, then resolve TODO-TUN-P2-006-EVIDENCE001. Advance TUN-P2-006 to VALIDATION only after READY and record LOCAL_VALIDATED lineage; native next determines the next eligible P2 item. Preserve original historical DONE evidence.

No new P2 PR published. Existing branch-push automation creates a main PR with auto-merge when no PR exists; use controlled draft publication before any push. No protected-main merge, registry publication, production deployment or release authorized. Security PR21 remains separate draft; open vulnerabilities remain release-blocking.

All13 pre-existing dirty paths in artifacts/p2-005-auth-20261021/preserved-dirty-before.json are hash-identical and excluded. Do not overwrite docs/context/current or docs/project. Refresh isolated native context at artifacts/p2-006-assurance-20261108/context, verify CURRENT, and record final native outcomes before handoff.
