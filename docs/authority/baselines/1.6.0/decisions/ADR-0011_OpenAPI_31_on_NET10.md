# ADR-0011 — OpenAPI 3.1 Normative Executable Contract on .NET 10

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
OpenAPI 3.1 is the normative executable HTTP contract for the .NET 10 baseline. OpenAPI 3.2 is a forward target and becomes normative only through a governed runtime/tooling upgrade.

## Rationale
The executable contract must match supported first-party/runtime tooling rather than forcing a newer specification label without equivalent toolchain maturity.

## Revisit trigger
Approved .NET/tooling upgrade with production-ready first-party OpenAPI 3.2 support.
