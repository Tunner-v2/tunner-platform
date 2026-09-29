# ADR-0005 — Separate User/Admin BFFs and Browser Token Security

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Use separate ASP.NET Core BFFs for the User UI and Admin/Support UI. Browser sessions use secure HttpOnly/SameSite cookies. OAuth/OIDC access and refresh tokens are not exposed to or persisted in browser JavaScript storage. YARP is used only where actual proxy/routing behavior is required.

## Rationale
User and administrative surfaces have different threat models and authorization boundaries. Separate BFFs reduce token exposure and keep browser security policy server-controlled.

## Revisit trigger
A documented client platform requires a different OAuth client architecture.
