# Audit append and correlation foundation

Work item: TUN-P1-014. This technical implementation contract uses approved authority; it does not introduce an AMD or Product rules.

## Authority and ownership

Document 28 section 9 defines the append-only AuditEvent family and integrity checks. Document 32 sections 1, 2 and 4 authorize implementation field schemas and govern operation, correlation and authenticated actor context. Documents 02 and 32 preserve logical database ownership; AMD-0004 requires reuse. Document 29 keeps data classification, retention policy, holds and privacy behavior with the owner.

The owning application supplies an already-authorized server-derived actor, action/result meaning, stable event/operation/correlation identity, applicable resource/version and approval references, and explicit data-classification and retention-class references. Reference presence does not validate authorization, approval or legal policy. No HTTP endpoint, audit search, investigation, approval workflow, retention duration, deletion job or business enum is added.

## Executable contract

Use the existing Tunner.Database assembly, with a cohesive Audit capability and the existing PostgreSQL migration model. No project/package/service or shared-everything layer is needed. Repository search found no compatible audit append implementation.

AuditEvent is immutable and snapshots reference collections. It contains event ID/time, actor principal/type/identity references, action/operation/correlation and optional causation, target resource/version references, permission/scope/policy versions, optional reason, step-up/approval references, before/after version references, provider/evidence references, result, classification and retention-class reference. Owner semantics remain opaque. Required identity fields cannot be empty; optional references cannot be blank when supplied. Times are serialized as explicit UTC instants. No arbitrary object/payload field is available.

The versioned envelope has deterministic field serialization. Integrity format sha256-envelope-v1 covers the exact UTF-8 envelope bytes. A stored record verifies the digest and event/time/operation/correlation/classification/retention projections against its envelope. The envelope preserves the exact original UTC timestamp; the PostgreSQL timestamp projection is explicitly truncated to microsecond precision and checked at that same precision. This is an integrity check, not a signature or protection from a privileged administrator rewriting both data and digest. Production key management, independent anchors and owner-specific retention controls are not approved by this local foundation.

EfAuditAppender stages only new records on the owner's configured DbContext. Staging is not durable success. The owner commits through its governed transaction. An existing EfTransactionalOutbox callback can stage the owner write and audit event before the atomic outbox save/commit; no parallel transaction coordinator is introduced. Caller-supplied context must contain the audit mapping and belong to the authorized owner boundary.

The forward-only migration adds audit.events, stable event-ID uniqueness, operation/correlation lookup indexes and database triggers rejecting UPDATE, DELETE and TRUNCATE. These prohibit in-place changes through ordinary DML, including raw SQL. Corrections append new events through the owning contract. A future approved retention disposition requires separately governed owner implementation; this primitive imposes no legal deadline or purge behavior. Migration history remains intact.

## Tests and reviews

Tests cover metadata and reference snapshots, required/optional validation, UTC stability, correlation preservation, exact-envelope integrity and projection mismatch, database round-trip, duplicate identity, ordinary DML immutability, concurrent same-ID append, and atomic owner/audit/outbox commit and rollback. Real PostgreSQL tests apply all forward migrations to disposable Testcontainers databases with random ports. Existing architecture, transactional outbox, unit harness and migration guards provide regressions. A deliberate corruption test only disables guards inside its disposable test database and must detect the change.

Preimplementation reviews assess this contract and test plan only. Final Full-stack Engineer, Solution Architect, Compliance Specialist, Tester/QA and Auditor reviews must inspect the committed implementation and executed evidence. Codex records engineering reviews; these are not independent human, legal, Product or production acceptance.

Commit all owned behavior and a PENDING validation envelope, validate that exact SHA, then record passing evidence and review findings. Only then check the final lifecycle gate. Technical-only completion records validated/tested SHA separately from metadata-only closure SHA. Original P0 closure and P1-003 historical operator evidence remain preserved.

