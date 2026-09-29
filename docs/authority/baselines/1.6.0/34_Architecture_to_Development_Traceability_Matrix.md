> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 34 — Architecture-to-Development Traceability Matrix

## 1. Purpose

Every FigJam architecture section must resolve to authoritative development documentation. A range mapping below applies to **each section number in that range individually**; special A-sections are separately listed. Implementation work must cite both the relevant FRD and exact FigJam flow/section when behavior is flow-specific.

## 2. Section coverage

| FigJam section(s) | Development authority | Closure purpose |
|---|---|---|
| 01 | 01, 02, 15, 32 | platform architecture, topology, cross-domain boundaries |
| 02–02A, 07 | 17, 19, 32 | direct/Product-origin identity, SSO and return flow |
| 03, 08 | 21, 24, 25, 26 | subscription checkout/billing/payment/settlement |
| 04 | 28 | Support workflow |
| 05, 16–18 | 19, 23, 27 | Connected Product onboarding/integration/notifications/ops |
| 06, 09–10 | 17–22, 32 | normalized domain relationships and Product lifecycle |
| 11–12 | 23, 32 | provider architecture/setup |
| 13 | 24, 26 | Stripe payment/webhook/ledger/reconciliation |
| 14–15 | 27, 23 | notification provider integration, delivery evidence and provider-boundary contracts |
| 19–27 | 21, 24–26, 28, 32 | complete Finance lifecycle/control |
| 28–35 | 15, 30, 32 | execution/event/worker/timer/finality operations |
| 36–45 | 17, 28, 32 | Identity/auth/session/security |
| 46–56 | 18, 24–26, 32 | Billing Account/delegated authority/payment methods/migration |
| 57–72 | 20–22, 24–25, 32 | Subscription/checkout/renewal/dunning/trials/discounts |
| 73–86 | 20, 22, 26, 32 | Commercial catalog/pricing/agreement/fee/tax/currency |
| 87–92 | 23, 24, 27, 32 | Provider & Merchant Operations |
| 93–99 | 27, 32 | Notifications |
| 100–105 | 28, 31 | Support |
| 106–110 | 28, 29 | Risk & Compliance |
| 111–114 | 28, 30, 32 | Audit/Governance/approvals/policies |
| 115–122 | 28, 31, 32 | Internal Access |
| 123–127 | 30, 33 | Platform Reliability/change/DR |
| 126A | 30, 33 | Data Protection Contract |
| 127A | 30, 33 | Reliability Budget |
| 128–135 | 19, 32, 33 | Product Integration/API/SDK/events/results |
| 128A | 19, 30, 32 | API protection policy |
| 128B | 19, 31, 33 | Product integration self-service/readiness |
| 136–140 | 30, 32 | workflow/automation/Governor/non-human/AI |
| 141 | 22, 26, 33 | tax liability/remittance |
| 142–144 | 29, 30 | data lifecycle/retention/hold |
| 145–150 | 29, 28, 33 | privacy requests/governance/incidents |

## 3. Explicit FigJam contract-marker closure

The following markers were discovered by programmatic FigJam audit and must have a direct closing contract:

| Section | Marker | Closing document |
|---:|---|---|
| 41 | RESOLVED — FRD CONTRACT AUTHORITY | 17 §9 |
| 42 | RESOLVED — FRD CONTRACT AUTHORITY | 17 §10 |
| 43 | RESOLVED — FRD CONTRACT AUTHORITY | 17 §§4–5 plus verification rules |
| 44 | RESOLVED — FRD CONTRACT AUTHORITY | 17 §12 |
| 47 | RESOLVED — FRD CONTRACT AUTHORITY | 18 payer/provisioning contract |
| 48 | RESOLVED — FRD CONTRACT AUTHORITY | 18 membership lifecycle |
| 49 | RESOLVED — FRD CONTRACT AUTHORITY | 18 authority precedence/capabilities |
| 50 | RESOLVED — FRD CONTRACT AUTHORITY | 18 effective dating/revocation |
| 52 | RESOLVED — FRD CONTRACT AUTHORITY | 18 Billing Profile contract |
| 53 | RESOLVED — FRD CONTRACT AUTHORITY | 18 Payment Method Reference contract + 24 execution boundary |
| 54 | RESOLVED — FRD CONTRACT AUTHORITY | 18 Product Billing Relationship |
| 55 | RESOLVED — FRD CONTRACT AUTHORITY | 18 closure blockers/lifecycle |
| 56 | RESOLVED — FRD CONTRACT AUTHORITY | 18 migration policy and historical-invariant contract |
| 60 | RESOLVED — FRD CONTRACT AUTHORITY | 21 first-payment ordering + 24 payment finality |
| 61 | RESOLVED — FRD CONTRACT AUTHORITY | 21 canonical lifecycle state model |
| 62 | RESOLVED — FRD CONTRACT AUTHORITY | 21 change/proration/schedule contract |
| 63 | RESOLVED — FRD CONTRACT AUTHORITY | 21 renewal ordering/finality |
| 64 | RESOLVED — FRD CONTRACT AUTHORITY | 21 dunning policy contract |
| 65 | RESOLVED — FRD CONTRACT AUTHORITY | 21 lifecycle-control contract |
| 66 | RESOLVED — FRD CONTRACT AUTHORITY | 21 Product handoff + 19/32 event/application-result contract |
| 67 | RESOLVED — UI/FRD CONTRACT AUTHORITY | 21 Product-entitlement boundary + 31 customer wording/action matrix |
| 68 | RESOLVED — FRD CONTRACT AUTHORITY | 20/21 multi-item/commercial action contract |
| 69 | RESOLVED — FRD CONTRACT AUTHORITY | 20/21 trial/intro phase contract |
| 71 | RESOLVED — FRD CONTRACT AUTHORITY | 20/21 cadence/calendar contract |
| 72 | RESOLVED — FRD CONTRACT AUTHORITY | 20/21 discount/promotion contract |
| 141 | RESOLVED — POLICY/FRD/RELEASE-GATE AUTHORITY | 22 remittance policy + 26 accounting/reconciliation + 33 market/release gate |

## 4. Cross-cutting authority coverage

| Concern | Authority |
|---|---|
| documentation/change control | 00, 16, 34 |
| technology/package choices | 01, 13, decisions/ADRs |
| repository/modules | 02 |
| Scrum/Git lifecycle | 03 |
| governance/context/dev tooling | 04, 12 |
| versioning/releases | 05 |
| environments/secrets/deployment | 06 |
| public API/SDK/events | 07, 19, 32 |
| security/privacy/compliance/global market | 08, 17, 22, 28, 29, 33 |
| tests/evidence | 09, every FRD acceptance section, 33 |
| UI/UX | 10, 31 |
| planes/milestones | 11 |
| runtime communication/infrastructure | 15, ADRs |
| Product compliance/KYC + provider-account separation | PDR-0006, 07, 22, 23, 28, 32 |
| Product payable balance + payout identity/destination/finality/exception reconciliation | PDR-0006, 26, 32 |
| Tunner-owned notification SDK/provider boundary | 27, 32 |

## 5. Product Decision closure

No Product Owner business decision remains OPEN in the current documentation closure.

Resolved constitutional decisions include:

- PDR-0005 — Canada/Ontario home jurisdiction and configurable currency model;
- PDR-0006 — Product finance/provider-account separation and payment orchestration;
- PDR-0007 — DOB-based 18+ eligibility;
- PDR-0008 — Connected Product service relationship/platform scope;
- PDR-0009 — managed/extensible tax-engine foundation;
- PDR-0010 — configurable FX economic treatment and guided configuration.

PDR-0010 maps FX economics across 20, 26, 31, 32 and 33: cause-based defaults are policy-driven, Product-specific overrides are versioned through ProductCommercialAgreement, actual conversions retain immutable FxConversion evidence, and operator configuration is guided/validated/previewable rather than hardcoded.

If a new ambiguity is discovered during FigJam alignment, it must be added to document 16 as a new Product Decision/amendment gate; implementation may not invent a resolution.

## 6. Closure audit conditions

Final promotion to ACTIVE requires all of:

- every section mapping above has an implementation-capable contract;
- all explicit FigJam BACKEND/LEGAL/UI markers resolved;
- all open PD-CLOSE decisions accepted/replaced, with PD-CLOSE-01/02/04 already resolved and current currency/checkout model recorded in PDR-0005/PDR-0006;
- no normative unresolved-marker status remains in development authority; historical FigJam closure markers are recorded above as RESOLVED against their owning FRD/UI/policy authority;
- source register freshness corrected;
- manifest/hash/package audit passes;
- domain enum/term/reference consistency passes;
- UI screen matrix covers every user/operator action in the FRDs;
- production gates make variable legal/deployment values explicit policy/release inputs rather than developer guesses.
