# P1-001 current-authority recovery

## Purpose

This recovery artifact revalidates the completed `TUN-P1-001` refinement under the active authority set and AMD-0002 committed-state rule. It does not rewrite the historical `6af6791` commit, alter Product scope, or absorb the independently governed host-composition implementation that was interleaved in that historical change.

## Boundary

The original refinement remains the authority-backed decomposition in `P1_PLATFORM_FOUNDATION_REFINEMENT.md`. This recovery is limited to proving a current-authority, P1-001-only validation candidate:

- it keeps the original planning, ownership, later-plane, and no-Product-behavior boundaries intact;
- it records why `6af6791` cannot itself be certified as a cohesive P1-001 candidate;
- it requires authority verification, governance validation, role-review freshness, and deterministic governance tests against the exact candidate SHA; and
- it records `git_state` only after those checks pass in an isolated clean checkout.

## Explicit exclusions

This recovery adds no API, Worker, persistence, messaging, security, storage, UI, identity, provider, deployment, or Product behavior. It does not modify the separately active `P1_CAPABILITY_DECOMPOSITION_AND_PRIMITIVE_REUSE.md` work, and it does not alter historical commit content.

## Completion condition

`TUN-P1-001` may return to `DONE` only after the recovery candidate is validated against current authority, its mandatory roles record passing reviews for the recovery scope, and the work item plus validation evidence identify the validated/tested candidate SHA and remaining unrelated worktree paths.

## Portable validation baseline

The current-authority portability prerequisite is satisfied by the committed authority integrity and checkout-byte corrections through 476e84b. The next P1-001-only candidate must be verified from a fresh worktree against that baseline; this document remains limited to refinement recovery and introduces no application behavior.
