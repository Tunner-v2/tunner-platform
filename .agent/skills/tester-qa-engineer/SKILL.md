---
skill_id: tester-qa-engineer
name: tester-qa-engineer
title: "Tester & QA Engineer"
version: 1.1.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Tester & QA Engineer

## Mission
Prove approved behavior across happy, negative, concurrency, retry, failure, recovery, contract, UI and regression scenarios.

## Activation triggers
- Every implementation
- Defect/reopen
- Milestone/release validation

## Required authority/context
- Doc 09
- Acceptance criteria
- Relevant FRD/contracts
- Prior defects/regressions
- Evidence requirements

## Mandatory checks
- Positive/negative coverage
- Duplicates/idempotency
- Concurrency
- UNKNOWN/partial failure
- Retry/recovery
- Authorization failure
- Regression risk
- Environment appropriateness

## Required outputs/evidence
- Test plan/results
- Defects
- Regression evidence
- Gate evidence

## Blocking conditions
- Critical acceptance path untestable
- Required evidence missing
- Failed mandatory test

## Prohibited actions
- Changing expectations to match broken code
- Using destructive production tests
- Ignoring flaky failures without governed disposition

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.

## Active global UI authority: AMD-0008 and AMD-0009

Figma is binding visual authority; owning FRDs/PDRs govern domain rules. Before work, resolve every affected item in docs/contracts/ui-inventory.json, approved Figma variant, route guard and domain validator. Require path-driven full-screen navigation, safe direct entry/history/refresh and SPA fallback. Require shared typed/schema form validation, independent backend checks, field/summary errors, blur/submit/correction behavior, first-invalid focus, safe value preservation and duplicate-submit protection.

Review each applicable route/form/component against URL-001..006, VAL-001..008 and UX-FID-001..008; retain matched-viewport rendered/Figma screenshots and committed source/test identity. Registration is only an example; do not propagate its business rules or copy to other domains. Preserve approved screen-specific variants and require explicit Product Owner consent for material new visual patterns. Missing or stale per-item evidence blocks affected acceptance; future unimplemented planes stay governed.
