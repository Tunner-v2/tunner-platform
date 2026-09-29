# PDR-0010 — Configurable FX Economic Treatment and Guided Configuration

**Status:** APPROVED  
**Decision date:** 2026-09-25  
**Owner:** Product Owner  
**Scope:** Currency conversion economics, Product payout preferences, ProductCommercialAgreement FX treatment, Finance configuration UX and evidence

## Decision

Tunner resolves FX economic responsibility through a versioned `FxEconomicTreatmentPolicy`. FX treatment follows the cause of the conversion and is configurable without code changes. Tunner provides a platform **Standard Cause-Based** policy so normal Product activation does not require repetitive manual policy creation. Product-specific commercial overrides are allowed only through explicit, versioned `ProductCommercialAgreement` authority.

## Standard Cause-Based defaults

| FX context | Default treatment | Rule |
|---|---|---|
| Customer/cardholder or issuer conversion outside Tunner | `EXTERNAL_TO_TUNNER` | Customer/issuer economics remain outside Tunner unless Tunner itself incurs an evidenced charge. |
| Provider settlement / Tunner treasury conversion caused by Tunner routing or bank currency | `TUNNER_BORNE` | Tunner bears the actual treasury conversion economics by default; provider settlement currency never re-denominates or silently reduces ProductPayable. |
| Product-requested payout conversion because payout currency differs from Settlement/payable currency | `PRODUCT_BORNE` | Product bears actual externally evidenced payout conversion cost by default. |
| Statutory/accounting translation with no actual commercial conversion | `REPORTING_ONLY` | Preserve rate/source/value evidence; do not create an economic deduction merely because reporting currency differs. |

## Configurable override modes

For a Product-affecting FX context, an authorized `ProductCommercialAgreement` may select:

- `PRODUCT_BORNE`;
- `TUNNER_BORNE`;
- `SHARED` with explicit Product/Tunner percentage split totaling 100%.

Where commercially required, a policy may also define approved Tunner subsidy/cost caps or Product cost caps. `EXTERNAL_TO_TUNNER` and `REPORTING_ONLY` are contextual/non-economic modes and are not used to hide a real Tunner/Product allocation.

Only externally evidenced cost components are allocatable. The policy may identify eligible components such as explicit provider FX fee, externally evidenced spread/economic cost, bank fee and rounding/variance evidence. Tunner must not fabricate a spread, estimate an actual cost after the fact, or charge the same provider/bank cost twice.

## Policy resolution and versioning

Resolution precedence is:

1. explicit effective `ProductCommercialAgreement` override;
2. narrower active Product/environment FX policy when approved;
3. platform Standard Cause-Based policy.

Resolution uses business effective time and explicit scope. Overlapping/ambiguous active policies are invalid. Missing/ambiguous required resolution blocks the affected conversion rather than guessing. Historical `FxConversion`, Settlement, Payout, journal and agreement evidence retains the exact policy version used and is never rewritten by later configuration.

## Guided configuration requirement

FX/payout configuration is designed for low-friction operator use and must not require editing raw JSON or financial database state as the normal workflow. The guided flow must:

1. select Product/environment/agreement and FX context;
2. show the effective Standard Cause-Based default;
3. offer understandable templates such as **Product bears**, **Tunner absorbs**, and **Shared/custom** when authorized;
4. resolve/show eligible source/target currencies, provider routes, payout corridors and destination readiness;
5. expose split percentages, cost-component scope and optional approved caps only when applicable;
6. preview example/indicative source amount, conversion, allocation and expected payout with clear estimate-vs-actual labeling;
7. validate unsupported corridors, contradictory scopes, invalid splits/caps and missing dependencies before activation;
8. show current vs candidate versions, effective date and impacted scope;
9. require reason, approval/step-up/SoD where commercial-governance policy requires it;
10. activate prospectively and preserve immutable audit/version evidence.

Advanced/custom controls use progressive disclosure. The standard path should normally be completed by accepting the recommended default/template and selecting eligible payout preferences.

## Product self-service boundary

A Connected Product may configure `ProductPayoutPreference` within allowed currencies/destinations/corridors and may view the effective FX treatment and an indicative payout/FX preview. Product self-service cannot alter commercial FX responsibility unless the effective `ProductCommercialAgreement` and authorization policy explicitly permit that capability. Provider routing and Tunner treasury configuration remain Tunner-internal.

## Accounting and evidence invariants

- ProductPayable remains Product + payable-currency scoped and is not re-denominated by provider settlement currency.
- Provider-settlement/treasury FX is separate Tunner clearing/treasury evidence.
- Product payout FX is separate from ProductPayable calculation and is applied at the payout path under the resolved policy.
- Every actual conversion creates immutable `FxConversion` evidence with source/target amounts/currencies, actual effective rate, provider/bank reference, timestamps and available fee/spread evidence.
- Indicative quotes/previews never become actual ledger evidence.
- Returned/reversed payouts create new conversion/evidence lineage when another real conversion occurs; original FX evidence remains immutable.

## Acceptance

This decision resolves **PD-CLOSE-05**. No Product Owner commercial ambiguity remains for FX economic responsibility. Any future new FX treatment form must extend the policy/configuration model through an approved amendment/PDR rather than introducing hidden application logic.
