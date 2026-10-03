# P1-002 host-composition committed-state recovery

## Purpose

This record restores machine-verifiable committed-state evidence for `TUN-P1-002` without rewriting the historical mixed P1 foundation commit.

## Scope retained

The original `6af6791412c28133159c9a34b053e87bc7284d5f` change established the application-only solution, API and Worker composition roots, explicit Platform module registration, deterministic architecture tests, and the static host-composition validator. The recovery validates those same structural invariants on an isolated committed checkout.

## Deliberate exclusions

This recovery does not add a Product endpoint, identity/session behavior, persistence schema, BFF/UI, provider call, messaging topology, object storage, cache, realtime behavior, deployment claim, or any unrelated active-worktree path.

## Candidate validation

The candidate must pass authority verification, governance validation, `Validate-TunnerPlatformHostSource.ps1`, the Platform architecture tests, and bounded P1-002 context verification in an isolated checkout. Exact candidate and closure SHA metadata are recorded in the governed work-item and validation evidence after validation.