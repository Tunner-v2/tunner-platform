# ADR-0002 — Redis for Distributed Cache and SignalR Backplane

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Use Redis 8.x through StackExchange.Redis / ASP.NET Core distributed-cache abstractions for non-authoritative distributed caching, protection counters, safe ephemeral coordination, and SignalR scale-out when multiple web nodes require it.

## Boundaries
Redis must never be the sole authority for identity, billing, subscription, payment, invoice, ledger, approval, audit, commercial history, or other durable business state. Redis loss must cause cache misses/degraded realtime behavior, not loss of business truth.

## Revisit trigger
Licensing, operational requirements, or a measured need for another compatible/cache technology.
