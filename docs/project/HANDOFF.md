# HANDOFF

## Work item

- ID: TUN-P0-019
- Milestone/Sprint: P0 / no sprint
- Status: VALIDATION (GitHub ruleset and code-owner review verified; safe auto-merge automation pending live validation)
- Branch: governance/b2-ruleset-validation
- Implementation commit: `5c1cdb6` (`ci: enable guarded pull request auto-merge`)

## Completed

- B0/B1 completed with 125/125 repository-mirror hash verification.
- Local remote `origin` is registered as `https://github.com/Tunner-v2/tunner-platform.git`.
- `.github/workflows/automate-pr-automerge.yml` enables GitHub native auto-merge for non-draft, same-repository pull requests. It does not approve, bypass, or execute pull-request code.

## Tests/evidence

- GitHub SSH host-key verification: PASS.
- GitHub SSH identity authentication: FAIL — `Permission denied (publickey)`.

## Decisions/blockers

- SSH authorization is not needed because HTTPS remote access is available.
- CODEOWNERS is materialized with `@Tunner-v2/ai-dev`.
- Repository ruleset 24217332 was reported configured; independent authenticated readback is pending.
- The repository owner must enable **Allow auto-merge** before the workflow can enable automatic merging. If Actions policy restricts tokens, it must also allow `contents: write` and `pull-requests: write` for this workflow.
- `EXT-DRIVE-001` affects external freshness comparison only.

## Context state

- Context mode: TASK.
- Authority mirror status: verified local mirror pending external check.
- Activated skills: governance-engineer, devops-engineer, cyber-security-engineer, auditor.

## Exact next action

- Merge and validate the auto-merge workflow on a protected same-repository pull request. Then resume TUN-P0-002, Git and contribution policy, under protected-branch governance.
