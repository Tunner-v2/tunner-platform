# ADR-0006 — Identity Foundation: ASP.NET Core Identity + OpenIddict

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Use ASP.NET Core Identity as the Tunner account/authentication foundation and OpenIddict for OAuth/OIDC authorization-server/protocol capabilities. Google and Microsoft are the first external-login adapters.

## Required first-class capabilities
Verified email, password authentication, passkeys/WebAuthn, TOTP authenticator-app MFA, recovery codes, session/device management, step-up authentication, external account linking, revocation and auditability.

## Boundaries
External identity providers never become Tunner account authority. Provider subjects/claims are normalized to Tunner-owned account-link records and must obey Tunner lifecycle, recovery, security and audit rules.

## Revisit trigger
A future requirement cannot be met safely by this stack or licensing/security posture materially changes.
