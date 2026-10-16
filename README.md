# Tunner Platform

Tunner Platform completed P0, the Governance & Development System milestone, and is now in the authority-backed P1 Platform Foundation milestone. `TUN-P1-002` completed API/Worker host composition and module bootstrap; `TUN-P1-003` is the next, separately governed transaction/outbox refinement. Product behavior remains governed by later scoped work items.

Start with [AGENTS.md](AGENTS.md). The verified, repository-local execution mirror is in `docs/authority/`; Drive remains the canonical external authority.

## Bootstrap status

- Effective authority: locked Baseline 1.6.0 + approved ADR/PDR records + AMD-0001 + AMD-0002 + AMD-0003.
- Authority bundle SHA-256: `6e2ab9f5d5f3f3d490c0428d8f2a997a3f2c7b40815a4728cf8d622999b1c712`.
- Canonical Drive readback: governing Development Documentation, Pre-P0, Decisions, and FRD/Authority folders are readable; the full source-to-mirror diff remains TUN-P0-033 scope.
- P0 is closed and integrated by PR #10; P1 is active with host composition only. See [P1 refinement](docs/development/P1_PLATFORM_FOUNDATION_REFINEMENT.md) and [local host composition](docs/development/LOCAL_PLATFORM_HOST_COMPOSITION.md).
- Local Docker dependency foundation: [docs/development/LOCAL_DOCKER.md](docs/development/LOCAL_DOCKER.md). It is local/test-only; the credential-free P0-007 OpenBao foundation is documented in [docs/development/LOCAL_OPENBAO.md](docs/development/LOCAL_OPENBAO.md).

Do not treat this README as a replacement for the approved authority.