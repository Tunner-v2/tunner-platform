# Pre-P0 Implementation Package

**Status:** COMPLETE — READY TO MATERIALIZE DURING TUN-P0-001
**Authority:** Baseline 1.6.0 + AMD-0001 + AMD-0002 + AMD-0003 + AMD-0004 + AMD-0005

This package gives the AI developer implementation-ready instructions/templates before repository creation.

## Root
- `AGENTS.md`

## Agent Skills
20 role `SKILL.md` templates: full-stack-engineer, solution-architect, project-manager, ui-ux-engineer, frontend-engineer, tester-qa-engineer, financial-specialist, compliance-specialist, auditor, founder-product-owner, admin-operator, support-engineer, end-user-ux-reviewer, content-writer, business-analyst, sdk-engineer, governance-engineer, rd-engineer, cyber-security-engineer, devops-engineer.

## Governance Templates
milestone.template.yaml, sprint.template.yaml, work-item.template.yaml, revision.template.yaml, todo.template.yaml, defect.template.yaml, reopen.template.yaml, decision.template.yaml, amendment.template.yaml, gate.template.yaml, role-review.template.yaml, evidence-manifest.template.yaml, release.template.yaml, context-policy.template.yaml, authority-mirror-manifest.template.yaml, bootstrap-work-record.template.yaml, skill-registry.template.yaml, role-activation-policy.template.yaml, lifecycle-policy.template.yaml, dependency.template.yaml, pr-integration-policy.template.yaml, path-resolution-policy.template.yaml, P0_INITIAL_BACKLOG.yaml.

## Project Memory Templates
PROJECT_CONTEXT, CURRENT_STATE, DECISION_INDEX, RISK_REGISTER, HANDOFF.

## Bootstrap and Acceptance
Repository blueprint, bootstrap runbook, P0 DoR/DoD, fresh-agent test, context cost policy, GitHub bootstrap policy, completion checklist.

During TUN-P0-001 these are materialized into the repository, generated Git values are filled only when they exist, hashes/provenance are recorded, and lifecycle becomes governed. Do not redesign them during bootstrap without an authority conflict/implementation impossibility; use ADR/amendment.

Supporting authorities:
- https://drive.google.com/drive/folders/1sDBE-k3fdIKw2psi7Ul7Ty1u3yNj1FgH
- AMD-0001: https://drive.google.com/file/d/15MYKfh7zplOWoz80FvgPJOPi-Y6GfI7H/view
- Doc 35: https://drive.google.com/file/d/1Pp1rlvF20HIA-e_1AOd72esSxZlNISQj/view
- Doc 36: https://drive.google.com/file/d/1iIe-aRFQzJdaoLItpLx8tCIXZSCiVYzg/view
- Doc 37: https://drive.google.com/file/d/1c5Ni8LGgQ-2JxpacoPAopSCO-al23ut2/view


## Location-independent path rule
All repository-internal paths in this package are logical paths relative to `<repo-root>`. The checkout may live on any supported drive/directory. `.tunner-root` plus the governed resolver determine location at runtime; machine-specific absolute checkout paths are not committed.


## Authority-first decision-resolution rule
AMD-0005 requires proof of applicable-authority review before new decisions or missing-authority blockers are created. The decision/work-item/lifecycle templates in this package include the corresponding evidence/control fields.

AMD-0005: https://drive.google.com/file/d/1M_B4TYwf5gDKaZ-1w_gMR9Nse-Fs7B87/view

## Committed-state readiness clarification — AMD-0002

AMD-0002 continues to permit local/branch development while protected-main approval is pending, but repository-changing work must now reach an identified validated committed state before `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` or completed handoff.

The package templates record validated/tested commit identity, preserve unrelated/pre-existing dirty paths, and distinguish a validation-candidate commit from an optional later metadata-only closure/handoff commit. Behavior-affecting changes after validation invalidate readiness until recommitted/revalidated.

This prevents a dirty worktree from being treated as durable local validation while preserving AMD-0002's non-blocking merge model.

## Post-closure Revision lifecycle
Closed milestones/work items retain their historical closure. Later corrective/hardening/maintenance/improvement work uses append-only `RV` lineage such as `P0-RV001` or `P0-004-RV001`. Revision sequence numbering is monotonic and unbounded. `REOPENED` remains for the active acceptance/release lifecycle. An RV is work lineage, not an ADR/PDR/amendment and cannot authorize changed business/architecture/security semantics by itself.

