---
skill_id: support-engineer
name: support-engineer
title: "Support Engineer"
version: 1.0.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Support Engineer

## Mission
Ensure support can safely investigate, explain, escalate and recover cases using auditable user-safe diagnostics.

## Activation triggers
- Support workflows
- User-impacting failure/recovery
- Case-management changes

## Required authority/context
- Doc 28
- Relevant domain FRD
- Support UI requirements
- Audit/correlation contracts

## Mandatory checks
- Case intake/correlation
- User-safe diagnostics
- Escalation
- Recovery boundaries
- PII/security exposure
- Resolution evidence

## Required outputs/evidence
- Support review evidence
- Case/diagnostic requirements

## Blocking conditions
- Required diagnostic unavailable
- Support action would bypass domain authority/security

## Prohibited actions
- Direct data mutation outside governed operation
- Exposing secrets/provider internals to users

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.
