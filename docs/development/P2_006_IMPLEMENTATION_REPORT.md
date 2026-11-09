# P2-006 implementation and global UI compatibility report

Engineering tests pass at a2df3359f8092e1e816125fe384e7a8a86785c68; native acceptance gates must also pass before local validation is declared. Branch: codex/p2-006-assurance-20261108. No new PR is published.

## Authority and eligibility

Startup verified196 current authority artifacts. P0/P1 remain CLOSED and P2 ACTIVE; native next reported TUN-P2-006 eligible. Locked Baseline1.6.0, FRD17, catalog32, ADR0006, PDR0011/AMD0007 and AMD0008/0009 were applied. Existing FigJam/flow contracts and native context were resolved within the current work item. AMD0005 resolution and exact Figma reads are recorded in P2_006_UI_AUTHORITY_REVIEW.md. No external authority or Figma/Drive documentation was edited.

## Inventory and architecture

The central docs/contracts/ui-inventory.json now has current acceptance evidence for16 implemented/test-fixture items: seven existing User screens (Login, Registration, Email Verification, Personal Information, Consent Pending, Account Ready, Signed In), their four forms, two shared User/Admin field variants, the step-up component/form and its Development-only carrier screen. Planned future modules remain planned.

|Scope|Canonical route or interaction|Figma/authority|
|---|---|---|
|Existing User routes|/registration, /registration/verify-email, /registration/personal-information, /registration/consent, /registration/complete, /login, /signed-in; exact inventory is authoritative|Existing per-item references and prior scoped approvals retained|
|Identity verification chooser/detail|Same-screen dialog; no independent pathname|Platform98:27; current-password46:7; validation70:1257; three-scope consent|
|Isolated test carrier|/browser-testing/step-up; session guard, reload and history|TEST_FIXTURE; shared approved dialog; static local explanation|
|Shared controls|User/Admin fixtures; no invented Product routes|Existing approved field variants; Admin49:132 approved idle-border exception|

Actual existing route pathnames are asserted against the central inventory by current contract/browser tests. The table is a readable index; it does not replace inventory contracts.

One shared typed form/error capability and one native modal provide validation and keyboard focus. Current backend credential/session and strict bounded JSON primitives are reused. The explicit server owner derives actor/operation/version/context and applies its local mutation in the proof-consumption transaction. The browser sees no raw proof and cannot choose operation authority. Completion reconciliation prevents uncertain responses from blindly resubmitting.

## Findings and fixes

DEF-TUN-P2-006-UI-001 (UI_REQUIRED) is resolved by the exact direct Product approval. DEF-TUN-P2-006-UI-002 (UI_VALIDATION_DEFECT) records pointer blur moving Confirm before the click and modal focus/lifecycle issues; shared handlers and focus containment/restoration are corrected and retested. DEF-TUN-P2-006-UI-003 (UI_FIDELITY_DEFECT) records320px heading/action wrapping; current snapshots show the corrected approved responsive pattern. Existing URL routing regressions pass; no new UI_ROUTING_DEFECT remains in this scope.

Earlier original UI001/UI002 source evidence predated integration. New P2-004-UI001-RV001/P2-004-UI002-RV001 compatibility evidence validates exact changed ownership; P1-018-RV003 extends and revalidates shared primitives/inventory. Original DONE records and old validated SHAs remain unchanged. TODO-TUN-P2-006-EVIDENCE001 closes only after native revision/commit-readiness checks succeed.

## Changes and source

Exact source ownership: governance/evidence/TUN-P2-006-COMMITTED-SOURCE.json. Main changes are Identity assurance/proof contracts, persistence/migration, private BFF options/password endpoints, shared bounded JSON parser, shared form/native modal, approved chooser/password UI, local starter host/carrier, tests and central inventory. No dependency or package lock update was introduced for the dialog. Candidate ownership was committed before execution.

Implementation commits: ee316aa (BFF), dfd5846 (UI/local carrier),09efb5c and d141823 (shared focus/StrictMode/mobile corrections), a2df335 (inventory metadata only). Earlier private policy/proof commits and receipts remain in Git. Actual complete executable rerun occurred at d141823; a2df335 changed only the non-executable inventory host-reference classification and passed the typed31-check suite again.

## Executed validation

|Check|Result|Evidence|
|---|---|---|
|Identity/PostgreSQL and HTTPS backend|PASS147,0 fail,0 skip|results/identity.trx|
|Theme and typed contract/unit|PASS31|results/unit.log; results/inventory-unit.log|
|New dialog, five widths plus short viewport|PASS20|results/stepUp.json|
|Shared User/Admin controls|PASS16|results/shared.json|
|Existing registration routes/forms|PASS22|results/registration.json|
|Existing Login/Signed In|PASS16|results/authentication.json|
|Real local HTTPS/SMTP/PostgreSQL end-to-end|PASS3|results/real.json|
|Starter process/port/TLS/Production guards|PASS7|results/starter.log|
|Frontend build|PASS|results/build.log|

Evidence paths in this table are relative to governance/evidence/ui/step-up-20261109. Test index contains raw hashes, portable normalized artifacts and source provenance.147 backend cases include wrong binding/policy/session, replay/concurrent consumption, restrictions/expiry, strict malformed/injected JSON, rollback and no browser-visible proof. The real browser test loses a real committed success response and reconciles exactly once without another POST. Registration age/consent behavior remains unchanged.

## Visual and accessibility evidence

Current validated-review.html and visual-review.json contain approved references and actual screenshots. At1440×1100 the chooser is500×410 at470,300. Five widths1440/1024/768/390/320 plus short viewport, keyboard forward/reverse/Escape/restore, input error associations, first-invalid focus, pending duplicate protection and axe WCAG2.2 AA engineering tags pass. Automated accessibility checks are not a claim of universal conformance.35/42 prior captures are byte-identical; the remaining seven differ by at most2/255 per channel with no material layout drift found. Current native URL001..006, VAL001..008 and UX-FID001..008 evidence is per item, not borrowed from another form.

## Decisions, gates and handoff

No new Product decision is required for these three approved visual scopes. The separate owner Figma backfill remains recorded and read-only access is preserved. Production policy/content/owner configuration, final Product acceptance where applicable, protected-main merge, CI/security publication and release are separate gates. Monitoring image work remains stopped per user direction and open vulnerability findings are not waived.

Native revision closure/current UI/work-item gates must pass and be committed before the final handoff can claim local validation. Exact next action and gate receipts are in P2_006_HANDOFF.md. Preserve the13 unrelated dirty paths; their hashes are recorded in the current test index. Isolated context remains artifacts/p2-006-assurance-20261108/context.
