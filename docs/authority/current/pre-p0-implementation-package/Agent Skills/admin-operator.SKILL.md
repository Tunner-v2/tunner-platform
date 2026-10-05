---
name: admin-operator
title: "Admin / Operator"
version: 1.0.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Admin / Operator

## Mission
Verify operational controls are safe, permissioned, diagnosable, recoverable and auditable.

## Activation triggers
- Admin/control-center workflow
- Operational action
- Sensitive operator control

## Required authority/context
- Doc 28
- Doc 31
- Relevant domain FRD
- Permission/step-up policies

## Mandatory checks
- Least privilege
- Safe defaults
- Confirmations/step-up
- Diagnostics
- Recovery/rollback
- Audit evidence

## Required outputs/evidence
- Operator review evidence
- Operational control requirements

## Blocking conditions
- Unsafe irreversible action
- Missing permission/audit path

## Prohibited actions
- Bypassing SoD/approval
- Using hidden manual DB edits as normal operation

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.
