# P2-007 passkey UI review packet

Status: **PROPOSAL ONLY — Product Owner consultation/visual consent pending.** Local review artifacts only; no frontend implementation, source-document/Figma/FigJam write, Product approval, production configuration, or release acceptance.

Open `index.html` for the before/after gallery and interactive review harness. Every account/name/key/date/status in the prototype is a synthetic fixture. The review selector is outside the product composition. Reference PNGs are evidence only and are never used as implementation backgrounds.

## Recommendation and exact consultation

Recommended: preserve FRD17 business behavior and the approved Tunner composition, with only the six bounded adaptations below. The existing designs contain conflicts, so this recommendation is submitted to the user explicitly; it is not silently finalized or treated as approval. No age/DOB acquisition or rejection behavior changes. No architecture change, parallel credential store, custom verifier, alternate session authority, or new provider.

**Question for the Product Owner:** Do you approve P2-007-V01 through V06 as shown in this packet: retain current FRD17 behavior (no SMS sign-in factor, no repeat recovery-code reveal, and account facts only from the server), reuse the approved Add/step-up/success/removal visual language, add the passkey login action and truthful recovery states, and apply the illustrated mobile/contrast adaptations? Recommended: approve this bounded scope and manually reconcile the listed Figma nodes; any requested business-rule change follows the governed authority workflow. This is visual/conflict-resolution consent only, not release or runtime-policy approval.

## What was inspected

Authority read: root AGENTS.md; Frontend Engineer, UI/UX Engineer, End-user UX Reviewer, Content Writer and QA skills; `docs/development/P2_007_PASSKEY_SCOPE.md`; `governance/evidence/TUN-P2-007-AUTHORITY-RESOLUTION.json` and `TUN-P2-007-RD-SOURCES.json`; current FRD17; applicable portions of docs10/31/32; AMD0008/0009; current UI inventory; shared components, content/forms/routing, authentication and step-up source.

Read-only Figma file `apNbwvAgdn1xIxYH1oiaSC`, session `0329c4d8-fbee-45f9-a50c-d406f4711f34`: enumerated both actual pages (`0:1 Login`, `7:5 Account Center`), all Account Center top-level compositions and matching passkey/remove/feedback/step-up frames. Searched all Login-page text for passkey: no matching passkey action/state found. `70:270` is registration, not login; `2:4` is login. Inspected login-known-accounts/product-origin inventory; no passkey text found. No dedicated passkey remove/pending/error/unknown/mobile composition was found within these two pages. This is a bounded observed inventory, not a claim that an unrelated third-party library has no reusable patterns.

High-fidelity design context and screenshots: `13:2` account security; `36:2` methods; `46:24` Add; `46:44` remove-device confirmation; `46:116` success toast; `98:28` shared step-up; `54:81` mobile Account Center shell; `2:4` login; `70:1257` shared validation exemplar. Screenshot `52:291` confirms Add backdrop. FigJam `bdJwXt1U7n3iyluSD21OYK` section `88:6720` confirms authentication/assurance, enrollment, operation-bound proof and separate revocation. Text snapshots and PNGs are under `reference/`.

## Existing approved patterns — reuse without new visual invention

| Reference | Observed design | Reuse boundary |
|---|---|---|
| `13:2`, especially `13:129–131` | Account security, “No passkey added yet”, Add | The empty passkey row already exists. Do not claim it is missing. Render account status only from authoritative facts. |
| `36:2` | 1440×1100 methods page, 248px sidebar, 72px header, two-column 820/268 cards | Reuse shell, type, spacing, rows, badges and buttons. Conditional facts/rows require V01 reconciliation. |
| `46:24` / `52:292` | 520×410 Add; 12px radius; 472×112 device panel; 40px actions | Same-screen modal. Reference device text is sample context; a browser/device hint never proves credential location. |
| `52:291` | 1440×1100 backdrop; Add centered at 460,345 | Reuse pale backdrop `rgba(247,250,252,.82)`. |
| `98:28` | Existing 500×410 operation step-up card | Reuse `StepUpDialog`, including its existing password-proof flow and approved variants. No duplicate proof dialog. |
| `46:116` | 380×92 success toast | Exact generic copy and treatment are usable after the exact committed result only. No success toast after step-up alone. |
| `46:44` | 500×330 sensitive removal confirmation, red action | Reuse geometry/color/impact hierarchy; passkey wording and safe metadata are V04 adaptation. |
| `70:1257` / `70:1313` | Form error summary in Geist 13px semibold `#b91c1c` | Reuse shared `FormErrorSummary`; no native bubbles or independent error system. |
| `54:81` | 390px Account Center with 64px header, menu and stacked cards | Mobile shell exists; the passkey composition/dialog reflow itself remains V05. |

## Explicit bounded approval scopes

| Scope | Before / actual conflict or missing state | Proposed after and rationale |
|---|---|---|
| **P2-007-V01** | `36:134–139` recommends SMS despite FRD17 excluding phone as MVP factor; `36:146` offers repeat “View codes” despite one-time reveal; `13:111–114`, `36:113–116/130` assert strong/protected/MFA/sample state. “Passkey on this device” can misstate a synced/security-key credential. | Preserve current FRD; present only backend-supported methods and actual safe metadata/capabilities. No SMS factor or repeat reveal control. Use plain “Passkey” unless backend can substantiate device association. Reuse the page shell and rows; neutral header/guidance when protection facts are absent. The synthetic prototype shows only passkey/password fixture facts, not a full future MFA implementation. User explicitly consulted before affected production UI. |
| **P2-007-V02** | Login `2:4` and all Login text contain no passkey entry action. Current source has no passkey button. | Add one outlined “Sign in with passkey” action beneath the password action in the existing auth card. Retain approved glow assets/chrome and current previously governed provider presentation. The proposal’s provider labels are noninteractive context, not a new provider authorization. |
| **P2-007-V03** | Add `46:24` is a start state only; no passkey pending/cancelled/error/unsupported/policy/expired/unknown feedback variants observed. | Reuse Add panel and shared error summary/status text. No new icon/visual system. While native confirmation is active, prevent duplicate launch. Cancellation is neutral, not a frightening security error. A post-submission uncertain response retains the exact original request and blocks new mutations; status checking does not replay. Native browser/OS prompt remains native. |
| **P2-007-V04** | No passkey-specific management/removal confirmation or safe multi-key metadata state observed. | Adapt `46:44` to “Remove this passkey?” with safe selected-key metadata and accurate impact. Eligible alternatives/last-key rules remain backend policy. Do not promise sign-out, product-wide termination or credential erasure from the device. Shared step-up revalidates the exact chosen operation. |
| **P2-007-V05** | Mobile billing shell exists, but no passkey-specific mobile/tablet state observed. | Reuse 64px header/menu and stacked cards, maintain 16px dialog gutter, natural text wrapping, usable action wrapping and scroll within short viewports; 320/390/768/1440 checks. Desktop Add remains 520×410 and removal 500×330. |
| **P2-007-V06** | Figma `#94a3b8` small shell/metadata text on white is below 4.5:1; required focus/error/status semantics are not fully drawn for these passkey states. | Darken affected small text to existing `#64748b` (or `#475969` where needed), retain decorative borders and surfaces, and reuse existing visible indigo focus outlines/shared error text. Status has words, never color alone. Contrast adaptation is explicit consent, not an undocumented restyle. |

The candidate follows these six scopes. It is not asserted pixel-identical overall: V01–V06 deliberately alter only the stated states/compositions. Full production integration still needs matched-viewport evidence. All other style/asset differences must be assessed, not absorbed as approved by this packet.

## Proposed inventory / ownership refinement

| UI item | Kind / canonical owner | Guard and navigation |
|---|---|---|
| U-AUTH-01 passkey entry | Existing full screen `/login`, `2:4` | Existing anonymous/public sign-in context; preserve approved Product-origin return context, server allowlist/abuse controls. No credential/account enumeration. |
| U-ACCT-SECURITY (refinement proposal) | Full screen `/account/security`, `13:2` | Active authenticated User session and Account scope; server restriction/capability facts. Direct entry/reload/history reconstruct authoritative view. |
| **U-ACCT-03 Sign-in Methods** | Full screen `/account/security/sign-in-methods`, `36:2` | Active User session; strict ownership; current server method/capability bootstrap; unavailable data is not empty data. No sensitive URL params. |
| **U-AUTH-05 Passkey Setup** | **Modal**, owner `/account/security/sign-in-methods` or `/account/security`; `46:24`/`52:291` | Same-screen local modal is permitted by AMD9. Refresh closes unstarted modal; resumes/checks only server-authorized original pending operation. No separate full-screen route needed solely for this overlay. |
| COMP-PASSKEY-REVOKE (proposal) | Modal under methods owner URL; `46:44` adaptation | Selected owned credential/version; server eligibility, restrictions and exact protected operation rechecked before mutation. No key IDs in public URL. |
| COMP-STEP-UP (existing) | Modal in owning operation; `98:28` | Existing shared challenge/proof handling; not a navigation or general assurance bypass. |
| COMP-PASSKEY-RESULT (proposal) | Inline status/shared toast on owner screen | Consume exact server result, not another tab’s result or session-only evidence. No new result pathname for a toast. |

Existing `/registration` and authentication paths remain intact. These paths are proposed route-manifest entries, not newly published API contracts. The current UI inventory remains unchanged; this packet is its coordinated refinement input. Admin safe metadata is a separate scope; P2-007 adds no Admin secret/private-key UI.

## Backend / frontend state handoff

Candidate internal BFF endpoints communicated by backend: `/authentication/passkeys/options`, `/authentication/passkeys/sign-in`; `/account/passkeys` bootstrap/metadata plus enrollment/options, enrollment/complete, revoke and result. These are private coordination candidates; use the final typed backend contract, not this document, as source authority.

Enrollment: owner loads actual capabilities → review Add → request governed ENROLL protected operation → existing shared step-up → **step-up completion authorizes/creates pending ceremony only** → `navigator.credentials.create` → send permitted browser response → full framework verification and atomic persistence → exact result → refresh safe methods + toast. A cancellation/expiry after proof is not an active credential.

Primary sign-in: explicit gesture → native `navigator.credentials.get` → BFF verified assertion → existing restriction/abuse/assurance/session rules → confirmed authorized destination. No client policy/assurance upgrade, account existence disclosure or private material. Preserve password flow and existing guarded `/signed-in` behavior until an approved owner transition exists.

Revocation: select credential → review actual impact → server checks ownership/version/eligibility → existing REVOKE protected operation/step-up → atomic revocation on exact completion → refresh. Do not send a second unconditional mutation after the protected operation already completed it. Session side effects come from explicit approved security policy (`PRESERVE_EXISTING` or `REVOKE_CREDENTIAL_SESSIONS` as supported by the backend); the frontend must not choose one. Required impact disclosure is derived from that selected policy, using safe backend facts/content, before confirmation. The `remove` review state is explicitly a synthetic preserve-existing fixture; `removeEndSessions` is a separate synthetic revoke-credential-sessions fixture in which this current session is affected. Neither is a production default. Missing/unknown policy must block confirmation instead of substituting either disclosure.

If the confirmed revocation intentionally ends this current session, V03/V04 include the illustrated `revokedSignedOut` outcome: “This passkey was removed. Sign in again to continue.” Show it only with a proved exact terminal operation result, and route safely to existing `/login`. A generic expired/unauthorized session without that proof uses `sessionExpired`: “Your session has ended. Sign in again to continue.” It must not claim the removal succeeded or show the passkey-added toast. Session policy selection remains the P2-019 owner input; no policy default is proposed here.

Unknown responses: retain actor/session/action/original operation binding server-side; check the exact operation after network loss, reload, cancellation after submission, expiry or another-tab activity. Never replace it with a fresh operation ID blindly, infer success from any active session or unrelated key, or show another tab’s success. A definitive pre-submission native cancellation can safely say no passkey was added; all ambiguous post-submission outcomes stay pending until reconciled. Browser errors may conflate cancellation, timeout and no usable credential: use the safe `loginUnavailable` wording instead of falsely classifying or disclosing account/credential existence. The explicit cancelled fixtures apply only when cancellation is actually known.

## Source reuse and artifact limitations

Production should compose existing `ModalDialog`, `PrimaryButton`, `StepUpDialog`, `useTunnerForm`, `FormErrorSummary`, `ContentProvider`/published content and navigation hooks. Add one account-specific visual variant/token extension derived from these actual nodes where shared styles cannot express it; do not fork validation or domain state. Existing shared `ModalDialog` handles inert background, keyboard wrap and trigger focus restoration. No label field is introduced without a current domain contract; the proposal displays safe returned names/metadata only.

The local HTML is intentionally a synthetic review harness with local JavaScript state and no real credential/provider/session APIs. It uses existing shared stylesheet classes and token files plus scoped draft account geometry, and contains literal draft copy solely for review. It does not substitute for production React component reuse, governed content publication, backend integration, route implementation, WebAuthn/HTTPS/PostgreSQL tests or native/UI gate evidence.

Fonts are self-hosted only inside this artifact. Geist/Geist Mono came from the official Google Fonts OFL directories; Inter is copied from existing approved `@fontsource-variable/inter@5.3.0` dependency. Their OFL licenses and SHA256 provenance are retained under `assets/` and `provenance.json`. No dependency installation. Production should reuse current font dependencies and add only governed licensed assets if genuinely needed. No screenshot is embedded in `proposal.html`/CSS/JS.

## Manual source update instructions — no external writes performed

1. In Platform UX `36:2`, keep the shell, method-row hierarchy and card tokens; reconcile `36:134–139` SMS and `36:155–156` SMS guidance with current FRD17. Remove/hide unsupported factor UI; do not implement SMS to preserve old pixels.
2. Reconcile `36:141–146` backup-code treatment with one-time reveal/download. Do not offer repeat viewing of stored code values. Future replacement or status affordance must reflect that owner contract.
3. Reconcile `13:111–114`, `13:124–127`, `13:133–144`, `13:150–160`, `36:113–116`, `36:120–130`, `36:151–156` into explicitly named fact-driven variants. Keep synthetic sample facts clearly labelled in design documentation; add absent/unavailable variants. Do not interpret a passkey as blanket strong assurance.
4. Add V02 login action to `2:4` using existing outline/action tokens and preserved original glow assets. Retain previous approved provider/registration corrections, no DOB-first gate.
5. Duplicate/reuse `46:24`/`52:291` only as a manual design operation after consent for V03; add the exact states/copy listed here. Do not redraw browser/OS native prompts as Tunner UI.
6. Adapt `46:44` for selected passkey management/removal, safe identity/created/last-use/status metadata from server, and explicit no-extra-side-effect wording. Use `46:116` only for confirmed committed operations.
7. Add passkey mobile/tablet counterparts based on `54:81` shell; match the shown gutter/wrapping. Apply only V06 approved contrast changes and retain all other surfaces/borders.
8. Record Product Owner identity/date/scope/reference/approval evidence, version the manual design update, then update route/source binding and matched production screenshots. Approval is absent until actually supplied.

## Acceptance boundary and next action

See `acceptance-matrix.md` and `rendered/checks.json`. Isolated proposal rendering is not UI acceptance and inherits no PASS from the existing 16 UI items. P2-007 remains IN_PROGRESS with `UI_REQUIRED` / scoped `UI_VISUAL_APPROVAL` until actual consent and implementation evidence. Candidate defects to record during coordination: unsupported/misleading method/protection facts, missing state coverage, routing/validation/fidelity issues as applicable. No repository blocker or decision/amendment was created by this artifact agent.

Exact next action: root presents the six-scope question and gallery to the user, records the actual response, then coordinates source inventory/production UI only within the approved scope. Backend/security work can continue independently. Do not stage/commit this agent’s artifacts until root has reviewed ownership and final integration evidence.
