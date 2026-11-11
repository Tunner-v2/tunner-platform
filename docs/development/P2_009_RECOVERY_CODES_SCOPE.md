# P2-009 recovery-code issuance, replacement and consumption

Status: REFINEMENT. Native work-start and refreshed isolated context must permit implementation. P2-001–007 were audited; P2-008 remains VALIDATION/READY, with its technical revisions closed. This feature is unfinished.

## Authority and ownership

FRD17 sections6/7/10/13/15/16, catalog32, ADR-0006, current NIST Rev4 guidance, FigJam38/42 and PDR-0011 govern this Identity-owned primitive. AMD-0008/0009 govern the existing Account Security screen, recovery card and one-time modal. The Product Owner actually approved009-L01–L03 and one optional download from that same volatile view; the consent does not choose production policy. Drive/Figma/FigJam stay read-only.

Reuse ASP.NET Identity/native .NET cryptographic primitives, the existing User BFF, exact-operation proof, persisted current Account/session authority, local Identity transaction/outbox and shared typed form/dialog/navigation/content capabilities. No public code-validity oracle, separate identity host, generic credential framework, plaintext escrow or duplicated proof engine. Native default plaintext recovery-token storage and its approximately47-bit default generator are unsuitable unchanged for the planned saved-code contract.

## Required end state

1. Generate a complete distinct set under a reviewed supported random/encoding/verifier/work profile. Supported saved-code profiles account for at least64 random bits after formatting/canonicalization. No new dependencies or copied cryptography. Production count/format/work/profile values are explicit trusted inputs, not Figma samples or fixture defaults.
2. Require current fresh exact-operation authorization for Generate/Replace. Serialize/revalidate current source, expected resource/policy/version, replacement plan and effective intervals at final execution time. Proof consumption, verifiers, old-set effects, safe owner result and security outbox commit atomically.
3. Store only purpose/owner/set-bound native salted verifiers and safe operation/lifecycle metadata. Individual use and set lifecycle/version remain distinct. No redeemable values in Identity tokens, database/DP/vault escrow, events, logs, evidence, URLs, browser persistence, telemetry, or Admin/Support.
4. Only the winning confirmed-commit request may return its transient first-delivery value. Discard on rollback/unknown outcome; duplicate/status/reload/history/another tab cannot re-reveal or regenerate. HTTP completion does not prove user receipt/display/save. The browser can display once and request one local file from that same current payload; clear it on close/departure. No automatic download, clipboard or silent retry.
5. Consume one code in a trusted current Identity recovery-operation context, independent of an already-held strong session. Verification, final currentness, individual one-way consumption and dependent recovery-owner mutation/result/outbox share the exact same transaction. Reject arbitrary/inactive/foreign transactions. Owner rollback restores all provisional effects; concurrent different operations cannot consume twice. Same-operation result recovery never turns a consumed code into a new proof.
6. P2-011 owns complete recovery precedence and eventual session/factor/replenishment effects; P2-016 owns notification delivery. Preserve explicit safe intent/lineage needed by those owners. Do not infer that code acceptance mints a session, that a notification was delivered, or that expiry physically purged data. Production lifecycle and current dependency-patch dispositions remain governed inputs/followups before release.
7. Reuse `/account/security`; no independent new page is required. Refine U-AUTH-06 as a traceable same-screen component with parent ownership, canonical route guards and shared controls under P1-018-RV007. The approved disclosure composition and all existing consumers stay faithful. Complete current server-selected impact/publication is required before replacement continuation; no UI-selected business effects.

## Implementation sequence and technical review

Implement the cohesive domain/persistence primitive and meaningful tests first, then the existing proof/BFF winning-response projection, then the approved UI integration. The concrete shared first-delivery seam, supported profiles, final consume currentness and private-download QA boundary require technical review before their affected code; they are engineering refinement, not an invented Product decision. Use only complete labelled finite synthetic profiles for local tests. No Docker image work, production activation or external publication.

## Acceptance and evidence

The37 proposed security cases remain NOT_EXECUTED. Require actual native/core, PostgreSQL concurrency/rollback/currentness, bypass/authorization and secret-output tests; all password/passkey/TOTP completion paths; lost response/duplicate/cancel/refresh/history; requested-download cleanup and fault canaries; shared-consumer regression; per-item URL-001..006, VAL-001..008 and UX-FID-001..008 evidence, rendered/reference screenshots, keyboard/accessibility and native UI gate. Public fault qualification precedes private-value tests. TOTP no-download controls remain intact; any recovery-only file admission must be one exact positively owned path with private bounded verification and cleanup.

Commit all owned source before candidate validation, bind actual test SHAs and complete final role reviews. Startup PASS is not final evidence. Keep validation PENDING until actual required acceptance gates pass. Record branch/commit, exact owned paths, failures, blockers, owner inputs and next action. Preserve unrelated13 and the unknown Windows-cache contents boundary.
