# Contributing to Tunner Platform

Tunner Platform is in P0, the Governance & Development System milestone. Product-plane work (P1–P8) is blocked until the P0 enablement gate passes.

## Before changing code or documentation

1. Read [AGENTS.md](AGENTS.md), the current work item, and the smallest sufficient authority records in `docs/authority/`.
2. Confirm the work item is eligible and record material ambiguity as `INSUFFICIENT_CONTEXT` rather than inventing requirements.
3. Keep a change limited to one cohesive, reviewable vertical slice.

## Branches and commits

`main` is protected. Do not push directly to it, force-push it, or delete it.

Use a short-lived branch whose prefix reflects the change:

- `feat/TUN-123-short-description`
- `fix/TUN-123-short-description`
- `change/TUN-123-short-description`
- `hotfix/TUN-123-short-description`
- `docs/TUN-123-short-description`

Use cohesive, buildable commits when practical. Recommended commit form:

```text
feat(scope): concise change summary

Work-Item: TUN-123
Milestone: P0
Authority: <applicable authority record>
```

Commit messages support traceability but never replace work-item, authority, or evidence records.

## Pull requests

Every normal change reaches `main` through a pull request. The repository automations create an eligible PR and enable GitHub auto-merge after a branch push. A required `CODEOWNERS` review and all protected-branch requirements remain mandatory.

Before requesting review, complete the pull request template, link the governing work item, identify authority and decision impact, and attach applicable evidence. Do not mark an unverified check as passed.

## Quality and security

Run the tests and static checks required by the work item. Document any test that is intentionally user-run, unavailable, or blocked, with the reason and recovery path.

Do not commit secrets, credentials, private keys, production data, or generated local configuration. Route security-sensitive, compliance-sensitive, financial, contract, and deployment changes through their required role reviews and evidence gates.

## Authority and decisions

The approved authority order is defined in [AGENTS.md](AGENTS.md). Repository documentation and automated summaries do not override approved product decisions, authority, or contracts. Material changes to authority, public contracts, security policy, or governance require the applicable decision or amendment record.