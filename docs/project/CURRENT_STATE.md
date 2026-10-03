# CURRENT_STATE

## Baseline and milestone

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002 + AMD-0003 + AMD-0004 + AMD-0005.
- Authority mirror: `tunner authority verify` passes for 99 hash-checked authority artifacts, including AMD-0004 and its recorded Drive provenance.
- Milestone: P0 is `CLOSED`. Its read-only `tunner governance milestone close-check P0` returned machine-readable `PASS` after every declared work-item final gate, P0 role review, evidence reference, closed TODO/defect, governance-schema, and authority-integrity check passed.
- Integration: P0 was integrated into protected `main` by PR #10 at merge commit `7bf72b9`. This remains a governance milestone outcome, not a production-release claim.
- P1 is ACTIVE. TUN-P1-001 through TUN-P1-011 are DONE locally with their required scope-specific validation, evidence, and specialist reviews. TUN-P1-006 through TUN-P1-011 remain technical-only foundations: their original boundaries and limitations are unchanged, and they have not been represented as Product Acceptance. TUN-P1-020 corrected the lifecycle control so an explicitly evidence-backed `product_acceptance_required: false` declaration can move only from VALIDATION to DONE; all other work retains the declared next-state lifecycle path.

- Resolved governance lifecycle defects: DEF-TUN-P1-006-001 through DEF-TUN-P1-009-001 are CLOSED by TUN-P1-020. Its functional tests prove the direct technical-only path, the preserved Product-Acceptance path, and rejection when no explicit exception exists.
## AMD-0005 authority application

- AMD-0005 is active and GLOBAL. The authority mirror was re-read from canonical Drive on 2026-10-03 and now hash-verifies all five active amendments. TUN-P1-021 is DONE: its implemented-state conformance audit corrected only confirmed governance/context/documentation drift, preserved valid P0/P1 foundations, and added no Product behavior. The previously recorded live-container test-evidence gap is remediated: the PostgreSQL/Redis Testcontainers suite passed 8/8 with `TUNNER_RUN_CONTAINER_TESTS=1`.

## AMD-0004 authority application

- AMD-0004 is active. P0 remains CLOSED and its historical close evidence is preserved. P1 now has a capability-oriented backlog and focused technical-primitives reuse audit; no generic library or Product behavior was added by the planning change.

## Locally validated foundations

- TUN-P0-001 through TUN-P0-015, TUN-P0-019, and TUN-P0-026 through TUN-P0-035 are all `DONE` with required evidence and final gates passing.
- AMD-0003 is active. The shared C# and PowerShell repository-root resolvers support explicit root, `TUNNER_REPO_ROOT`, Git top-level, and marker-walk resolution. The user-run relocated-copy proof passed after the no-Git fallback validation repair.
- The P0 close command now verifies all declared work items, their final gates, milestone evidence, declared milestone roles, and open P0 TODO/defect records without mutating any state.

## Completed conformance audit

- The full P0/P1 implemented-state audit is complete. P1-010 remains DONE and aligned; P1-011 remains DONE with selective gRPC activation trigger NOT_PRESENT. Authority Resolution Passes now distinguish the seven genuine missing-contract/selected-UX blockers from the non-blocking gRPC trigger absence. No authority conflict, security finding, portability violation, unapproved financial behavior, or prohibited generic helper library was found.

## Exact next authorized action

- P1-010 is DONE as a technical-only foundation under ADR-0002, ADR-0004, and Documents 01/07/08/09/15. It supplies a Platform SignalR adapter with authenticated policy-gated BFF mapping, server-derived scope resolution, explicit browser-origin/rate-limit hooks, and payload-minimal authoritative-resynchronization hints. It adds no BFF host exposure, Product-facing SignalR, Redis backplane, domain hint category, or authoritative realtime state.

## 2026-10-02 validation remediation and P1-010 refinement

- DEF-TUN-P1-003-001 is CLOSED: the transactional-outbox static guard now verifies the single OutboxMessage mapping without rejecting later independently governed shared-context mappings. Live static validation, governance validation, and architecture tests passed.
- P1-010 validation is complete: solution build had zero warnings/errors, the static source guard passed, and all 14 Platform architecture tests passed. BFF host exposure, identity/session behavior, domain resource policy, client UX/retry, domain payloads, Redis scale-out configuration, and Product-facing SignalR remain separately owned.
- P1-011 is complete as a protocol-eligibility audit. Its activation trigger is `NOT_PRESENT`: no authorized browser feature has a material gRPC-Web need and no component has a justified separate-process native-gRPC boundary. No gRPC package, proto, endpoint, generated client, or generic scaffold was added; future adoption requires its applicable feature/process ADR.
