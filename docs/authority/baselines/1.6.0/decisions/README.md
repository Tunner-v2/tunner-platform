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
