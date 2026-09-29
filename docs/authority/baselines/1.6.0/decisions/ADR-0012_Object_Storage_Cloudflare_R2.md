# ADR-0012 — Cloudflare R2 Primary Object Storage

**Status:** ACCEPTED  
**Date:** 2026-09-21  
**Supersedes:** ADR-0003

## Decision

Keep the S3-compatible Tunner `IObjectStorage` contract and select **Cloudflare R2** as the primary object-storage provider. The .NET infrastructure adapter uses the official AWS SDK for .NET S3 client configured for R2.

## Scope

Large/binary artifacts, privacy exports, support attachments, generated retained documents, evidence packages, import/export files and other objects unsuitable for relational rows.

## Boundaries

- PostgreSQL remains authoritative for object ownership, domain linkage, SHA-256, size/media type, classification, lifecycle state, retention/hold intent and audit correlation.
- R2 object bytes never become hidden business-state authority.
- R2 is S3-compatible but not fully AWS S3 feature-equivalent. Only capabilities verified in Cloudflare's current R2 compatibility matrix may be used.
- Do not depend on AWS S3 Object Lock or AWS KMS semantics. Where retention enforcement is required, use approved R2 bucket-lock/lifecycle controls plus Tunner governance.
- R2 Location Hints are not residency guarantees. Use Jurisdictional Restrictions when an approved residency policy can be satisfied by an R2-supported jurisdiction.
- If a required jurisdiction cannot be guaranteed by R2, the Product/environment is blocked from activation until another approved `IObjectStorage` adapter satisfies the policy.

## Environment strategy

- deterministic fake storage is allowed only for unit/component tests;
- provider integration tests use dedicated non-production R2 buckets;
- Sandbox/Staging/Production use separate buckets and credentials;
- Production credentials/buckets are never shared with lower environments.

## Security

R2 access credentials are vault-managed and least-privilege/bucket-scoped. Presigned URLs are short-lived and operation-scoped. Uploads remain untrusted until content/size/hash/scanning policy passes.

## Revisit trigger

A residency requirement R2 cannot satisfy, a required storage capability not available in R2, unacceptable provider risk/commercial terms, or measured reliability/performance requirements.
