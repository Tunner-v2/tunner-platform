# Tunner Decision Records — Active Authority

Only current Product Decisions belong in the active development package. Historical business-model changes remain in source-control/change history rather than in active implementation guidance.

## Active ADRs

- `ADR-0001_Durable_Messaging_RabbitMQ_Outbox_Inbox.md`
- `ADR-0002_Redis_Cache_and_SignalR_Backplane.md`
- `ADR-0004_Communication_Protocol_Boundaries.md`
- `ADR-0005_Browser_BFF_and_Token_Security.md`
- `ADR-0006_Identity_ASPNET_Identity_OpenIddict.md`
- `ADR-0007_Frontend_React_TypeScript_Vite_TailAdmin.md`
- `ADR-0008_GitHub_and_GitHub_Actions.md`
- `ADR-0009_Local_Observability_OTEL_LGTM.md`
- `ADR-0011_OpenAPI_31_on_NET10.md`
- `ADR-0012_Object_Storage_Cloudflare_R2.md`
- `ADR-0013_Transactional_Email_Resend_Mailpit.md`
- `ADR-0014_Payment_Provider_Stripe.md`

## Active Product Decisions

- `PDR-0005_Canada_Ontario_Home_Jurisdiction_and_Configurable_Currency_Model.md` — Toronto, Ontario, Canada is the current legal/home jurisdiction; Tunner currently has a CAD-denominated Canadian bank account; presentment, provider settlement, Product payable and Product payout currencies are separately configured with immutable FX evidence.
- `PDR-0006_Tunner_Managed_Provider_Accounts_Product_KYC_and_Payment_Orchestration.md` — ProductPayable/PayableBalance are Product + payable-currency scoped; ProductKycCase is Tunner-owned Product compliance; provider merchant/accounts, routing and checkout presentation remain separate Tunner infrastructure; Products integrate only with Tunner; PayoutProcessingCase is exception-only.
- `PDR-0007_DOB_Based_18_Plus_Eligibility.md` — registration collects date of birth and performs server-side calendar-safe 18+ determination.
- `PDR-0008_Connected_Product_Service_Relationship_and_Platform_Scope.md` — defines the uniform Tunner service/commercial/operational relationship used by every Connected Product.
- `PDR-0009_Managed_Tax_Engine_and_Extensible_Tax_Provider_Architecture.md` — Tunner uses managed tax services through provider-neutral `ITaxEngine`; Stripe Tax is the initial adapter; future approved managed tax providers plug into the same foundation without changing Product-facing or canonical Checkout/Invoice/Payment/Finance contracts.
- `PDR-0010_Configurable_FX_Economic_Treatment_and_Guided_Configuration.md` — cause-based FX responsibility is locked through versioned `FxEconomicTreatmentPolicy`; Standard Cause-Based defaults apply automatically, ProductCommercialAgreement may explicitly override Product-affecting allocation, and all configuration is guided, validated, previewable, effective-dated and auditable.

## Active Amendments

- `AMD-0001_Pre-P0_Governance_Agent_Skills_Context_and_Bootstrap_Completion.md` — **GLOBAL** — approved 2026-09-29. Completes the pre-P0 AI-development governance model: 20 specialist skills, governance-controlled orchestration, bounded/adaptive context, token-cost governance, repository/GitHub bootstrap semantics, P0 feature gate and relocation of feature/platform implementation out of the governance plane.
- `AMD-0002_Non-Blocking_PR_Approval_and_Continuous_Local_Execution.md` — **GLOBAL** — approved 2026-09-30; commit-readiness clarified 2026-10-03. Makes required human PR approval a protected-main integration/merge gate rather than a global development execution gate; governance continues eligible local/branch work, requires repository-changing work to reach a validated committed state before `LOCAL_VALIDATED`/completion, and distinguishes that committed local readiness from `MERGED_TO_MAIN`.
- `AMD-0003_Location-Independent_Repository_and_Path_Resolution.md` — **GLOBAL** — approved 2026-09-30. Prohibits hardcoded machine-specific repository/solution/workspace absolute paths, requires repository-relative persistence plus dynamic root discovery, and adds relocation/path-portability gates.

- `AMD-0004_Shared_Technical_Primitives_Formatting_and_Canonicalization.md` — https://drive.google.com/file/d/1ZBHXj1ndc5x341fRxEsva06Z2vpUPWzn/view — **GLOBAL** — approved 2026-10-02. Makes flow-to-code mapping explicitly many-to-many; requires reuse of cohesive technical primitives and shared presentation formatting/canonicalization; separates business/domain semantics from deterministic mechanics and UI representation; prohibits screen/module-local duplication and catch-all helper business logic; adds server/client authority and cross-runtime conformance rules.

- `AMD-0005_Authority-First_Decision_Resolution_and_Duplicate-Decision_Prevention.md` — https://drive.google.com/file/d/1M_B4TYwf5gDKaZ-1w_gMR9Nse-Fs7B87/view — **GLOBAL** — approved 2026-10-03. Requires a verified Authority Resolution Pass before new decision/missing-authority blocker creation; rejects duplicate decisions already resolved by active authority; distinguishes absent conditional activation triggers from real contract gaps; requires insufficient context to expand before declaring missing authority.

Baseline 1.6.0 remains the immutable locked archive. Until a later consolidated baseline supersedes it, implementation authority is **Baseline 1.6.0 + accepted ADR/PDR records + active amendments**.


### Amendment applicability clarification

AMD-0001 through AMD-0005 have been explicitly clarified as **GLOBAL** for their cross-cutting governing principles. Plane/work-item references inside those amendments record discovery context or initial implementation obligations; they do not limit applicability unless a rule explicitly says so.
