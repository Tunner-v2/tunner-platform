# CURRENT_STATE

## Baseline and milestone

- Effective authority: Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002.
- Authority mirror: `tunner authority verify` passes for all 72 imported authority artifacts.
- Milestone: P0 is ACTIVE. P1–P8 remain blocked by the separate P0 enablement gate.
- Integration: local/branch development continues under AMD-0002; protected-main integration still requires approved human controls.

## Locally validated foundations

- TUN-P0-001 through TUN-P0-015 are locally validated where recorded; no item is represented as DONE before protected integration and P0 enablement.
- TUN-P0-026 is integrated into protected main through approved PR #8.
- TUN-P0-027 and TUN-P0-028 are locally validated governance foundations.
- TUN-P0-029 is locally validated: adaptive CORE/TASK/EXPANDED/FULL_AUDIT selection, disposable repository indexing, explicit insufficiency escalation, budget failure, access-versus-prompt separation, and source freshness all pass locally.

## Exact next authorized action

- Run `tunner governance next` and select the next eligible P0 control-plane item.
- Do not push or change protected `main` without explicit user direction.