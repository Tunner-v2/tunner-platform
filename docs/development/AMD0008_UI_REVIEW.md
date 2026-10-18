# AMD-0008 registration visual review

Status: scoped Product Owner approval recorded; synchronized Figma and implementation pending final committed-source validation. P2 milestone production UI gate remains BLOCKED.

## Authority and scope

AMD-0008 and current Docs00/10/12/31/33/35/36/37 are synchronized from Drive. Current FRD17/29/31, PDR-0011 and AMD-0007 retain business authority. Approved Figma controls visuals. No new Product business decision, authority waiver or Figma write is inferred from this audit.

The AMD-0005 resolution pass and findings are recorded in governance/evidence/AMD0008-UI-AUDIT.json. Original active-item alignment and previous committed validation remain preserved; closed P0/P1 lifecycle history is unchanged.

## Restored visual elements

Registration, verification and Ready retain all ten supplied SVG background/glow/separator/circle assets, verified byte-for-byte against fresh Figma exports. Placeholders, typography, card/control geometry, step badge, notes, notice copy layout, recovery/support/footer composition and approved outlined check glyph (70:696) are restored. Shared published-copy controls supply placeholders; the proposal remains DRAFT and browser fixtures explicitly simulate publication.

Reference screens: [Registration70:270](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=70-270), [Verification70:539](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=70-539), [Ready70:630](https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master?node-id=70-630). Matching viewport:1440x1024. Narrow review:320x1000.

## Explicit visual approval request

The image inventory and exact SHA256 values are in governance/evidence/ui/AMD0008/review-snapshot.json. Open the corresponding PNG files for reference/current/proposal comparisons. All data is synthetic.

1. Approve registration confirmation composition (197:5 within70:270), and Personal Information onboarding198:2 derived from approved normal form35:107, with names, required DOB and verified email. These functional requirements already follow AMD-0007; this request concerns their layout.
2. Approve the pictured consent-pending screen using existing shared controls and server-status action. This creates no new consent authority or policy.
3. Approve #475569 shared slate for placeholders and unchecked-checkbox border and retain the same footer contrast exception. The approved preview is after-register-contrast-proposal.png. The source now adopts the approved placeholder/checkbox color after direct Product Owner consent.
4. Approve reuse of current shared focus/checked/error/expired/recovery patterns and pictured 320px responsive adaptations, subject to final rendered and accessibility checks. No arbitrary visual-diff tolerance is requested. Loading/busy behavior remains functionally required; any material novel visual state also requires a reviewed design.

Reference contrast is2.56:1 for placeholder text,1.23:1 for the unchecked checkbox boundary and2.45:1 for footer links. Proposal #475569 is7.58:1 on white and7.24:1 on page background. Automated axe success does not close these manual findings.

AMD-0008 section4 requires explicit consent with screen/node, before/after, rationale, scope, approver, date and evidence. Generic proceed, silence and prior AI Figma edits are not consent. Approved changes must be synchronized to Figma before affected production UI is unblocked.

## Recorded consent

Direct user response: "Approve the four listed visual scopes". The exact question, scope, date, session approver identity and reviewed-snapshot hash are in governance/evidence/AMD0008-PRODUCT-OWNER-VISUAL-CONSENT.json. Synchronization is in AMD0008-FIGMA-SYNC.json; the new editable consent frame is207:2. Existing native date affordances and shared interaction/responsive patterns are approved exceptions, not new business policy.

## Gates and remaining owners

TUN-P2-004 stays VALIDATION; TUN-P2-004-UI001 is UI_REQUIRED. P2 now requires the existing native UI_VISUAL_APPROVAL gate at milestone close-check. It stays BLOCKED until all applicable visual evidence, consent, synchronization and reviews pass. Engineering build/tests never constitute visual or release acceptance.

Closed bootstrap AGENTS/role-schema/runtime/CI propagation is a separately governed Revision requirement; no global propagation completion is claimed. The active verified amendment and Doc35 already bind current work; UI001 owns tracking this remaining acceptance concern. Login2:4 exists; its GitHub vs FRD Google/Microsoft provider mismatch belongs to P2-005 refinement and needs its own precise visual proposal.

Content/legal publication, production policy/delivery providers, protected-main integration and release approval remain separate. Existing P1-RV001 release/security issues are unwaived.

## Resume

Record actual scoped Product Owner response and snapshot identity. If approved, synchronize only approved Figma changes, implement approved contrast/state adaptations, commit, validate exact source SHA, refresh role/evidence/context and re-evaluate the visual gate. Otherwise retain UI_REQUIRED and continue independently eligible work according to governance next. Never absorb pre-existing dirty context/project/editor files.
