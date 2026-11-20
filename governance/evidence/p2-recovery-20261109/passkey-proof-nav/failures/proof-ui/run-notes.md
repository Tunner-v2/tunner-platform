# Actual focused proof UI result

Source candidate: `39afa3221aedf6da4cdcde95ba256f4d86bb95b4`.

Executed `step-up-passkey.spec.ts` through the existing passkey Playwright config in Edge with synthetic BFF and browser credential-method fixtures. Final exit **1**: **20 passed, 2 failed, 0 skipped, 0 flaky**, duration 43.4 seconds. `results.json`, `playwright.log`, `exit-code.txt`, and both failure snapshots are retained unchanged. This is component/mock UI evidence, not native cryptographic or end-to-end acceptance.

Both failures read counters before the async continuation had finished. Their completion wait used the page-level `Continue setup` button, already visible behind the shared modal. The captured snapshots show that background button and the still-busy native proof modal. The original expected counters and binding checks are unchanged. The proposed correction scopes only those two completion waits to the active dialog's `Continue setup` control. That correction requires its own committed anchor and executed rerun; this failed receipt remains a failure.

The three feedback render cases passed at 1440, 390, 320 px and attached automated axe reports with no violations. Actual 1440/320 screenshots were visually inspected: shared approved overlay, focus heading, wrapping actions and bounded mobile width are retained. Automated scans do not establish complete accessibility or Product acceptance.

Broader primary, navigation and legacy regression suites were not started after this focused failure. No production source or content was changed to address these two test waits. Exact next action: root anchors the corrected spec, then rerun the 22 focused cases before broader validation.
