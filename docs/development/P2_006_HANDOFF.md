# P2-006 development handoff

Branch: codex/p2-006-assurance-20261108 (local). TUN-P2-006 remains IN_PROGRESS. Validated behavior:32a682ebce4b4b83523936d4bdb8f5312be18b6b. Full feature, UI, Product and release acceptance remain pending.

## Implemented and tested

Private versioned assurance selection and persistent one-use password reauthentication proofs. Proofs bind Account/session/User scheme/origin/security stamp to exact operation/correlation/resource/expected version/context and current policy. Issuance verifies current credentials and explicit method assurance. Consumption rechecks current session, contact and Account restrictions, then commits the local Identity action and outbox atomically. Replay, competing consumers and failed/throwing/expired actions cannot apply the action twice. PostgreSQL guards prohibit binding/expiry mutation or proof revival. Ending clears protected retry keys. Session authentication time, assurance, cookie key and absolute expiry remain unchanged.

Full actual Identity suite:127 PASS,0 FAIL,0 SKIP:38 proof cases plus89 existing Identity cases. Build:0 warnings/errors. Registration, age-aware eligibility, verification, login and sessions remain covered. Receipt: governance/evidence/p2-006-proof-20261108/receipt.json. Earlier fixture and downgrade-preflight failures are retained; both were corrected before the final run. The migration reuses the foundation populated-database downgrade preflight before removing schema. Historical policy-only receipt: governance/evidence/p2-006-policy-20261108/receipt.json.

No new HTTP route, frontend or production policy provider is enabled. Tests use explicitly labelled synthetic policy values and disposable PostgreSQL containers. Actual approved runtime policies are required before affected production paths are enabled.

## Exact next action

Continue guarded BFF challenge/resume and sensitive-operation owner integration within TUN-P2-006. Use docs/contracts/operation-step-up-contract.md and docs/development/P2_006_UI_AUTHORITY_REVIEW.md. Read-only Figma review found approved step-up overlay98:27 on Account Center7:5; the whole screen is not missing. Resolve Other method/password detail and required states from existing patterns, with scoped Product Owner consent for a material adaptation. Reuse shared validation and safe same-screen state; register a canonical guarded pathname for any independent screen. Complete applicable Playwright, URL/VAL/UX and rendered/reference evidence, actual policy configuration and final role/native gates before full acceptance. Keep TUN-P2-006-VALIDATION.json absent until all required scope passes.

## Browser testing and delivery

Existing local flow remains available at https://localhost:24443/registration and /login; verification inbox http://127.0.0.1:28025. It covers registration -> email verification -> Personal Information (DOB here) -> Account Ready -> password sign-in -> sign-out. Google/Microsoft federation, passkeys/TOTP/recovery and later Account Center actions remain their governed P2 items.

No new P2 PR is published. Existing push automation can create a main PR and enable auto-merge if no draft exists; use a controlled draft-publication workflow before pushing. Security PR21 remains separate, draft/main with auto-merge disabled. No protected-main merge, registry publication, production deployment or release is authorized.

Custom LGTM rebuilding remains stopped as the user instructed. Development monitoring default is prebuilt grafana/otel-lgtm:0.38.1 at digest0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7, with41 prior compatibility checks. Open vulnerability findings remain release-blocking in P1-RV001; required gates are not waived. Earlier scoped cleanup removed16 unused Tunner image references and two unused/unshared Tunner LGTM caches (Docker reported17.857GB); receipts remain P1-RV001-001-UNUSED-IMAGE-CLEANUP-20261108.json and P1-RV001-001-UNUSED-CACHE-CLEANUP-20261108.json under governance/evidence. Other projects, containers and volumes were preserved.

## Context and ownership

All13 pre-existing dirty paths in artifacts/p2-005-auth-20261021/preserved-dirty-before.json were hash-verified intact. Preserve them. Use isolated native context at artifacts/p2-006-assurance-20261108/context; do not overwrite docs/context/current or docs/project files. All10 role contracts are active; startup and slice assessments are executing-agent reviews, not independent human or final acceptance. Figma and Drive are read-only. Refresh native authority/status/next/context before resuming and continue eligible development independently of security/main integration gates.

## Final native acceptance findings

At27e02face25cb8d8317fa1422019326eb7fd682a, authority verification was VERIFIED196 artifacts, governance validation PASS826 records and isolated context CURRENT. Work-item gate remained NOT_READY for final feature evidence, final role evidence and per-route/form UI acceptance. The P2 commit-readiness audit returned REMEDIATION_REQUIRED for older completed UI001/UI002 validation sources predating later integration changes, including current Identity context/model changes. These are recorded in governance/evidence/p2-006-proof-20261108/native-gates.json and governed TODO-TUN-P2-006-EVIDENCE001. Preserve historical acceptance; use applicable RV lineage and current browser/UI regression evidence rather than rewriting old validated SHAs. This integration-evidence refresh joins the remaining acceptance work and does not stop eligible local P2-006 development.
