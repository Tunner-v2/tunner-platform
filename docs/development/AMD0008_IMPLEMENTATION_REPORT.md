# AMD-0008 implemented visual scope

Validated source: ba81f0e3ccd92b2e1b9730c72cb37f0de489278c.

The approved registration, verification and Ready visuals now restore placeholders, typography, card/control/note geometry, notice/recovery/support/footer composition and the supplied background/glow layers. Personal Information and consent-pending layouts are synchronized to the direct Product Owner approved review. Shared contrast adjustments and interaction/responsive reuse are recorded as scoped approved adaptations.

Figma frames: Registration70:270, Verification70:539, Ready70:630, Personal Information198:2, Consent207:2. The new consent frame is editable. Original snapshot hashes, actual consent, synchronization and final rendered images are committed under governance/evidence/AMD0008* and governance/evidence/ui/AMD0008.

Validation:395 .NET,26 web and21 browser cases pass, with zero failures/skips. Builds, current dependency/security checks, authority verification and fresh BFF packaging pass. Final visual result is PASS_REGISTRATION_SCOPE at the tested source SHA. No arbitrary pixel-diff tolerance was applied.

P2 production UI acceptance remains BLOCKED pending the tracked bootstrap/role/schema/CI propagation revision and remaining milestone UI scopes. Existing release/protected-main blockers remain separate. See AMD0008_HANDOFF.md for exact next actions and preserved dirty-file boundaries.
