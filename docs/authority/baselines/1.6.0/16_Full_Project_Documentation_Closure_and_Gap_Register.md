> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 16 — Full-Project Documentation Closure & Gap Register

## 1. Closure objective

Tunner is not considered development-ready merely because P0/P1 can start. The entire development authority must be sufficiently complete that implementation of P0 through P8 never requires a developer, AI agent, designer, or operator to invent business semantics.

A mutable policy value (for example a jurisdiction deadline, SLO target, retry schedule, tax registration status, or Product-specific dunning schedule) may remain configurable **only when** the owning policy schema, authority, activation gate, versioning, fallback/block behavior, UI treatment, audit evidence, and tests are fully specified. A configurable value is not a documentation gap. An undefined behavior is.

## 2. Completion standard

For each functional domain, documentation must define:

- authority owner and boundaries;
- canonical entities and identifiers;
- lifecycle/state machines and legal transitions;
- commands/operations, authorization and idempotency;
- concurrency/version checks;
- API, SDK and event/webhook contracts;
- policy/configuration objects and precedence;
- external-provider finality and UNKNOWN behavior;
- data ownership, retention and audit evidence;
- notifications and customer/operator-visible wording rules;
- UI/control-center requirements and missing-screen behavior;
- failure, retry, recovery and reconciliation semantics;
- permissions, step-up and approval/SoD requirements;
- examples and acceptance tests;
- explicit unsupported/deferred capabilities and the trigger required to enable them;
- FigJam section/flow traceability.

## 3. Architecture coverage requiring development specifications

The FigJam architecture contains the following major functional groups, all of which require an authoritative development specification before whole-project closure:

| Architecture sections | Domain | Required development authority |
|---|---|---|
| 28–35 | Execution, events, workers, timers, external finality | Existing baseline must be promoted into explicit runtime contract catalog |
| 36–45 | Account, identity, authentication, sessions, recovery, security | Account & Identity FRD |
| 46–56 | Billing Account, membership, authority, profile, payment-method refs, migration | Billing Account & Authority FRD |
| 57–72 | Subscription, checkout, renewal, dunning, lifecycle, trials, cadence, discounts | Billing Subscription & Checkout FRD |
| 73–86 | Catalog, pricing, agreement, fee, economics, tax, currency, source conflict | Commercial & Pricing FRD + Tax/Market FRD |
| 87–92 | Merchant/provider onboarding, routing, credentials, health | Provider & Merchant Operations FRD |
| 93–99 | Notification intent, templates, delivery, preference/suppression, routing, in-app | Notifications FRD |
| 100–105 | Support intake, investigation, escalation, resolution, reporting | Support Operations FRD |
| 106–110 | Risk case, IDV review, merchant compliance, policy exception | Risk & Compliance FRD |
| 111–114 | Audit, approval, policy governance | Audit & Governance FRD |
| 115–122 | Workforce identity, roles, JIT privilege, certification, SoD, offboarding | Internal Access FRD |
| 123–127A | Reliability, incidents, change, data protection, DR, reliability budgets | Platform Reliability/DR FRD |
| 128–135 + 128A/128B | Product registry, OAuth clients, SDK compatibility, capabilities, webhooks, protection, self-service | Connected Product & SDK FRD |
| 136–140 | Workflow definitions, durable workflow, Governor, non-human/AI principals | Automation & Governed Tooling FRD |
| 141 | Tax remittance | Tax Remittance & Liability contract |
| 142–150 | retention, holds, privacy requests, processing governance, privacy incidents | Privacy & Data Lifecycle FRD |
| 19–27 | payments, invoice/receivable, ledger, economics, payable, settlement, corrections, approval | Payments/Finance/Accounting FRDs |

## 4. Explicit FigJam contract markers that must be fulfilled

The following architecture sections explicitly state that a backend/legal contract is required. Full documentation closure is impossible until each is satisfied by a named development document and acceptance tests:

- 41 — session revocation, token/grant semantics and Product propagation;
- 42 — recovery proof hierarchy, retry/lockout, factor-reset effects and no-contact path;
- 43 — verification retention, duplicate-contact conflict, provider refresh;
- 44 — Account restriction/closure, billing blockers, session propagation and reopen behavior;
- 47 — Billing Account defaults, compatibility and duplicate prevention;
- 48 — membership invitation/role/removal semantics;
- 49 — Billing Authority precedence and revocation behavior;
- 50 — authority transfer, in-flight operation behavior and concurrency;
- 52 — Billing Profile ownership/version resolution;
- 53 — payment-method reference lifecycle/default/fallback/migration;
- 54 — Product Billing Relationship state/effective dating;
- 55 — Billing Account closure blockers/reopen/retention;
- 56 — Billing Account merge/split/payer migration;
- 60 — first-payment activation/invoice/payment ordering and provider finality;
- 61 — canonical Subscription states and action authority;
- 62 — proration, scheduled changes and conflict precedence;
- 63 — renewal ordering, version selection and partial failure;
- 64 — dunning policy contract and recovery;
- 65 — pause/resume/cancel semantics;
- 66 — Product result/event delivery and application result;
- 67 — Account Center wording/authority gap;
- 68 — multi-item and independent-subscription semantics;
- 69 — trials/introductory phase semantics;
- 71 — cadence/anchor/calendar rules;
- 72 — discounts/promotions/stacking/redemption;
- 141 — jurisdiction-specific tax filing/remittance contract boundary.

## 5. Development specifications created in the closure draft

The following specifications now exist in the 1.6.0 closure workspace and are subject to the final cross-document audit and Product Decision closure:

1. `17_Account_Identity_and_Session_FRD.md`
2. `18_Billing_Account_and_Delegated_Authority_FRD.md`
3. `19_Connected_Product_and_SDK_Lifecycle_FRD.md`
4. `20_Commercial_Catalog_Pricing_Agreement_and_Fees_FRD.md`
5. `21_Billing_Subscription_Checkout_Renewal_and_Dunning_FRD.md`
6. `22_Global_Market_Tax_Product_Compliance_and_Remittance_FRD.md`
7. `23_Provider_Account_and_Routing_Operations_FRD.md`
8. `24_Payments_Refunds_Disputes_and_Reconciliation_FRD.md`
9. `25_Invoices_Credits_Receivables_and_Collections_FRD.md`
10. `26_Finance_Ledger_Accounting_Settlement_and_Payout_FRD.md`
11. `27_Notifications_Communications_and_Preferences_FRD.md`
12. `28_Support_Risk_Audit_and_Internal_Operations_FRD.md`
13. `29_Privacy_Data_Lifecycle_and_Processing_Governance_FRD.md`
14. `30_Platform_Reliability_Change_DR_and_Automation_FRD.md`
15. `31_UI_UX_Screen_and_Control_Center_Requirements_Matrix.md`
16. `32_Domain_API_Event_Data_and_Policy_Contract_Catalog.md`
17. `33_Production_Readiness_Compliance_and_Release_Gates.md`
18. `34_Architecture_to_Development_Traceability_Matrix.md`

The package may split a file further if that materially improves authority or readability. Consolidation must never obscure domain ownership.

## 6. Product-owner decisions and closure status

All Products use the same Tunner service relationship and provider-neutral integration model.

### PD-CLOSE-01 — Connected Product service relationship — RESOLVED

Recorded as **PDR-0008**. Tunner models the service capabilities, commercial terms, integration state, merchant/payment configuration, settlement evidence and operational/audit context required for a Connected Product.

### PD-CLOSE-02 — Merchant/KYC/payment orchestration — RESOLVED

Recorded as **PDR-0006**. Connected Products integrate only with Tunner. Product economics are Product-scoped: ProductPayable and ProductPayableBalance are owned by Product + payable currency, while Tunner separately owns/administers provider merchant/accounts, ProviderAccountBinding, routing and provider evidence. Product compliance/KYC is represented by `ProductKycCase`; provider verification of Tunner's own account is separate infrastructure evidence. A Product may use multiple provider accounts without creating provider-specific Product balances.

Product compliance restrictions never delete already-earned ProductPayable. Approved Settlements are immutable. Payouts snapshot `ProductPayoutDestinationVersion`; an externally meaningful replacement uses a new `payout_id`/idempotency context; `PayoutProcessingCase` is exception/reconciliation-only.

Product commercial treatment is captured through versioned `ProductCommercialAgreement` and `FeePolicy` records. Different Products can have different Tunner fees such as `8% + USD 0.25` versus `12%`, and every financial event retains the exact effective versions used.

### PD-CLOSE-03 — Managed tax engine and provider extensibility — RESOLVED

Recorded as **PDR-0009**. Tunner does **not** implement or maintain proprietary tax-rate, jurisdiction or taxability calculation logic. `ITaxEngine` is the stable provider-neutral foundation for managed tax services. **Stripe Tax is the initial production adapter** where its capability supports the active market/transaction context.

Additional managed tax services may be added later as new `ITaxEngine` adapters through provider capability/configuration and testing, without changing Tunner Checkout, Product API/SDK, Invoice, Payment or Finance business contracts. Tunner may prefer the active payment provider's managed tax service when that service is approved and compatible, but payment-provider routing and tax-engine binding remain separate capabilities. Tunner remains authoritative for `TaxResponsibilityProfile`, `TaxEngineBinding`, normalized immutable `TaxDetermination`, registration/readiness policy, accounting evidence, correction/reconciliation and remittance state. Missing or unknown required tax calculation blocks the affected transaction rather than invoking a Tunner-built calculator, guessing a rate or silently applying zero tax.

### Checkout/business-location simplification — RESOLVED

PDR-0005 defines Toronto, Ontario, Canada as Tunner's current legal/home jurisdiction and records the current CAD-denominated Canadian bank account. Product payable remains in Product commercial transaction currency; provider settlement/treasury currency is independent; Product payout currency is configurable; every actual conversion is preserved as immutable FX evidence. PDR-0006 defines Tunner-owned checkout orchestration with provider-secure embedded or provider-hosted presentation modes and pre-submission provider continuity routing.

### PD-CLOSE-04 — 18+ age assurance baseline — RESOLVED

Recorded as **PDR-0007**. Registration asks for date of birth, calculates age server-side using calendar-safe date arithmetic, denies activation for users under 18 and does not use a separate age-attestation checkbox. Raw DOB retention is purpose/policy controlled.

### PD-CLOSE-05 — Configurable FX economic responsibility — RESOLVED

Recorded as **PDR-0010**. FX economic treatment is governed by versioned `FxEconomicTreatmentPolicy` and follows the cause of the conversion. The approved platform default is:

1. **customer/cardholder FX** — `EXTERNAL_TO_TUNNER` when the issuer/payment method performs conversion outside Tunner;
2. **provider-settlement / Tunner treasury FX** — `TUNNER_BORNE` by default because the conversion results from Tunner provider/bank routing and may not re-denominate or silently reduce ProductPayable;
3. **Product payout FX** — `PRODUCT_BORNE` by default when the Product selects a payout currency different from Settlement/payable currency; actual externally evidenced conversion cost is allocated transparently;
4. **statutory/accounting translation** — `REPORTING_ONLY`; it creates valuation/evidence and does not itself create an economic deduction.

`ProductCommercialAgreement` may explicitly override Product-affecting FX allocation. Supported policy treatment is configuration-driven rather than coded per Product and includes `PRODUCT_BORNE`, `TUNNER_BORNE`, `SHARED` and the non-economic/contextual modes `EXTERNAL_TO_TUNNER` and `REPORTING_ONLY`. `SHARED` uses an explicit percentage split and may include approved caps/subsidy limits. Only actual externally evidenced cost components may be allocated; Tunner does not fabricate an FX spread or duplicate a provider/bank charge.

A platform **Standard Cause-Based** policy is available as the safe default, so Products do not require repetitive manual setup. Authorized operators can create Product-specific overrides through a guided configuration workflow: select scope/context, choose a policy template, configure allocation/components/caps when applicable, preview example outcomes and route/currency compatibility, review effective dates and current-vs-candidate versions, obtain required approval, then activate. Configuration is versioned/effective-dated and never rewrites historical `FxConversion`, Settlement or Payout evidence.

Product self-service may configure `ProductPayoutPreference` within eligible corridors and view/preview the effective FX treatment, but cannot change commercial FX responsibility unless the active ProductCommercialAgreement and authorization policy expressly permit it. Missing or ambiguous required policy resolution blocks the affected conversion rather than guessing.

## 7. Decisions that do not require Product Owner intervention

The following can be closed in the FRDs as engineering/domain contracts because they preserve existing Product Decisions rather than create new commercial policy:

- no automatic identity linking solely because two providers report the same email;
- verified-contact replacement is candidate-first and old verified contact remains until new proof succeeds;
- no implicit subscription proration: the active commercial policy must specify behavior or the monetary change is blocked;
- no implicit dunning schedule: every paid recurring Product contract references a versioned Dunning Policy; missing policy blocks activation/renewal configuration;
- no implicit trials/discounts/promotions: capability is supported, but absence of an active version means none applies;
- no implicit FX-derived customer price: Product prices are explicit by configured currency; unsupported currency blocks rather than silently converting;
- no blind payment/provider/email failover after UNKNOWN external outcome;
- historical invoice/payment/ledger/commercial/tax records are immutable; corrections are compensating records;
- self-service Billing Account merge/split is disabled; governed payer migration is prospective and preserves original financial lineage;
- marketing communication is disabled for a recipient/market unless the applicable consent/preference/market policy explicitly allows it;
- generic JSON workflow DSL is not a prerequisite to implement domain workflows; typed/compiled orchestration remains the default until the documented abstraction trigger is met.

## 8. Full closure gate

The package can return to `ACTIVE / DEVELOPMENT_AUTHORIZED=true` only when all of the following pass:

1. every document in section 5 exists and is authoritative;
2. every FigJam contract marker in section 4 maps to a resolved specification section;
3. every OPEN Product Decision in section 6 is approved or replaced by an explicit alternative;
4. no FRD contains unresolved `TBD`, `TODO`, `OPEN CONTRACT`, `BACKEND CONTRACT REQUIRED`, or equivalent implementation ambiguity;
5. every architecture section 01–150 plus 126A/127A/128A/128B has at least one development-document owner in the traceability matrix;
6. state/event/API names are consistent across domain documents;
7. Product/Tunner authority boundaries are consistent;
8. provider-specific concepts do not become domain authority;
9. global market/tax/privacy/marketing activation has a deterministic policy gate;
10. UI requirements identify every required screen/state/action or explicitly mark it as `UI_REQUIRED` before implementation;
11. acceptance tests cover happy path, duplicates, concurrency conflicts, partial failure, UNKNOWN external state, retries, cancellation, correction, authorization failure, policy version change and recovery;
12. manifest inventory/hash audit passes;
13. global governed presentation-content architecture is implemented in the development authority: all applicable UI/notification/disclosure content uses stable semantic content contracts, deterministic fallback, version/publication/audit controls and user/operator-oriented language;
14. no ordinary user-facing surface depends on raw internal state/class/provider/database/queue terminology as its primary copy, and content management cannot alter business/domain semantics;
15. contextual content administration, sensitive-content governance and model-assisted proposal boundaries have implementation-capable contracts and acceptance coverage.

**Baseline 1.6.0 closure result: PASS — 2026-09-29.** The requirements above are satisfied for the locked baseline. Any future amendment that invalidates a closure condition reopens the affected gate.

After this documentation gate passes, development authorization opens **P0 governance/control-plane implementation only**. Feature/platform planes P1–P8 remain blocked until the P0 feature-development enablement gate in document 11 passes. This separates documentation readiness from executable-governance readiness and prevents feature development from depending on ad-hoc chat memory or unenforced workflow rules.
