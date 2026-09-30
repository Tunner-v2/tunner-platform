> **Tunner Development Authority Package**
>
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  
> **Primary implementation authority:** Approved Development Documentation  
> **Secondary authority:** Tunner FigJam architecture and workflow contracts  
> **Tertiary reference:** Figma Admin/Support UX and Tunner Platform UX  
> **Business safeguard:** No development document may silently override an explicit Product Owner business decision or Tunner domain-ownership boundary. A conflict creates a governed Product Decision / Amendment gate.

# Tunner Development Documentation — Master Index

## 1. Purpose

This repository package is the parent development authority for building Tunner with human engineers and AI development agents. It translates the Tunner business model and FigJam architecture into an implementable, governed, testable and transferable engineering system.

The documentation is intentionally separated into focused authorities rather than a single oversized specification. Every development task must be traceable to the applicable documents, architecture flow IDs, Product Owner decisions, acceptance criteria, Git history and test evidence.

## 2. Locked baseline decisions

| Area | Decision |
|---|---|
| Runtime | .NET 10 LTS |
| Language | C# 14 |
| Persistence baseline | PostgreSQL 18, current supported minor |
| Development environment | Docker-first |
| Deployment cloud | Primary compute/database cloud remains undecided; approved managed providers may be selected independently behind governed adapters |
| Initial deployment topology | Cohesive modular platform with API + Worker; no microservice-per-domain rule |
| Internal async messaging | PostgreSQL transactional outbox/inbox + RabbitMQ 4.3 current supported patch; broker never replaces the authoritative transaction/outbox |
| Distributed cache / realtime backplane | Redis 8.x through StackExchange.Redis; cache/backplane only, never business authority |
| Object storage | Cloudflare R2 primary object store behind S3-compatible `IObjectStorage`; official AWS SDK for .NET S3 client; environment-isolated buckets |
| Browser realtime | ASP.NET Core SignalR; Redis backplane when horizontal scale requires it |
| Browser typed RPC | gRPC-Web selectively for Tunner-owned web clients; not the default public Product contract |
| Internal RPC | In-process module calls by default; native gRPC only across justified process/service boundaries |
| Public Product API | HTTPS REST/JSON + OpenAPI; SDKs wrap this contract |
| Observability | OpenTelemetry logs, metrics and traces |
| Local observability | Grafana OTEL-LGTM for development/test only; production backend remains cloud/ops decision |
| Secrets | Vault-only secret storage; OpenBao is the local/cloud-neutral reference implementation |
| Environments | Local Dev + ephemeral Test; Connected Products get isolated Sandbox, Staging and Production environments |
| SDK strategy | Contract-first; .NET reference SDK; TypeScript next/parallel for strong MVP; additional SDKs by demand |
| Connected Product commercial model | Every Product integrates with Tunner as a service consumer. Tunner records the service, commercial, operational, payment, settlement and audit relationships required to deliver Tunner capabilities (PDR-0008). |
| Current legal/home jurisdiction | Toronto, Ontario, Canada |
| Commercial reach | Global eligible markets; availability is policy/provider/compliance gated, not limited to Canada |
| Currency model | Product presentment currency is configuration-driven by explicit catalog price + CurrencySupportPolicy; provider settlement, Product payable and Product payout currencies are independently resolved and every actual FX conversion is immutable evidence |
| Delivery process | Scrum with governed milestones, sprints, work items, defects, reopens, amendments and releases |
| Git | Protected trunk-based development with short-lived branches |
| UI implementation | Guided, contextual workflows; no database-style CRUD as the primary operator experience |
| Frontend runtime | React + TypeScript + Vite + TailAdmin React; User and Admin/Support are separate applications sharing governed components/tokens |
| Browser backend pattern | Separate ASP.NET Core BFFs for User UI and Admin/Support UI; secure HttpOnly cookie sessions; access/refresh tokens are not exposed to browser JavaScript |
| Identity/OAuth implementation | ASP.NET Core Identity + OpenIddict; passkeys/WebAuthn, password, TOTP MFA, recovery and step-up are first-class identity capabilities |
| External identity providers | Google + Microsoft first adapters through governed external-login provider contracts |
| Payment provider | Stripe is the initial production payment provider through the official Stripe.net SDK; Tunner provider contracts remain authoritative and additional pre-approved providers/accounts may be activated for continuity routing |
| Managed tax engine | Tunner does not implement proprietary tax-rate/jurisdiction/taxability calculation. `ITaxEngine` is the stable provider-neutral foundation; Stripe Tax is the initial managed tax-service adapter, and additional approved managed tax services can be added later as adapters without changing Checkout, Product API/SDK, Invoice, Payment or Finance business contracts. |
| Checkout orchestration | Tunner owns CheckoutOperation, entry URL, commercial/tax review, state and result. Provider adapters may use PCI-safe embedded components or an approved provider-hosted flow; provider routing is resolved before collection and provider choice remains hidden from Connected Products. |
| Git hosting / CI | GitHub + GitHub Actions with Rulesets, required checks, reusable workflows, OIDC deployments and artifact attestations |
| Transactional email | Resend primary production adapter through the official .NET SDK; Mailpit local/test sink; Mailgun approved alternate but deferred |
| Data residency | No strict residency promise before deployment-region selection; every Product/environment carries governed `data_region` / residency-policy capability |
| Product finance & provider separation | ProductPayable and derived ProductPayableBalance are Product + payable-currency scoped. `ProductKycCase` is Tunner-owned Product compliance. Tunner provider merchant/accounts, ProviderAccountBinding, settlement currency, treasury FX and routing are separate execution/reconciliation infrastructure and never partition or re-denominate Product economics (PDR-0006). |
| Financial-services regulatory posture | Market/provider activation must pass the applicable regulatory/provider/compliance gate. Tunner Finance is limited to the payment, payable, settlement, payout, reconciliation and accounting-control capabilities defined by the active service contracts; any additional regulated financial capability requires explicit approval before activation. |
| Payout finality | Approved Settlement is immutable; Payout snapshots ProductPayoutDestinationVersion; provider route is resolved separately; replacement external payout uses new payout/idempotency identity; PayoutReceipt is per payout attempt/finality; PayoutProcessingCase is exception/reconciliation-only. |
| FX economic treatment | PDR-0010 locks a configurable, versioned `FxEconomicTreatmentPolicy` with cause-based defaults: customer/issuer FX is external to Tunner, Tunner treasury/provider-settlement FX is Tunner-borne by default, Product-requested payout FX is Product-borne by default, and accounting/statutory translation is reporting-only. ProductCommercialAgreement may explicitly override Product-affecting allocation through guided configuration and approved policy versions. |
| Account eligibility | Tunner Accounts are **18+ only**; registration collects DOB and performs authoritative server-side age calculation; under-18 registration is denied (PDR-0007) |
| UI source | Figma is reference, not business/backend authority |
| Internet research | Mandatory before implementation of a material standard, library, compliance control, provider, protocol or architectural decision |
| AI behavior | No unapproved assumptions. Ambiguity becomes a scoped governance block and Product Decision request. |

### Global commerce interpretation

Toronto, Ontario, Canada is Tunner's current corporate/home jurisdiction for the active model. Tunner may offer eligible Products to customers globally when provider, card network/issuer, sanctions, Product and applicable destination-market requirements permit the transaction.

Product presentment currency is explicit Product catalog/configuration, not inferred from the customer's card currency. An eligible card can be used when the selected provider route supports the configured presentment currency; issuer/cardholder FX is external unless Tunner/provider actually performs a recorded conversion.

Tunner currently has an active CAD-denominated Canadian bank account for provider settlement. Provider settlement currency is configuration-driven. A future USD account becomes active only after it is created, verified and bound. ProductPayable remains in the Product commercial transaction/presentment currency and is independent of provider settlement currency; provider settlement FX is separate Tunner treasury/clearing evidence. Product payout currency is independently configured and any actual conversion is recorded as immutable `FxConversion` evidence. Country/market eligibility remains versioned policy.

Global selling can create destination-country privacy, consumer, marketing and indirect-tax obligations. Market expansion is therefore controlled through a jurisdiction/market policy registry and tax-obligation monitoring rather than by hardcoding Canada-only compliance. Tunner delegates tax calculation to an approved managed tax service through `ITaxEngine`; it does not maintain its own tax-rate or jurisdiction rules. The active payment provider's tax service may be preferred when it is capability-compatible, but tax-engine binding remains separately configurable and extensible.

### Selected external-provider boundary

Stripe, Resend and Cloudflare R2 are selected implementation providers, but none becomes Tunner business authority. Tunner owns canonical payment, notification and object-metadata state. Provider SDK objects, webhooks and dashboards are normalized through adapters and may not be persisted as the domain model.

- Stripe: initial payment provider; stable official `Stripe.net` release only.
- Resend: primary production transactional-email provider; official `Resend` .NET SDK.
- Mailgun: approved alternate email provider; no MVP implementation unless a governed trigger justifies it.
- Cloudflare R2: primary object store through its S3-compatible API and official AWS SDK for .NET.
- Mailpit: local/test email capture only.

## 3. Authority order

For development execution:

1. **Approved development documentation**
2. **Approved amendments and Product Decisions**
3. **Tunner FigJam architecture / `TUN-FLOW-*` contracts**
4. **Figma Admin & Support UX reference**
5. **Figma User/Account UX reference**
6. **Implementation details and code**

An explicit Product Owner decision is constitutional business authority. If a development document conflicts with such a decision, development does not “choose” one: the affected item is blocked and the documentation is amended.

Google Drive UI/UX documents are explicitly outside development scope.

## 4. Document map

| File | Authority |
|---|---|
| `00_Authority_Change_Control_and_Documentation_Governance.md` | Source hierarchy, amendments, decisions, traceability |
| `01_Engineering_Technology_Foundation.md` | Runtime, architecture topology, persistence, Docker, observability |
| `02_Repository_Solution_and_Module_Architecture.md` | Repository layout, modules, dependency rules, code organization |
| `03_Governed_Development_Lifecycle_Scrum_Git.md` | Scrum + Git + branch/PR/defect/reopen/milestone governance |
| `04_Governance_and_Context_Tooling_Specification.md` | `tunner-governance`, `tunner-context`, `tunner-dev`, `tunner-test` |
| `05_Versioning_Compatibility_and_Release_Management.md` | Platform/API/SDK/event/config/document versions and compatibility |
| `06_Environment_Configuration_Secrets_and_Deployment.md` | Dev/Test/Sandbox/Staging/Production, configuration, vault, ports |
| `07_API_SDK_Event_and_Connected_Product_Foundation.md` | Contract-first APIs, SDK rules, environments, webhooks/events |
| `08_Security_Privacy_Compliance_and_Global_Market_Governance.md` | Secure SDLC; Canada/Ontario home-jurisdiction baseline plus global destination-market compliance overlay |
| `09_Testing_Acceptance_Evidence_and_Quality_Gates.md` | Test pyramid, evidence, release packs, environment testing |
| `10_UI_UX_Components_Widgets_and_Plugins_Development_Rules.md` | Guided UX, components, consent/disclosure, UI blocking rules |
| `11_Development_Planes_Milestones_and_P0_Backlog.md` | Development planes and executable P0 foundation backlog |
| `12_AI_Development_Protocol_Context_and_Project_Memory.md` | AI operating protocol, context packs, handoff and assumption control |
| `13_RD_Source_Register.md` | Current authoritative research sources and review cadence |
| `14_Glossary_and_Canonical_Terms.md` | Canonical Tunner engineering/business terminology |
| `15_Runtime_Communication_and_Infrastructure_Technology_Scope.md` | Exact technology scope: API vs gRPC/gRPC-Web vs SignalR, RabbitMQ, Redis, storage and runtime topology |
| `16_Full_Project_Documentation_Closure_and_Gap_Register.md` | Whole-project documentation closure gate and Product Decision register |
| `17_Account_Identity_and_Session_FRD.md` | Account, auth, MFA/passkeys, sessions, recovery and closure |
| `18_Billing_Account_and_Delegated_Authority_FRD.md` | Billing Account, membership, authority, profile and payer migration |
| `19_Connected_Product_and_SDK_Lifecycle_FRD.md` | Product/environment/client/capability/SDK/integration lifecycle |
| `20_Commercial_Catalog_Pricing_Agreement_and_Fees_FRD.md` | Billing-safe catalog, pricing, agreements, fees and commercial snapshots |
| `21_Billing_Subscription_Checkout_Renewal_and_Dunning_FRD.md` | Billing Subscription states, checkout, renewal, dunning and lifecycle changes |
| `22_Global_Market_Tax_Product_Compliance_and_Remittance_FRD.md` | Global market eligibility, Product compliance/KYC, tax and remittance |
| `23_Provider_Account_and_Routing_Operations_FRD.md` | Tunner provider merchant/accounts, capabilities, routing, credentials and health |
| `24_Payments_Refunds_Disputes_and_Reconciliation_FRD.md` | Payment finality, retries, refunds, disputes and reconciliation |
| `25_Invoices_Credits_Receivables_and_Collections_FRD.md` | Invoices, credits, allocations, receivables and collections |
| `26_Finance_Ledger_Accounting_Settlement_and_Payout_FRD.md` | Tunner financial-control ledger, Product fee/payable, settlement, payout and reconciliation |
| `27_Notifications_Communications_and_Preferences_FRD.md` | Notification intents, templates, Resend delivery, in-app and consent/preferences |
| `28_Support_Risk_Audit_and_Internal_Operations_FRD.md` | Support, risk, audit, approvals, workforce access/JIT/SoD |
| `29_Privacy_Data_Lifecycle_and_Processing_Governance_FRD.md` | Privacy requests, retention, processing governance and incidents |
| `30_Platform_Reliability_Change_DR_and_Automation_FRD.md` | Reliability, incidents, change, DR, workers/timers/Governor/AI |
| `31_UI_UX_Screen_and_Control_Center_Requirements_Matrix.md` | Required user/admin screens, actions, states and UI_REQUIRED gate |
| `32_Domain_API_Event_Data_and_Policy_Contract_Catalog.md` | Canonical API/event/error/data/policy/adapter contracts |
| `33_Production_Readiness_Compliance_and_Release_Gates.md` | Production/security/compliance/finance/provider/release gates |
| `34_Architecture_to_Development_Traceability_Matrix.md` | FigJam-to-development coverage and explicit contract-marker closure |
| `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md` | Mandatory multidisciplinary AI role contracts and review responsibilities |
| `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md` | Implementation-grade P0 bootstrap/governance/context/orchestration contract |
| `37_Current_Authority_Index.md` | Current effective authority, authorization state and bootstrap-generated resources |
| `Pre-P0 Implementation Package/` | Ready-to-materialize AGENTS.md, 20 skills, governance/project-memory templates, bootstrap runbook and P0 acceptance pack |
| `decisions/` | Accepted ADRs, Product Decision Records and approved amendments; immutable decision lineage |

The separately maintained **Tunner Architecture Flowchart Interpretation and Development Guidelines** remains mandatory reading and is intentionally external to this package because it governs FigJam interpretation across documentation revisions. Current Drive authority: https://drive.google.com/file/d/1I6PXE-kMIwe3WRrjUwPPckyQFzGU8BTB/view

**PD-CLOSE-05 — FX economic responsibility is RESOLVED by PDR-0010.** FX treatment is configuration-driven through versioned `FxEconomicTreatmentPolicy` with an approved cause-based default and guided configuration. Provider-settlement/treasury FX, Product payout FX, customer/issuer FX and statutory/accounting translation remain distinct contexts; every Product-affecting override is explicit, previewable, effective-dated and tied to `ProductCommercialAgreement`. No open Product Owner commercial decision remains in the current documentation closure. Baseline 1.6.0 closure passed on 2026-09-29; P0 is authorized and P1–P8 remain gated by the P0 feature-development enablement gate.

### Pre-P0 implementation package

The complete pre-P0 materialization pack is maintained at:

https://drive.google.com/drive/folders/15pVwFcj6TfbRQWAcR_54V7-DrrMJ7k28

It contains the concise `AGENTS.md` template, all 20 role `SKILL.md` contracts, governance-record templates, project-memory templates, repository blueprint, bootstrap/self-hosting runbook, context-cost policy, GitHub bootstrap policy, P0 Definition of Ready/Done and fresh-agent recovery test. During `TUN-P0-001`, these are materialized into the repository and then governed by the repository control plane.


### PR approval and continuous development

Under **AMD-0002**, required PR approval gates integration into protected `main`; it does not normally stop local/branch development. Governance must continue all eligible work while approval is pending and block only scopes whose dependency explicitly requires `MERGED_TO_MAIN` or another genuine human-authority gate.

## 5. Development start rule

**DOCUMENTATION BASELINE 1.6.0 IS ACTIVE AND LOCKED. P0 IS AUTHORIZED.** The full-project closure gate in document 16 has passed for this baseline.

The first authorized implementation activity is **P0 — Governance & Development System**. P1–P8 feature/platform development must not begin until the P0 feature-development enablement gate in document 11 passes with machine-readable evidence. This prevents product implementation from depending on chat memory, unenforced workflow rules or ad-hoc orchestration.

## 6. Development sequence after documentation activation

Documentation activation authorizes **P0 — Governance & Development System first**.

1. P0 governance/control-plane bootstrap: governance schemas, context engine, agent skills, governed orchestration, project memory/state, test/evidence and developer automation.
2. Pass the P0 feature-development enablement gate in document 11.
3. P1 technical/platform foundation.
4. P2 functional Account & Identity.
5. P3 Connected Product + SDK foundation in parallel according to dependencies.
6. Shared platform services.
7. Billing Account, Commercial and Billing Subscription core.
8. Operational maturity.
9. Full Finance & Accounting.
10. Production-readiness hardening.

P1–P8 must not bypass P0 merely because documentation is active.

## 7. Definition of “done”

“Code compiles” is never sufficient.

A governed work item is done only when its required implementation, documentation, automated tests, security/contract checks, evidence, Git/PR trace and applicable Product Acceptance are complete.

## 8. Core engineering maxim

> **Simplify topology, not business truth. Reuse infrastructure, not business authority.**
