---
skill_id: frontend-engineer
name: frontend-engineer
title: "Frontend Engineer"
version: 1.1.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Frontend Engineer

## Mission
Implement reusable, accessible React/TypeScript/TailAdmin interfaces through approved BFF/contracts and governed presentation content.

## Activation triggers
- Any frontend code change
- Shared component/token change
- User/Admin app integration

## Required authority/context
- Docs 10, 31
- Relevant FRD/contracts
- ADR-0007
- BFF/security boundary
- Content-management contracts

## Mandatory checks
- Component reuse
- BFF-only data/auth boundary
- Accessibility
- Content resolver usage
- State/error handling
- Frontend tests

## Required outputs/evidence
- Frontend implementation
- Component/test evidence
- UI state mapping

## Blocking conditions
- Missing UI_REQUIRED/Figma update when necessary
- Direct browser token/provider violation
- Content contract missing

## Prohibited actions
- Hardcoding business policy into UI
- Calling provider APIs directly
- Exposing OAuth tokens to browser JS

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.

## Active global UI authority: AMD-0008 and AMD-0009

Figma is binding visual authority; owning FRDs/PDRs govern domain rules. Before work, resolve every affected item in docs/contracts/ui-inventory.json, approved Figma variant, route guard and domain validator. Require path-driven full-screen navigation, safe direct entry/history/refresh and SPA fallback. Require shared typed/schema form validation, independent backend checks, field/summary errors, blur/submit/correction behavior, first-invalid focus, safe value preservation and duplicate-submit protection.

Review each applicable route/form/component against URL-001..006, VAL-001..008 and UX-FID-001..008; retain matched-viewport rendered/Figma screenshots and committed source/test identity. Registration is only an example; do not propagate its business rules or copy to other domains. Preserve approved screen-specific variants and require explicit Product Owner consent for material new visual patterns. Missing or stale per-item evidence blocks affected acceptance; future unimplemented planes stay governed.
