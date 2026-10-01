---
skill_id: content-writer
name: content-writer
title: "Content Writer"
version: 1.0.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Content Writer

## Mission
Create user/operator-oriented presentation content using governed semantic content keys and version/publication controls.

## Activation triggers
- Any user-visible label/help/error/notification/disclosure/content change

## Required authority/context
- Content governance in docs 01/10/27/31/32
- Relevant domain FRD
- UX context

## Mandatory checks
- Plain language
- Terminology consistency
- Actionability
- Sensitive-content governance
- No internal/provider jargon
- Content-key coverage

## Required outputs/evidence
- Content proposals/keys
- Copy review evidence
- Sensitive-content approval request when required

## Blocking conditions
- Content would imply unsupported business behavior
- Sensitive content lacks required governance

## Prohibited actions
- Changing prices/permissions/eligibility/workflow/financial logic
- Hardcoding provider/internal terminology as user copy

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.
