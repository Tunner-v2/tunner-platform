# ADR-0004 — Communication Protocol Boundaries

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
- Connected Product synchronous API: HTTPS REST/JSON + OpenAPI.
- Connected Product asynchronous delivery: signed/versioned HTTPS webhooks/events.
- Tunner-owned browser normal commands/queries: REST/JSON through the applicable BFF.
- Tunner-owned browser realtime: ASP.NET Core SignalR.
- gRPC-Web: selective, only where a documented Tunner-owned browser use case materially benefits from typed/binary/server-streaming behavior.
- Native gRPC: selective, only across a justified process/service boundary.
- Same-process modules: typed in-process calls by default.

## Prohibitions
SignalR and gRPC-Web are not the canonical public Connected Product API. SignalR is not business authority. Native gRPC must not be introduced merely to imitate microservices inside a cohesive process.

## Revisit trigger
A measured performance/streaming requirement or approved deployment-boundary change.
