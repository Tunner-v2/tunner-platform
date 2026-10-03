> **Tunner Development Authority Amendment**  
> **Status:** APPROVED / ACTIVE  
> **Effective date:** 2026-10-02
# 37 — Current Authority Index: AMD-0004 effective overlay

## Effective authority

`Locked Documentation Baseline 1.6.0 + accepted ADR/PDR records + AMD-0001 + AMD-0002 + AMD-0003 + AMD-0004`

`AMD-0004_Shared_Technical_Primitives_Formatting_and_Canonicalization.md` is `APPROVED / ACTIVE` effective `2026-10-02`.

It establishes many-to-many flow-to-code traceability, capability-oriented reuse of deterministic technical mechanics, and the boundary between domain/business semantics, technical canonicalization, and presentation formatting. It does not reopen P0.

## P1 execution rule

P1 work must use a dependency-eligible, capability-oriented backlog. A flow identifier is traceability authority, not a requirement for an isolated implementation stack. Before adding a formatter, parser, normalizer, canonicalizer, or similar mechanic, the implementing work item searches existing BuildingBlocks, Platform capabilities, owning-module capabilities, frontend shared packages, and applicable framework capabilities.
