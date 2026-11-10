# Optional V04 addendum — choose an eligible authenticator

**UNAPPROVED OPTIONAL PROPOSAL.** This addendum is separate from the approved 008-V01–V05 packet. It does not amend that consent or implement a production picker. The original packet files/hashes are checked unchanged in `rendered/checks.json`. Open [the pictures](index.html).

The public scenario contains two currently eligible active authenticators. It enables review of explicitly supported multiple-factor runtime profiles without selecting a universal factor count or making enrollment imply mandatory MFA. One eligible factor retains the already approved flow. Zero eligible factors uses the existing unavailable/changed handling. More than one eligible factor presents this chooser, with no initial choice and no automatic first-item selection.

## Approved pattern reused

A bounded read-only Figma/inventory pass found the actual approved shared chooser, Platform UX `98:28` (`step-up-card`, 500×410) and authenticator choice row `98:35` (444×64), already owned by `COMP-STEP-UP`. Live `get_metadata` and high-fidelity `get_design_context` with screenshot were read in session `57ee9061-53a9-40a6-817c-8e57e5a43203`; Figma was not changed. Existing actual approved reference image: `artifacts/p2-007-passkeys-20261109/ui-review/reference/figma-98-28.png`. Method-list reference `36:2` was also visually inspected. No dedicated approved multiple-TOTP-factor naming/picker contract was found in these bounded references or central inventory.

The proposal reuses the current `ModalDialog`, `.step-up-choice` button/chevron row, approved field/button components, shared typography/colors, and existing login shell/glows. Desktop proof rows remain 444×64; metadata wraps and row height grows on mobile. The card fits its content because two factor rows, explanation and selected-method context replace the original method-type chooser. The login instance uses its existing 440px card width and responsive padding. Reference screenshots are evidence only, never implementation assets.

## Exact optional consent scope

Approve or decline this additional V04 adaptation: when the server returns more than one currently eligible TOTP factor for **this exact pending login or sensitive operation**, show one button row per factor using the approved chooser pattern. Each row displays the proposed transient localized label **Authenticator app {position}** and a trusted added/activation timestamp. Clicking a row explicitly selects that factor and opens the existing code detail with the same label/time and **Choose a different app**. Changing the selection clears the code and returns focus to the chooser heading. No factor ID is shown as a label, name, URI, ARIA value or HTML data attribute.

Pictures cover `proof-picker`, `proof-selected`, `login-picker`, `login-selected` at 1440×1100, 390×844 and 320×844. Selected pictures are captured after an actual explicit click on the second row, not by automatic initialization. A 320px interaction check chooses the second app, enters a nonfunctional public code, returns to the picker, then chooses the first app and verifies an empty field. The proof example also checks Tab wrapping, Escape and trigger focus restoration. No real code, seed, QR, API or native authentication is involved.

The explanatory copy **“Numbers identify these options. They are not names in your authenticator app.”** makes the transient presentation clear. Amber PREVIEW SCENARIO notes and watermarks are review annotations, not production content. The proposed labels are neither persisted canonical nicknames nor claims about the third-party app name/device. A missing or ambiguous published presentation must not be replaced with an invented nickname or raw identifier. Exact timestamp labels and formatting remain part of this optional review.

## Exact safe projection needed

Current proof options expose `totpAuthenticators: Guid[]`; MFA options expose `{authenticatorReference}`. The chooser needs the same safe metadata shape in both protected contexts:

```text
authenticators: [{
  authenticatorReference: opaque ID used only for the selected POST,
  version: current factor lifecycle version,
  state: ACTIVE,
  createdAt: trusted timestamp,
  activatedAt: trusted timestamp for the verified activation,
  displayPosition: bounded positive integer in this response/selection context
}]
```

The safe **Added** time in these public pictures stands for a trusted server-projected display timestamp. Root should bind that slot explicitly to the owning metadata meaning (prefer the verified `activatedAt` for an active factor) and its published label before implementation; the frontend must not relabel a creation timestamp as a verified activation. A published `totp.selection.optionLabel` template plus a reusable integer formatter supplies the transient label. The shared date formatter renders the trusted timestamp with locale and a visible timezone. Public fixture dates/EST strings in this prototype are not production values.

`displayPosition` is scoped to the current safe list; it is not stored as a credential alias, used as authorization, sent instead of the opaque factor reference, or inferred from a raw identifier. Stable internal ordering may use opaque IDs as a backend tie-breaker without displaying them. Render keys bind to `authenticatorReference`, never the array index. After refreshed eligibility/version/context changes, clear the selection/code and ask for an explicit current choice; never silently shift an option number to another factor. Missing eligibility or a stale selected version follows the existing changed/unavailable state. The backend rechecks current source, actor, policy and exact operation at execution; frontend display state is not authority.

Potential equal timestamps do not justify a canonical public alias. The localized option number distinguishes the current rows while the real timestamps provide context. This is the audience-oriented transient pattern proposed for root/Product review; no new public naming/domain rule is established.

## Implementation boundary after consent

Use one chooser component inside the existing shared operation-proof detail and login-MFA screen, with identical metadata/selection/clear-code mechanics. It is local interaction state under the existing caller route or `/login/verify-authenticator`; no additional screen pathname is proposed. Reuse the current typed code schema and published error/summary/focus controls. On proof, POST the selected `authenticatorReference` with the original request/attempt. On MFA, use the protected original pending context and request. Read-only unknown-result reconciliation never chooses another factor or resubmits a code automatically. No Admin/Support seed access or secret-store path is introduced.

Proposed new content slots are `totp.selection.heading`, `.proofDescription`, `.loginDescription`, `.optionLabel`, `.added`, `.numberExplanation`, `.selectedDescription`, `.change`. They require genuine published provenance and exact approved wording; the synthetic ContentProvider in this artifact is not publication evidence. Existing verification/actions/content keys remain reused.

Native RV006 controls closed shared UI/inventory ownership. This optional component would require its own inventory/source binding, added consent reference and applicable VAL/UX evidence before readiness. It does not select policy, broaden the earlier five-scope consent or relax backend checks. Do not implement it until the root resolves this pictured addendum.

## Local checks and limits

`rendered/checks.json` records 12 rendered state/viewport cases, layout/runtime/identifier checks, Axe results and two targeted interaction cases. Automated incomplete contrast checks remain recorded. `contrast.json` records measured actual text pairs. These are local public-proposal checks only, not native, route, session, production or real-secret acceptance. Original 118 packet files were checked against their frozen provenance hashes and remained unchanged.

Reproduce with the existing local `../serve.mjs`, then `node artifacts/p2-008-totp-20261110/ui-review/optional-v04-factor-picker/capture.mjs` and `build-review.mjs` from `<repo-root>`. No package installation or new dependency is needed.
