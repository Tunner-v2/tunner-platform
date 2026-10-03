# P1-012 technical command primitives

P1-012 provides reusable technical mechanisms only: server-derived actor context, fail-closed evaluator resolution, opaque owner-scoped idempotency, and deterministic expected-version comparison. It creates no Account, Product, Billing, Finance, Admin role, entitlement, resource policy, public endpoint, or persistence schema.

Authority resolution: Baseline 01 authorizes authorization/idempotency/expected-version primitives. Document 32 defines the server-derived principal command envelope, expected-version behavior, and canonical `FORBIDDEN_SCOPE` and `VERSION_CONFLICT` problem codes. AMD-0004 and AMD-0005 preserve technical reuse and owner-domain semantics.