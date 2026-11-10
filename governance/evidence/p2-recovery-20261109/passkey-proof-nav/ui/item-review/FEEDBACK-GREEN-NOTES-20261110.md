# Feedback correction evidence

Production correction: `b0c04424136aace1b02c486baeaafd53ed61b8ec`.
Final test-only wait correction: `745f9c2dc1b9060c4be0318b33b4019ae77e9e36`.
The latter changes three waits to the actual foreground dialog. Every original operation counter, binding, storage and action assertion remains intact. Application/content/backend inputs are unchanged by that test-only commit.

| Executed cohort | Actual result | Receipt directory |
|---|---|---|
| Focused feedback regressions, Edge | 8 passed, exit 0 | `20261110-feedback-green-b0c0442-focused` |
| Full primary/proof/navigation/feedback, Edge | 60 passed, exit 0 | `20261110-feedback-green-745f9c2-edge` |
| Same full cohort, Firefox | 60 passed, exit 0 | `20261110-feedback-green-745f9c2-firefox` |
| Existing shared verification | 22 passed, exit 0 | `20261110-feedback-green-b0c0442-step-up` |
| Existing authentication | 21 passed, exit 0 | `20261110-feedback-green-b0c0442-authentication` |
| Existing registration | 24 passed, exit 0 | `20261110-feedback-green-745f9c2-registration` |
| TypeScript no emit | Exit 0 | `20261110-feedback-green-745f9c2-checks` |

Each browser directory contains actual `results.json`, `exit-code.txt`, log, source SHA and screenshots. These are component tests using existing synthetic BFF/native-method fixtures. The focused eight are repeated within the full sixty; they are not eight additional distinct cases. Shared/authentication receipts retain their actual b0 source because the later commit touches only the passkey proof spec. No skipped/flaky cases appear in the final cohorts.

The earlier regression-first run remains unchanged at `20261110-feedback-red-312af88`: seven failed, one passed. The initial broad b0 run remains unchanged at `20261110-feedback-green-b0c0442-edge`: 59 passed, one failed, exit 1. Its snapshot proves that a page-level Continue setup wait returned while the foreground proof was still pending; the final dialog-scoped waits resolve that timing defect without weakening the assertions.

`20261110-feedback-states-745f9c2/provenance.json` binds eight fresh actual React/CSS captures to source hashes, published fixture copy, viewport, computed styles, geometry, focus and axe reports. Add error, Remove error, Login HTTP error and native cancellation were each captured at 1440 and 320 pixels. All eight were visually inspected. Automated scans reported zero violations and geometry/focus findings were empty.

- Remove desktop feedback is 500×376; actions end at y=668 inside the dialog ending at y=700. The normal review remains 500×330, independently asserted by the regression.
- Add/Remove unknown Close immediately focuses the visible enabled Check status action at both widths. The original immutable request remains unresolved; no replacement mutation is enabled by the focus correction.
- Login HTTP error and known native cancellation initially focus Sign in with passkey. The separate uncertain-result regression focuses Check sign-in status and verifies exact read-only reconciliation without another assertion. The aborted-route regression verifies that a late ceremony does not take focus from the next screen.
- Shared `FormErrorSummary` is applicable under VAL-006 for U-AUTH-05, COMP-PASSKEY-REVOKE and COMP-PASSKEY-SIGN-IN. Field-specific VAL-001/002 N/A remains separate. The cancellation notice is a status, rather than an error alert.
- UX-FID-002 remains applicable: Account/modal flat surfaces and backdrop are recorded; Login retains both loaded approved glow SVGs. Registration forms retain their own owning-screen glow evidence. An absent illustration does not exempt flat-panel styling.

Binding visual references remain Figma Add 46:24/52:291, Remove 46:44, Login 2:4, field/error 70:1257, shared proof 98:28, with approved V03/V04/V06 and the existing V01–V06 consent receipt. No external source was edited and no new Product approval is claimed.

`FEEDBACK-GREEN-ITEM-ADDON-20261110.json` supplies exact case titles, report/image hashes and 17 per-item check replacements. The original `ITEM-EVIDENCE-MAP-20261110.json` and failure addendum are preserved. Retained earlier screenshots keep their own source dates; no prior render or native result is relabeled as a fresh run.

All owned Vite helpers and browser runs ended. No application dist, .NET build, staging, commit, backend, governance, Figma or Drive mutation was performed by this validation pass. Unrelated dirty paths were preserved. Root owns portable copying, current native browser execution, formal UI defect closure and governance gates. The retained pinned Windows WebKit Strict-cookie failure remains a separate recorded engine limitation; no assertion or security policy was waived. Native backend 224 remains bound to predecessor 0ef, with targeted 14 at 80a and current equivalence handled separately by root.
