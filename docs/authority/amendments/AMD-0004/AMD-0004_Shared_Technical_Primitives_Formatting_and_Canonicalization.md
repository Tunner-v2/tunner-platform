# Tunner Development Authority Amendment

> **Status:** APPROVED / ACTIVE  
> **Effective date:** 2026-10-02  
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0
> **Applicability:** GLOBAL across all development planes, domains, backend/frontend surfaces, SDKs and operator/user experiences wherever shared technical mechanics, formatting or canonicalization are relevant.
> **Scope clarification:** 2026-10-03 — the gap was identified before P1, but the reuse/canonicalization rules are permanent cross-system engineering rules.

# AMD-0004 — Shared Technical Primitives, Formatting & Canonicalization

## 1. Reason

The Product Owner identified a development-precision gap before P1 execution: existing authority correctly requires DRY technical reuse, `BuildingBlocks`, shared UI components and separation of domain semantics from infrastructure, but it does not explicitly define a reusable contract for deterministic formatting, normalization and canonicalization.

Without this amendment, an AI/human developer could technically conform to individual flow requirements while repeatedly implementing equivalent money/date/slug/string/identifier formatting mechanics in separate screens/modules, or could overcorrect by creating an unsafe generic helper layer that mixes business authority.

This amendment makes the intended enterprise implementation model explicit.

## 2. Change type

`TECHNICAL | ARCHITECTURE | DEVELOPMENT-PRACTICE | UI/QUALITY`

No Tunner commercial/business-domain semantics are changed.

## 3. Approved rules

1. **Flow-to-code mapping is many-to-many.** A `TUN-FLOW-*` identifier establishes behavioral/traceability authority. It does not mandate a dedicated class, service, project, endpoint, database table, worker, component tree or duplicate implementation.
2. Repeated deterministic technical mechanics must be implemented once as cohesive reusable capabilities and reused across applicable flows/modules/surfaces.
3. The governing separation is:
   - domain/business semantics;
   - technical canonicalization;
   - presentation formatting.
   These are distinct authorities/responsibilities.
4. Domain modules retain business calculations, eligibility, authorization, uniqueness scope, mutability/history policy, state transitions and other business meaning.
5. Shared technical primitives own deterministic mechanics only.
6. Presentation formatters/components consume authoritative typed values and change representation only; they may not calculate or alter price, fee, discount, tax, FX, entitlement, permission, state or compliance outcome.
7. Examples of reusable concerns include Money/currency presentation, numeric/percentage formatting, date/time/timezone display, durations, identifiers, slug/text/Unicode/whitespace normalization, URL/path encoding, safe filename normalization, masking/redaction presentation and similar deterministic representation mechanics.
8. Do not create an unbounded `Common`, `Utils` or `Helpers` business-logic dumping ground. Shared code is capability-oriented and cohesive.
9. Before adding a formatter/helper/normalizer, search `BuildingBlocks`, `Platform`, the owning module and frontend shared packages and reuse/extend an existing compatible capability.
10. Prefer stable framework/standard-library/vendor capabilities when they meet the requirement. A Tunner wrapper/abstraction is added only when consistency, portability, policy or testing requires it.
11. Persisted, security-relevant, externally referenced or identity-bearing canonical values are authoritatively validated/canonicalized server-side. Browser/client implementations may preview but do not become persistence/business authority.
12. When .NET and TypeScript/SDK implementations share one canonical semantic rule, they conform to one documented contract and shared test vectors/fixtures where practical. Source-code sharing across runtimes is not required.
13. P1 executable backlog decomposition is capability-oriented, with flow references attached for traceability. Do not create one P1 implementation stack per flow.
14. Shared primitives are established when approved reuse/contract needs exist; do not create speculative generic frameworks or empty abstraction layers.

## 4. Canonical examples

### Money / price presentation

Authoritative input:

`amount + ISO currency`

A shared formatter may render locale/context-specific output such as `CA$1,250.50`.

The formatter does not calculate or change the authoritative amount/currency and does not perform pricing, fee, tax, discount, rounding-policy or FX business calculations.

### Slug / text canonicalization

A shared slug canonicalizer may own casing, separators, allowed-character handling and Unicode/transliteration mechanics.

The owning domain separately owns:

- whether a slug exists;
- uniqueness scope;
- reserved values;
- mutability;
- redirects/history;
- authorization and lifecycle policy.

The server is authoritative for a persisted slug; client preview is advisory.

## 5. Affected authorities

- `01_Engineering_Technology_Foundation.md`
- `02_Repository_Solution_and_Module_Architecture.md`
- `09_Testing_Acceptance_Evidence_and_Quality_Gates.md`
- `10_UI_UX_Components_Widgets_and_Plugins_Development_Rules.md`
- `11_Development_Planes_Milestones_and_P0_Backlog.md`
- `14_Glossary_and_Canonical_Terms.md`
- `32_Domain_API_Event_Data_and_Policy_Contract_Catalog.md`
- `37_Current_Authority_Index.md`
- Development Documentation `README.md`
- Decisions `README.md`
- agent entry contract / repository `AGENTS.md` on authority synchronization

## 6. Global applicability and current transition

AMD-0004 is **GLOBAL**. It applies wherever Tunner implements repeated deterministic technical mechanics or presentation/canonicalization behavior, including P1 Platform Foundation, P2 Account/Identity, P3 Connected Product/SDK, P4 Shared Services, P5 Commercial/Billing, P6 Operations/Security/Privacy, P7 Finance/Accounting, P8 Production Readiness and future planes.

It also applies to governance/developer tooling and shared frontend/backend packages when those concerns are present. It does not force an irrelevant shared abstraction into a domain that has no approved reuse need.

P0 remains closed when its recorded close gate has passed; AMD-0004 does not reopen it merely because the rule is global. P1 was the first immediate consumer after approval, not the scope boundary.

All later decomposition must preserve the same rule: shared technical mechanics are reusable capabilities, flows remain traceability/behavior authority, and business/domain semantics remain owned by their domains.

## 7. Development authorization

**Historical transition snapshot at AMD-0004 approval. P1 being next at that time does not restrict AMD-0004 to P1.**


- Documentation/business closure remains PASS.
- P0 closure is not reopened by this amendment.
- P1 is the next development plane once the repository authority mirror imports/verifies this amendment and governance materializes eligible P1 work items.
- No new Product Owner business decision is introduced beyond approval of this development-practice amendment.

## 8. Approval

Approved by explicit Product Owner direction in the project conversation on 2026-10-02.
