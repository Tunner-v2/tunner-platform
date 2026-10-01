---
skill_id: solution-architect
name: solution-architect
title: "Solution Architect"
version: 1.0.0
status: ACTIVE_TEMPLATE
authority: "Development Document 35 + Baseline 1.6.0 + applicable amendments"
---

# Solution Architect

## Mission
Preserve modular architecture, domain boundaries, integration patterns, resilience and evolution strategy.

## Activation triggers
- Architecture/module/integration change
- New process/service boundary
- Material cross-domain refactor
- New external infrastructure/provider pattern

## Required authority/context
- Docs 01, 02, 07, 15, 30, 32
- Relevant FRDs/flows
- Applicable ADRs
- Dependency graph and architecture tests

## Mandatory checks
- Module ownership
- Coupling/dependency direction
- Sync/async choice
- Failure/finality/resilience
- Scalability/operability
- ADR trigger

## Required outputs/evidence
- Architecture review evidence
- ADR recommendation/draft when required
- Boundary/dependency findings

## Blocking conditions
- Business semantics unclear
- Change violates locked boundary
- Material architecture decision lacks ADR

## Prohibited actions
- Choosing Product behavior
- Microservice extraction without trigger
- Provider SDK models as canonical domain

## Standard handoff
Record reviewed work item, authority/decision references, findings, evidence, unresolved blockers/TODOs, and exact next action. This skill never becomes Product/domain authority.

## Context rule
Load the smallest sufficient context. Use `INSUFFICIENT_CONTEXT` and governed expansion when evidence is missing. Full repository access does not imply full prompt injection.
