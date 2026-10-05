# P1 Cloudflare R2 Object Storage Refinement

## Authorized boundary

TUN-P1-009 may establish a cohesive Platform-owned, provider-neutral IObjectStorage contract and a deterministic Cloudflare R2 adapter seam. It may move opaque bytes to or from an object store only when a later owning module supplies the authorized object purpose, metadata, policy, and lifecycle semantics.

## Reuse and provider assessment

The repository contains no object-storage capability. The baseline explicitly selects an S3-compatible IObjectStorage contract and Cloudflare R2 as the primary provider, with the official AWS SDK for .NET S3 client confined to the provider adapter. The Cloudflare R2 .NET guide currently documents an AWS SDK v3 configuration and R2-specific streaming-SigV4/checksum caveats; generic AWS SDK v4 guidance does not itself establish R2 compatibility. The bounded P1 adapter therefore uses centrally versioned AWSSDK.S3 3.7.510.8, the documented v3 line; a provider-client upgrade requires fresh R2 compatibility evidence.

## Required safeguards

- Domain modules depend only on IObjectStorage; Cloudflare/AWS types do not cross into Product, public, persisted, or owner-domain contracts.
- The R2 adapter must accept an injected narrow provider seam so deterministic tests require neither an R2 account nor live credentials.
- Provider behavior must be limited to object operations verified in R2's current compatibility documentation.
- Any R2-specific request flags are adapter mechanics, not universal object-storage semantics.
- Object-storage provider metadata, ETag, lifecycle state, retention setting, and location are never authoritative Product truth.

## Deferred decisions

Later governed work must select configuration and secrets delivery, account/bucket identifiers, endpoint and jurisdiction, bucket provisioning, lifecycle/lock policy, encryption, object versioning, content type/size/malware policy, retention/legal hold, presigned access, authoritative PostgreSQL object metadata, observability, and deployment behavior.

## Explicit exclusions

No Cloudflare account or bucket, token, credential, runtime configuration, endpoint, region, jurisdiction, upload/download API, presigned URL, user/admin experience, content/retention policy, domain object ownership, PostgreSQL schema, deployment, or release behavior is authorized by this refinement.
