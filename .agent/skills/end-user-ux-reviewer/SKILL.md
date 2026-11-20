---
skill_id: end-user-ux-reviewer
name: end-user-ux-reviewer
title: "End-user UX Reviewer"
version: 1.1.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# End-user UX Reviewer

## Mission
Review flows from the intended user task perspective for clarity, friction, trust and recovery while respecting authority.

## Activation triggers
- User-facing workflow/content change

## Required authority/context
- Relevant FRD
- UI requirements
- Figma reference
- Content copy

## Mandatory checks
- Goal clarity
- Progress/feedback
- Error recovery
- Terminology comprehension
- Trust/transparency
- Accessibility/friction

## Required outputs/evidence
- UX findings
- User-perspective acceptance evidence

## Blocking conditions
- Critical user path cannot be understood/completed

## Prohibited actions
- Weakening required security/compliance
- Changing business policy for convenience

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.

## Active global UI authority: AMD-0008 and AMD-0009

Figma is binding visual authority; owning FRDs/PDRs govern domain rules. Before work, resolve every affected item in docs/contracts/ui-inventory.json, approved Figma variant, route guard and domain validator. Require path-driven full-screen navigation, safe direct entry/history/refresh and SPA fallback. Require shared typed/schema form validation, independent backend checks, field/summary errors, blur/submit/correction behavior, first-invalid focus, safe value preservation and duplicate-submit protection.

Review each applicable route/form/component against URL-001..006, VAL-001..008 and UX-FID-001..008; retain matched-viewport rendered/Figma screenshots and committed source/test identity. Registration is only an example; do not propagate its business rules or copy to other domains. Preserve approved screen-specific variants and require explicit Product Owner consent for material new visual patterns. Missing or stale per-item evidence blocks affected acceptance; future unimplemented planes stay governed.
