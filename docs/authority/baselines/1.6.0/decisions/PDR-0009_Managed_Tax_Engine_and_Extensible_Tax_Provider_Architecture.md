# PDR-0009 — Managed Tax Engine and Extensible Tax-Provider Architecture

**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Effective:** 2026-09-25

## Decision

Tunner will not implement or maintain a proprietary tax-rate, jurisdiction, threshold or taxability calculation engine. Tax calculation is delegated to approved **managed tax services** through Tunner's stable provider-neutral `ITaxEngine` contract.

**Stripe Tax is the initial production `ITaxEngine` implementation** through `StripeTaxAdapter` where Stripe Tax supports the applicable market, currency, Product tax classification and transaction context.

Tunner is explicitly **flexible and enhanceable** at the tax-service layer. New managed tax providers can be introduced later by implementing additional `ITaxEngine` adapters on the same foundation. Adding a tax provider must not require a new Connected Product API/SDK contract or a redesign of Tunner Checkout, Invoice, Payment, Finance, Ledger or the canonical `TaxDetermination` model.

## Payment-provider and tax-provider relationship

Tunner may prefer the selected payment provider's managed tax service when that service exposes an approved compatible tax-calculation capability. This is an operational preference, not an architectural coupling.

Payment-provider routing and tax-engine binding are separate capabilities:

- a payment provider is selected for payment execution;
- an `ITaxEngine` binding is selected for tax calculation;
- the same external provider may supply both capabilities;
- a future payment provider may have no approved tax capability;
- a future managed tax service may be used even when another provider executes the payment;
- changing a payment route does not by itself invalidate or recalculate an already-authoritative `TaxDetermination`.

Tunner must never assume that every payment provider automatically supplies a usable tax-calculation service. Tax capability is explicitly discovered/configured, validated and activated.

## Tunner authority

Tunner remains authoritative for:

- `TaxResponsibilityProfile`;
- `TaxEngineBinding`;
- Product tax-classification mapping;
- customer/location/tax-ID/exemption evidence references;
- market and registration readiness gates;
- normalized immutable `TaxDetermination`;
- tax accounting, corrections and reconciliation;
- `TaxRemittanceObligation` and remittance state;
- audit/version/effective-time evidence.

The managed tax provider remains responsible for its maintained tax calculation rules, rates, jurisdiction resolution, provider-native calculation objects and service behavior. Provider-native tax objects are supporting evidence and never replace Tunner domain authority.

## Extensibility contract

A new managed tax provider is integrated by:

1. implementing the `ITaxEngine` adapter;
2. registering provider/adapter capability and readiness;
3. configuring a versioned `TaxEngineBinding` for the approved scope;
4. mapping canonical Tunner tax inputs to the provider request;
5. normalizing provider output to the canonical Tunner tax result/evidence contract;
6. implementing idempotency, timeout/unknown-state and reconciliation behavior;
7. passing contract, integration, acceptance, security/privacy and market-readiness tests.

No caller, Connected Product, Invoice, Payment or Finance code may branch on a tax provider name to implement business semantics. Provider-specific behavior stays inside the adapter and provider-evidence boundary.

## Failure and fallback

If a required tax determination has no ACTIVE compatible managed tax engine, the affected checkout/financial operation is blocked. Tunner does not fall back to internally maintained tax tables, guessed rates, stale cached rules or silent zero tax.

An unknown external tax-calculation outcome is reconciled according to the tax-engine contract. Tunner must not blindly calculate through another tax engine when doing so could create inconsistent or duplicate tax evidence.

## Acceptance invariants

- Stripe Tax is the initial managed tax-calculation adapter, not Tunner tax-domain authority.
- `ITaxEngine` is provider-neutral and stable.
- Tunner contains no proprietary global tax-rate/jurisdiction/taxability rules engine.
- future managed tax services can be added without Product-facing contract changes or canonical domain redesign;
- payment routing and tax-engine selection remain independently configurable;
- the same provider may supply both payment and tax capabilities when approved;
- missing tax-engine readiness blocks instead of guessing;
- every financially used tax result is preserved as immutable Tunner `TaxDetermination` evidence with exact engine/binding/provider lineage.

## Closure effect

This PDR resolves **PD-CLOSE-03**. Future replacement, addition or prioritization of a tax provider that preserves these business invariants is an adapter/configuration/ADR decision unless it changes observable tax responsibility, Product contracts or economic behavior.
