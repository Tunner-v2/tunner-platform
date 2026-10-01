# Tunner Platform

Tunner Platform is being established under P0, the Governance & Development System milestone. Product-plane implementation (P1–P8) is blocked until the P0 enablement gate passes.

Start with [AGENTS.md](AGENTS.md). The verified, repository-local execution mirror is in `docs/authority/`; Drive remains the canonical external authority.

## Bootstrap status

- Effective authority: locked Baseline 1.6.0 + approved ADR/PDR records + AMD-0001 + AMD-0002.
- Authority bundle SHA-256: `6e2ab9f5d5f3f3d490c0428d8f2a997a3f2c7b40815a4728cf8d622999b1c712`.
- Canonical Drive readback: governing Development Documentation, Pre-P0, Decisions, and FRD/Authority folders are readable; the full source-to-mirror diff remains TUN-P0-033 scope.
- Current scope: P0 control-plane work only.
- Local Docker dependency foundation: [docs/development/LOCAL_DOCKER.md](docs/development/LOCAL_DOCKER.md). It is local/test-only; the credential-free P0-007 OpenBao foundation is documented in [docs/development/LOCAL_OPENBAO.md](docs/development/LOCAL_OPENBAO.md).

Do not treat this README as a replacement for the approved authority.