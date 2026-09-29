> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 05 — Versioning, Compatibility & Release Management

## 1. Principle

Tunner does not have one universal version number.

Independent artifacts version independently and compatibility is explicitly recorded.

## 2. Version families

- Platform release version.
- Public HTTP API contract version.
- Event contract/schema version.
- SDK package version per language.
- Connected Product integration profile version.
- Database migration/schema baseline.
- Policy/configuration version.
- Commercial version.
- Provider configuration version.
- Workflow definition version.
- Documentation baseline version.
- Governance schema/tool version.

## 3. Platform releases

Use Semantic Versioning for software packages/releases where appropriate.

Before 1.0, breaking changes still require explicit compatibility review; “0.x” is not permission for uncontrolled breakage.

## 4. Public API versioning

API compatibility uses an explicit contract version independent of deployment release.

Recommended initial public representation: date/epoch family such as `2026-09`, while endpoints use a stable negotiated/profile mechanism defined in the API FRD.

Do not embed implementation build numbers in client contracts.

## 5. Event schema versioning

Every public/integration event contains:

- event type;
- event ID;
- schema/contract version;
- occurred time;
- subject/reference;
- correlation/causation;
- environment;
- producer identity.

Schema changes are classified:

- backward-compatible additive;
- conditionally compatible;
- breaking.

Breaking versions coexist through a controlled retirement window.

## 6. SDK version enforcement

Each SDK automatically reports:

- SDK language;
- SDK package version;
- runtime/framework version where appropriate;
- API profile requested;
- capabilities supported.

Server resolves compatibility state:

- `CURRENT`
- `SUPPORTED`
- `DEPRECATED`
- `RETIRING`
- `UNSUPPORTED`
- `BLOCKED_FOR_SECURITY`

### Rule

SDK enforces contract use but the server remains the ultimate authorization/compatibility authority.

No SDK can make an unsupported request “valid.”

## 7. Version policy registry

A version policy is versioned/effective-dated and can vary by:

- environment;
- Product;
- client/application;
- contract family;
- security condition.

Example:

```yaml
policy_id: SDK-DOTNET
version: 7
effective_from: 2026-10-01T00:00:00Z
current: 1.4.2
minimum_supported: 1.3.0
deprecated:
  - 1.2.x
blocked:
  - range: "<1.1.8"
    reason: SECURITY
```

Exact numbers are examples only.

## 8. Self-maintained compatibility

Automation should:

1. inventory active clients;
2. compare reported versions to current policy;
3. publish compatibility projections;
4. notify Product administrators before retirement;
5. detect unknown/stale SDK metadata;
6. create governance evidence;
7. never upgrade Product code automatically without Product authority.

## 9. Database versions

Migrations have monotonic identifiers and immutable history.

Release manifest includes required migration set.

Application startup must not silently execute production migrations unless deployment policy explicitly authorizes it.

## 10. Documentation versioning

Each ACTIVE authority has:

- document ID;
- semantic/document version;
- effective date;
- status;
- supersedes;
- associated release ranges where relevant.

## 11. Real example — old SDK

Product Sandbox reports `.NET SDK 1.2.4`.

Policy says it is `DEPRECATED`.

Expected:

- Sandbox calls continue if contract is supported.
- diagnostic endpoint reports deprecation.
- Product administrator gets actionable upgrade guidance.
- no sudden rejection unless policy says retirement/security block is effective.
- Production may have stricter policy than Sandbox only if explicitly configured.

## 12. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| VER-001 | compatible older SDK | request accepted + compatibility metadata returned |
| VER-002 | security-blocked SDK | request rejected with stable machine-readable reason |
| VER-003 | new event adds optional field | old consumer contract test still passes |
| VER-004 | breaking event schema | new version emitted without rewriting old historical payload |
| VER-005 | documentation amended | previous version remains resolvable in Git/history |
