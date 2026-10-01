# CURRENT_STATE

## Baseline and milestone

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- Authority mirror: `tunner authority verify` currently passes for all 72 imported authority artifacts.
- Milestone: P0 is ACTIVE. P1–P8 remain blocked by the separate P0 enablement gate.
- Integration: local/branch development continues under AMD-0002; protected-main integration still requires approved human controls.

## Locally validated foundations

- TUN-P0-001 through TUN-P0-015 are locally validated where recorded; no item is represented as DONE before protected integration and P0 enablement.
- TUN-P0-026 is locally validated. PR #8 remains an integration-review concern only; it does not block eligible local P0 work.
- TUN-P0-027 is locally validated: deterministic role activation, structured review enforcement, and scoped blocking all passed their local gate. It remains unmerged; protected-main integration has not been requested or performed.
- TUN-P0-008’s local migration rehearsal is operator-confirmed against Tunner’s dedicated loopback PostgreSQL port 25432; its connection value was not retained in repository evidence.
- TUN-P0-013 has local security/supply-chain evidence only; remote CI scanning and all production claims remain outside the verified scope.

## Exact next authorized action

- Run `tunner governance next` and select the next eligible P0 control-plane item. P0-028 is expected to consume the locally validated P0-027 role matrix when its dependency is declared `LOCAL_VALIDATED`.
- Do not push or change protected `main` without explicit user direction.