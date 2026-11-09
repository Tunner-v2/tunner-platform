# P2-006 read-only UI authority review

Review date: 2026-11-08 (America/New_York). Executing-agent review; no Product approval, UI implementation or visual gate result is inferred.

## Verified references

FRD17 section7 authorizes fresh operation-bound verification; section8 retains server-owned session authority. The current UI matrix and AMD-0008/0009 govern visual fidelity, shared validation and URL behavior. Both Platform UX pages were inspected read-only through the Figma connector: Login (0:1) and Account Center (7:5). No Figma or Google Drive edit was performed.

The approved Account Center overlay exists at [98:27](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=98-27), with card98:28. Its design context and screenshot were returned and inspected. It uses a1440x1100 backdrop and a500x410 white card with18px radius, #dbe3f0 border, approved shadow, Inter typography and the heading "Confirm it's you". Its choices are Passkey (98:31), Authenticator (98:35), Other method (98:39), and Cancel (98:42). This is existing design authority, not a missing entire step-up screen.

The password field pattern already exists in Login2:41/2:43; change-password overlays46:7 and52:273 provide further current-password patterns. Registration validation70:1257 remains the shared error reference. The reviewed step-up overlay does not itself specify the password-entry detail beneath Other method or its error/pending states. Reusing a password field alone does not approve a material new composition.

## Next authorized implementation action

Prepare the password-detail composition from those actual components, first review any matching child/state authority, and request scoped Product Owner consent if the resulting composition is a material visual adaptation. Keep the existing approved step-up overlay as the base. Do not remove or rename its choices as production UX without the required approval. Passkeys/TOTP remain their own governed work items; the current backend implements only password proof mechanics and does not assert that password always meets a sensitive operation's assurance requirement.

Implement the shared guarded BFF challenge/resume context and reusable UI within TUN-P2-006. Derive actor/session/operation authority on the server, keep password/proof/context secrets out of URLs, preserve safe origin context, use the existing Tunner form capability, and retain the owning action's idempotency/version contract. This overlay can remain a same-screen interaction; any independently navigable screen requires a canonical guarded pathname and inventory entry.

Then run the applicable URL/VAL/UX requirements and Playwright rendered comparisons at matching viewports, including keyboard, error, pending, expiry, cancellation and responsive states. Actual configured approved assurance-policy evidence remains required before affected production paths are enabled. This backend receipt grants no UI acceptance and does not complete P2-006.

The local design-context retrieval aid is artifacts/p2-006-assurance-20261108/figma-readonly/step-up-design-context.txt. The source Figma node remains authoritative; this note is not a replacement or an amendment.

## AMD-0005 resolution pass and scoped candidate (2026-11-08)

VERIFIED196 active authority artifacts; TUN-P2-006 is actionable. Reused FRD17§§7–8, catalog32 private schema/version rules, current UI matrix and AMD-0008§4/AMD-0009 rather than requesting new business authority. Fresh read-only design context/screenshot checks confirmed overlay98:27 with chooser98:31/35/39 and current-password overlay46:7. Other method has no password-detail composition in this reviewed step-up frame. Login2:41/2:43 and validation70:1257 supply existing control patterns; password-change composition rules are not copied into reauthentication. No matching approved password-detail/state composition was established by these references or the earlier page inventory. A scoped visual consent is therefore needed for that adaptation, not a new FRD or security policy.

Concrete draft review packet: artifacts/p2-006-assurance-20261108/browser-review/review.html, with approved Figma reference and desktop/mobile proposed states. Three exact scopes: chooser availability adaptations, password-detail composition/copy, and shared interaction/responsive states. This candidate is not production UX and approval is not inferred. Independent BFF mechanics continue while awaiting scoped consent. Neither Figma nor Drive is edited.
