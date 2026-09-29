> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 14 — Glossary & Canonical Terms

## Tunner Account

Tunner-owned identity/account used across Tunner Products.

## Connected Product

A Product integrating with Tunner through registered environments/clients/contracts. The Product retains authority for Product business/operational state.

## Product Environment

A Tunner integration boundary representing Sandbox, Staging or Production configuration for a Connected Product.

## Product Relationship

Tunner-owned relationship indicating an Account/Product integration relationship. It is not Product workspace/team membership.

## Billing Account

Tunner-owned payer/commercial account.

## Billing Account Membership

Tunner-owned delegated billing relationship. Not Product membership.

## Tunner Billing Subscription

Tunner-authoritative versioned commercial/billing lifecycle associated with Product + payer/commercial terms.

It is **not Product entitlement**.

## Product Subscription / Entitlement

Product-authoritative access/package/feature/limit relationship.

Tunner may correlate through `product_subscription_ref` but does not own/mutate Product entitlement.

## Commercial Snapshot

Immutable billing-safe event-time bundle of exact commercial/version references and resolved values. Not a live Product catalog.

## Provider

External capability provider such as payment, email, SMS, tax or verification system.

## Provider Adapter

Implementation translating Tunner generic provider contract to a specific provider. It is not a provider-specific business domain.

## Operation

Stable Tunner business/technical operation identity used for idempotency, state and correlation.

## External Finality

Conclusive evidence about an external side effect. A timeout is not necessarily final failure.

## UNKNOWN_EXTERNAL_STATE

Explicit state indicating Tunner cannot yet prove whether an external side effect occurred.

## Outbox

Records post-commit events atomically with the owning-domain transaction.

## Consumer Inbox

Durable per-consumer/subscriber deduplication/processing identity.

## Product Application Result

Evidence that the Connected Product applied a Tunner result under its own business rules. Distinct from webhook HTTP acknowledgment.

## Read Model

Derived query/projection representation; not authoritative mutation source.

## Policy Version

Immutable versioned/effective configuration used to make a governed decision.

## Product Decision

Explicit Product Owner-approved resolution of an ambiguity/business question.

## Amendment

Approved change to an existing development authority.

## ADR

Architecture Decision Record for material technical choice.

## Evidence Pack

Machine/human-readable artifacts proving build/test/security/acceptance/release conditions.

## Connected Product Service Relationship
The versioned Tunner↔Product relationship describing which Tunner capabilities are offered, how the Product integrates and how Tunner charges for those services. See PDR-0008.

## Product Commercial Agreement
Versioned commercial authority for a Connected Product defining enabled Tunner services, fee policy, provider-cost treatment, settlement/payout policy references, market/currency scope and effective interval.

## Fee Policy
Versioned deterministic rule that calculates Tunner's fee for eligible Product transactions. It can be percentage, fixed, combined or approved tiered logic and always preserves event-time version/evidence.

## Product Compliance Profile
Tunner-owned compliance context for a Connected Product describing the Product-level business/KYC/KYB/risk information required to provide Tunner services. It is separate from Tunner's external provider merchant/account relationship.

## Product KYC Case
Tunner-owned canonical KYC/KYB/compliance case for a Connected Product. Canonical states are `DRAFT`, `SUBMITTED`, `UNDER_REVIEW`, `VERIFIED`, `RESTRICTED`, `REJECTED`, `EXPIRED`, `REVERIFICATION_REQUIRED`, `CLOSED`. Provider verification of Tunner's own account is separate supporting provider evidence.

## Provider Account Binding
Versioned Tunner relationship to an external provider merchant/account/configuration. It belongs strictly to Tunner's provider relationship and may be used for operations from one or many Products. It is execution/reconciliation infrastructure, not Product financial ownership.

## Provider Route Resolution
Immutable event-time evidence of which Tunner Provider Instance/Provider Account Binding/routing-policy versions were selected for an external operation. It is not a Product payable ownership record.

## Provider Object Reference
Typed immutable/safe reference to provider-native evidence such as provider instance/account context, provider object type, provider object ID and provider event ID. A Provider Object Reference is evidence, not a Tunner business resource or Connected Product contract.

## Product Payable
Amount attributable/payable for a Connected Product from Tunner-managed commercial activity after applying the approved transaction, tax, refund/credit/dispute, provider-cost, reserve and Tunner-fee rules. ProductPayable ownership is Product + payable currency and is independent of the Tunner provider merchant/account used for execution. In the initial model, payable currency is the Product commercial transaction/presentment currency from the immutable CommercialSnapshot; provider settlement FX remains separate treasury/clearing evidence and never re-denominates the Product obligation.

## Product Payable Balance
Derived authoritative financial-control view of Product payable liability by Product + payable currency, calculated from ProductPayable and ledger evidence. It may expose accrued, settled, payout-pending, paid, held/restricted and adjustment components and is never an independently editable amount.

## Settlement
Tunner-calculated Product settlement position for a defined Tunner-managed activity period/cycle. Settlement approval is calculation/readiness evidence and never by itself proves money movement. An approved Settlement is immutable; corrections create superseding/correcting settlement operations and compensating financial entries.

## Product Payout Destination Version
Immutable Product-owned payout-destination version/snapshot selected for a Payout. A submitted Payout keeps its original destination version even if the Product later changes destination configuration.

## Payout
Tunner platform lifecycle for one external provider/bank disbursement attempt associated with a Product Settlement and ProductPayoutDestinationVersion. Provider account selection is separate execution routing evidence. Every externally meaningful retry/replacement receives a new `payout_id` and new idempotency context with predecessor lineage.

## Payout Receipt
Immutable final provider/bank evidence for one Payout attempt/finality outcome, including amount/currency, destination snapshot reference, provider evidence and final timestamps/status. It is not an Invoice.

## Payout Processing Case
Exception/reconciliation case opened or linked only when a Payout needs intervention/evidence processing because of failure, return, unknown external outcome, mismatch or another policy-defined exception. A normal successful Payout does not create a processing case.

## Financial-Services Regulatory Gate
A market/provider activation control that blocks a Product/payment route when the configured activity requires an unapproved registration/license or violates applicable provider/compliance policy.

## Home Jurisdiction
A jurisdiction where the Tunner operating company/entity is established or based for corporate/compliance purposes. Current baseline: Toronto, Ontario, Canada. Home jurisdiction does not restrict customer commercial reach.

## Global Eligible Market
A country/territory where Tunner policy permits a Product to be offered/sold after provider, sanctions, privacy/marketing, tax, consumer and other applicable market gates resolve successfully. Provider acceptance alone is insufficient.

## Presentment Currency
The explicit Product price/charge currency shown to the customer. It is configuration-driven and distinct from cardholder/account currency, provider settlement currency, Product payable currency, Product payout currency and statutory/accounting reporting currency.

## Provider Settlement Currency
The currency in which a payment provider credits Tunner's balance/bank settlement path. It may differ from presentment currency and, when conversion occurs, must retain immutable FxConversion/provider evidence.

## Product Payable Currency
The currency in which Tunner records the Product commercial obligation. In the initial model it equals the source transaction/presentment currency from the immutable CommercialSnapshot. Provider settlement currency does not change it.

## Product Payout Currency
The currency selected through the Product's active ProductPayoutPreference for an eligible payout route/destination. It may differ from Settlement currency only through an evidenced FX conversion.

## Tunner Treasury Account
Tunner-owned bank/treasury settlement destination metadata, including account currency, country, readiness/effective state and protected provider/bank references. Current active baseline includes a CAD-denominated Canadian bank account.

## FX Conversion
Immutable evidence of an actual currency conversion or policy-required statutory/accounting translation, including source/target amounts and currencies, effective rate, source/provider/bank reference, fees/spread evidence where available and timestamps.

## Product Payout Preference
Versioned Product configuration for preferred payout currency, destination/policy and effective interval. Preference does not guarantee a route; eligibility is checked at payout time.

## Provider Settlement Profile
Versioned provider/account configuration that maps supported payment/presentment currency context to provider settlement currency, TunnerTreasuryAccount destination and provider conversion behavior.

## FX Economic Treatment Policy
Versioned/effective commercial policy determining who bears actual externally evidenced FX fee/spread/variance when a conversion affects Product economics. PDR-0010 defines cause-based defaults: customer/issuer FX is `EXTERNAL_TO_TUNNER`; Tunner provider-settlement/treasury FX is `TUNNER_BORNE`; Product-requested payout FX is `PRODUCT_BORNE`; statutory/accounting translation is `REPORTING_ONLY`. ProductCommercialAgreement may explicitly override Product-affecting treatment with `PRODUCT_BORNE`, `TUNNER_BORNE` or `SHARED`.

## Guided Financial Configuration
Tunner UX/API pattern for policy-driven financial setup using recommended templates, contextual validation, currency/corridor capability checks, financial preview, current-vs-candidate version comparison, effective dates, approval and immutable audit evidence. Advanced options such as shared allocation and caps appear only when selected/authorized; operators do not edit raw financial state or hidden constants.

## Market Eligibility Policy
Versioned/effective policy resolving whether a Product/environment/payment-service profile may transact with a customer jurisdiction and under what tax/privacy/marketing/payment constraints.

## R2 Location Hint
A best-effort Cloudflare R2 placement hint intended primarily for access locality/performance. It is **not** a Tunner residency guarantee.

## R2 Jurisdictional Restriction
A Cloudflare R2 bucket jurisdiction constraint that guarantees storage/processing within a supported jurisdiction boundary. It may satisfy a Tunner residency policy only when the policy explicitly accepts that jurisdiction.

## Primary Payment Provider
The currently selected payment-provider implementation behind Tunner's provider-neutral contract. Current baseline: Stripe.

## Managed Tax Service
An external maintained service that performs tax-rate, jurisdiction and taxability calculation for supported transaction contexts. Tunner consumes it through `ITaxEngine` and does not reproduce its tax-rule database inside Tunner.

## Tax Engine
The Tunner provider-neutral `ITaxEngine` foundation used to invoke an approved Managed Tax Service and normalize its result into authoritative Tunner `TaxDetermination` evidence. Current initial implementation: `StripeTaxAdapter` using Stripe Tax where supported.

## Tax Engine Binding
Versioned/effective configuration selecting an approved `ITaxEngine` adapter for an environment/market/legal-tax context, including capability/readiness and supported scope. Payment-provider routing and TaxEngineBinding are independently configurable even when the same provider supplies both services.

## Tax Provider Extensibility
The architectural invariant that a new approved managed tax service can be added as another `ITaxEngine` adapter without changing Connected Product API/SDK contracts or redesigning Tunner Checkout, Invoice, Payment, Finance or `TaxDetermination` ownership.

## Primary Transactional Email Provider
The currently selected provider used for production transactional/security email behind `IEmailProvider`. Current baseline: Resend.


## Checkout Presentation Mode
A provider-adapter presentation choice for one Tunner `CheckoutOperation`. Supported modes are `EMBEDDED_PROVIDER_COMPONENT` and `PROVIDER_HOSTED_REDIRECT`. The mode controls secure payment-data collection UX only; Tunner retains checkout operation identity, commercial/tax context, payment state and final result.

## Checkout Operation
Tunner-owned customer checkout journey binding Product/commercial snapshot, explicit price/currency, TaxDetermination, Billing Account/customer context, payment operation and final result. Provider payment collection may use an approved embedded or provider-hosted presentation mode while CheckoutOperation remains Tunner authority.

## Provider Payment Collection Session
Short-lived provider-specific browser initialization/tokenization context created after Tunner resolves the payment route. It is an execution artifact, is not portable across providers unless explicitly supported, and never contains raw PAN/CVV in Tunner systems.


## Content Key
Stable semantic identifier for one manageable presentation-content purpose/slot. It is independent of a visual layout coordinate and does not contain business authority.

## Content Definition
Tunner-owned definition of a Content Key, its intended purpose, content class, allowed variables/slots and governance metadata.

## Content Variant
A governed contextual variant of a Content Definition for approved resolution dimensions such as locale, audience/role, surface, category, Product/context or environment.

## Content Version
Immutable version of a Content Variant. Publication activates an approved version prospectively; historical acknowledgement/delivery/audit evidence retains the exact effective version reference where required.

## Content Publication
Governed activation of an approved Content Version for its effective scope/interval. Publication is presentation authority only and cannot change domain/business semantics.

## Content Resolver
Tunner capability that resolves a semantic Content Key plus safe context to the effective published Content Version using deterministic precedence/fallback rules. Cache is non-authoritative.

## Content Proposal
Unpublished candidate content authored by a human or approved model adapter. A Content Proposal has no production effect until validation and required authorization/approval/publication complete.

## Governed Presentation Content
Human-readable Tunner content shown to users/operators, including titles, labels, descriptions, guidance, actions, statuses, errors, empty/result states, notifications and disclosures. It explains authoritative domain facts but never defines prices, permissions, eligibility, workflow behavior, financial calculations, compliance decisions or other business authority.
