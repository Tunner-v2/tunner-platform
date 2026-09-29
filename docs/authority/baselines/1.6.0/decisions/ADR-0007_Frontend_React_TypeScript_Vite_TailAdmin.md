# ADR-0007 — Frontend Runtime: React + TypeScript + Vite + TailAdmin React

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Implement separate Tunner User and Admin/Support web applications with React, TypeScript, Vite and TailAdmin React. Share governed design tokens/components through deliberate packages; do not create a shared browser business-state authority.

## UX rule
Interfaces are guided, contextual workflows rather than database-style CRUD. Figma is a reference; development documentation and domain contracts remain authoritative.

## Revisit trigger
A documented product/runtime requirement materially invalidates this stack.
