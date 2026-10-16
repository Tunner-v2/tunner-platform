# P2 Account & Identity refinement

This implementation plan derives from the existing verified Account/Identity FRD (document 17), ADR-0006, PDR-0007, current contract catalog 32 and traceability document 34. It adds no Product semantics and does not replace source authority. Identity maps to FigJam sections 36–45; flow-specific implementation must resolve the applicable approved flow/UX before coding.

P1 is a completed, accepted local engineering milestone. Its original protected PR #11 merged after human review; acceptance/closure metadata is in follow-up PR #12. AMD-0002 permits local P2 work against validated P1 work items while integration proceeds. No production deployment or formal release tag is claimed.

## Bounded work items

| Work item | Scope | Authority | Local prerequisites |
|---|---|---|---|
| TUN-P2-001 | Authority-backed refinement and frozen backlog snapshot | 11 preparation gate; 17; 32; 34 | TUN-P1-001 |
| TUN-P2-002 | Calendar-safe adult eligibility and derived evidence | 17 §11; PDR-0007 | TUN-P2-001 |
| TUN-P2-003 | Account and Identity persistence/framework foundation | 17 §§2–3,6; ADR-0006; 02 §§3–4 | TUN-P2-001, TUN-P2-002, TUN-P1-002, TUN-P1-003, TUN-P1-013 |
| TUN-P2-004 | Registration and verified-contact activation vertical slice | 17 §§3–4,11,13–16 | TUN-P2-002, TUN-P2-003, TUN-P1-017, TUN-P1-018, TUN-P1-019 |
| TUN-P2-005 | Authentication and server-managed BFF sessions | 17 §§6,8,13,15–16 | TUN-P2-003, TUN-P2-004, TUN-P1-012, TUN-P1-019 |
| TUN-P2-006 | Versioned assurance and operation-bound step-up | 17 §7; 32 command and policy contracts | TUN-P2-003, TUN-P2-005, TUN-P1-012, TUN-P1-013 |
| TUN-P2-007 | Passkey registration authentication and revocation | 17 §§6–7,13,15–16; ADR-0006 | TUN-P2-003, TUN-P2-005, TUN-P2-006 |
| TUN-P2-008 | TOTP enrollment verification and disable lifecycle | 17 §§6–7,13,15–16 | TUN-P2-003, TUN-P2-005, TUN-P2-006 |
| TUN-P2-009 | Recovery-code issuance replacement and consumption | 17 §§6–7,10,13,15–16 | TUN-P2-003, TUN-P2-005, TUN-P2-006 |
| TUN-P2-010 | Google and Microsoft federation and explicit linking | 17 §§3,5–6,13,15–16; ADR-0006 | TUN-P2-003, TUN-P2-004, TUN-P2-005, TUN-P2-006 |
| TUN-P2-011 | Proof-hierarchy Account recovery workflow | 17 §§10,13–16 | TUN-P2-003, TUN-P2-005, TUN-P2-006, TUN-P2-007, TUN-P2-008, TUN-P2-009, TUN-P2-010 |
| TUN-P2-012 | Verified-contact change with candidate proof and uniqueness | 17 §§4,7,13–16 | TUN-P2-003, TUN-P2-005, TUN-P2-006, TUN-P2-011 |
| TUN-P2-013 | Session device listing trust and revocation | 17 §§2,8–9,13,15–16 | TUN-P2-003, TUN-P2-005, TUN-P2-006 |
| TUN-P2-014 | OpenIddict Product authorization and revocation contracts | 17 §9; 19 Product/client contracts; 32 §10 | TUN-P2-003, TUN-P2-005, TUN-P2-006, TUN-P2-013 |
| TUN-P2-015 | Account restrictions and closure with owner-domain blockers | 17 §§3,7,12–16; 18 and 29 owner-domain contracts | TUN-P2-003, TUN-P2-005, TUN-P2-006, TUN-P2-011, TUN-P2-012, TUN-P2-013, TUN-P2-014 |
| TUN-P2-016 | Identity post-commit notification and audit integration | 17 §14; 27 notification contracts; 32 event envelopes | TUN-P2-003, TUN-P2-004, TUN-P2-005, TUN-P1-003, TUN-P1-006, TUN-P1-007, TUN-P1-014 |
| TUN-P2-017 | P2 end-to-end security privacy and UX conformance | 17 §§1–16; 09 testing gates; 08 security; 29 privacy | TUN-P2-002, TUN-P2-003, TUN-P2-004, TUN-P2-005, TUN-P2-006, TUN-P2-007, TUN-P2-008, TUN-P2-009, TUN-P2-010, TUN-P2-011, TUN-P2-012, TUN-P2-013, TUN-P2-014, TUN-P2-015, TUN-P2-016 |
| TUN-P2-018 | P2 acceptance and integration handoff | 03 lifecycle; 09 milestone gates; 17 acceptance | TUN-P2-017 |

## First implementation boundary

TUN-P2-002 owns the internal Accounts eligibility capability: the exact approved calendar-date rule, trusted server evaluation date, policy/evidence/correlation provenance and derived outcome. Raw DOB must not be retained in that outcome. Underage returns the approved AGE_INELIGIBLE outcome. This slice creates no Account, authenticated session or Product Relationship and publishes no endpoint. It is followed by the framework/persistence and registration slices rather than an unbounded Identity implementation.

## Preparation coverage

The frozen backlog snapshot maps all 16 FRD sections and all 11 FRD acceptance cases to owned work items. It covers state/resources, flows, data, APIs, OAuth/passkeys/MFA, contacts, sessions/devices, recovery, notification/audit, UI and acceptance. Detailed field schemas are created in the owning slice under contract catalog 32; they are not new business authority. Future records remain BACKLOG until their scope reviews, prerequisites, exact contracts, tests and evidence are ready.

## Boundaries and reuse

Reuse P1 authorization, concurrency/idempotency, effective-policy resolution, outbox/inbox, audit/correlation, BFF, error/OpenAPI and shared UI/content primitives. Accounts/Identity owns its business state. Product membership, roles, entitlements and local sessions remain Product-owned; Billing, Finance, Notification, Privacy and Security owners are consumed through approved contracts. Missing provider/policy/UX detail requires targeted expansion and AMD-0005 resolution before any decision request. Do not invent security lifetimes, risk values, retention rules, support overrides or new provider finality.

## Security follow-up

Actual GitHub image scans later found high/critical dependency findings in PostgreSQL gosu and other development images. P1-RV001/P1-RV001-001 records that release-blocking remediation and the absence of an effective required-status-check rule. Preserve severity thresholds and actual failures. Research candidates are not passing images; no scan exception, VEX waiver, image replacement or protection-rule change is approved by this planning record. This release scope does not block P2 authority refinement or isolated pure-domain work.

## Validation and recovery

TUN-P2-001-VALIDATION.json records the committed candidate and actual governance, role, coverage, dependency and context checks. The snapshot preserves the original plan while live work-item state evolves. Existing dirty docs/context/current, docs/project and .vscode changes remain excluded; fresh state is generated under artifacts/p2-transition-20261016. Repository role checks are executing-agent engineering reviews and do not impersonate human Product/security approval.
