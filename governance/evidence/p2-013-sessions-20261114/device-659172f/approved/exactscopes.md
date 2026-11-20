# Device UI: bounded Product Owner consent proposal

NON-PRODUCTION. Synthetic review data and proposed publication copy only. No live request, authentication, policy/default selection, browser persistence, production UI implementation or Product consent is included. The pending bulk packet remains a separate approval and is unchanged.

Open `review.html`. The toolbar switches local examples. Four principal proposed captures: `desktop-devices.png`, `mobile-devices.png`, `desktop-remove-trust.png`, `mobile-remove-trust.png`. Details, exact-result example, unconfirmed, loading and error are interactive examples without further screenshot expansion.

## Exact proposed scopes

| Scope | Reused approved composition | Bounded proposed adaptation requiring consent |
| --- | --- | --- |
| D01 — observed-device list and details | Device list16:125, current16:128, other16:134, explanation16:158; detail36:296, hero36:402, facts36:409, trust36:421. Expanded47:220/duplicate52:459 remains a reference, not a new technical assumption. | Guarded canonical `/account/devices` and `/account/devices/:reference` are route proposals only. Replace personal device/OS/location/trusted-since claims with safe reference, qualified BrowserFamily, first/last observed, current-session observation, recorded/effective trust and evaluated time. Distinguish browser-installation observation from physical or hardware identity. No location, IP, OS, personal names, fabricated inventory totals or familiarity verdict. |
| D02 — Remove trust confirmation and outcome | RemoveTrust36:427 is distinct from Signout36:429. Reuse modal46:44/duplicate52:314, desktop500×330, outlined Keep trusted and destructive Remove trust. | Exact selected reference and browser observation; truthful selected preserve-sessions effect. Remove trust does not itself sign out. Replace the unconditional future-extra-verification promise with the supported effect and conditional identity-verification guidance. Exact causal result and unconfirmed-result examples use the same shared dialog. Check this request does not create a new removal. No Sign out substitute or physical-device action. |
| D03 — shared states, focus and responsive layout | Existing local Account shell, fonts, tokens, fields/error/focus/loading patterns and shared buttons/cards. | Proposed390px wrapping/stacking, contained modal scrolling when needed, focus containment and Escape restoration, inert background, loading/error examples. No new form: shared typed validation remains mandatory for any later applicable form; no per-screen/native validation substitute. |

These are bounded consent requests for the listed adaptations, not approval of every device/session state, production policy, collection permission, assurance configuration, URL/VAL/UX evidence or acceptance. Separate specific consent is required before production implementation. Devices and Sessions remain different domain concepts; the approved navigation group name does not merge them.

## Source and evidence boundary

Backend source inspected at `87a16fc`: `src/Modules/Identity/DeviceContracts.cs` defines DeviceListingItem with DeviceReference, Version, TrustVersion, ObservedForCurrentSession, FirstObservedAt, LastObservedAt, Characteristics, RecordedTrustState, EffectiveTrustState and EvaluatedAt. BrowserFamily is the explicitly qualified example characteristic supplied by the existing synthetic profile. Device entries are created after successful authentication under Flow40; browser correlation neither establishes hardware identity nor authenticates a person. The current application supports only the explicitly qualified preserve-sessions trust-removal effect. No production collection profile, criteria, retention or assurance is selected by this packet. Session revocation preserves session history; it is not DeviceTrust removal.

Cached16:2 list screenshot,46:44 screenshot/code and the existing Account styles were reused without edits. The cache lacked Device details; one focused read-only high-fidelity36:296 call (Figma session `ad76c2d1-a2e6-43e4-8360-e3c31969d4d8`, skill `figma-design-to-code`) returned the detail reference and screenshot. `approved-device-details.png` is that actual reference, used only for comparison. No external file, Figma, FigJam or Drive content was changed. No screenshot substitutes for implementation markup.

References: [approved list](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=16-125), [approved details](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=36-296), [approved Remove trust](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=46-44).

## Validation and limitations

`render.mjs` reuses the existing installed Playwright/Edge local capture mechanism, not an app service or new framework. `render.json` records actual1440/390 viewport layout checks for eight local states, four capture paths, font/image geometry checks, modal action visibility/focus/Escape and browser disposal. It also verifies the existing bulk packet file hashes remain unchanged during capture. Network requests are blocked and recorded. This is artifact QA, not backend execution or a live guarded-route test.

Long device lists, actual protected route reload/history, screen-reader testing, real localized content, full keyboard navigation across future components, production qualification and URL-001..006/VAL-001..008/UX-FID-001..008 evidence remain outside this packet. No runtime-ready, full-feature acceptance, release approval or Product approval is claimed. Exact next action: obtain scoped Product Owner consent for D01/D02/D03 before any production device UI work.
