> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 13 — R&D Source Register

## 1. Purpose

Material engineering decisions must reference current primary sources. This register establishes the researched baseline and includes targeted refreshes through 2026-09-29. Every material item remains subject to the freshness cadence below.

A source entry is not permanently current. The work item/ADR must re-check freshness when implementation occurs.

## 1A. Canonical Tunner project resources

These links identify the project resources used during development. Their presence here does **not** change the authority hierarchy defined by document 00, document 31, or the README.

| Resource | Canonical link | Role / authority |
|---|---|---|
| Tunner FRD / Authority Package root | https://drive.google.com/drive/folders/11vc4sh0EKCc-JEkcjYx2qBo6Zu7bSBCV | Canonical project documentation root. |
| Development Documentation | https://drive.google.com/drive/folders/1sDBE-k3fdIKw2psi7Ul7Ty1u3yNj1FgH | Primary approved development authority package when locked/active. |
| Decisions (ADR/PDR) | https://drive.google.com/drive/folders/1Z8hgTUhtANFFgXUnU71f0pnfpQhab6wi | Approved architecture/product decision records; authority according to document 00 precedence. |
| Architecture interpretation & development guidelines | https://drive.google.com/file/d/1I6PXE-kMIwe3WRrjUwPPckyQFzGU8BTB/view | Architecture interpretation/development guidance subordinate to approved development authority. |
| Tunner Architecture / Master workflow FigJam | https://www.figma.com/board/bdJwXt1U7n3iyluSD21OYK/Tunner-%E2%80%94-Platform-Architecture | Architectural/workflow visualization; must remain synchronized with FRD/development authority and cannot override it. |
| Tunner Platform UX Master | https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master | User-facing UX/visual implementation reference; changeable to satisfy approved requirements. |
| Tunner Admin & Support UX Master | https://www.figma.com/design/3F9tAviUejTYpYvRJt8gM1/Tunner-Admin---Support-UX-Master | Admin/Support UX/visual implementation reference; changeable to satisfy approved requirements. |
| Locked Baseline 1.6.0 archive | https://drive.google.com/file/d/15LKeYkePa5_0RgPlikZNKsp7XAEsI8EW/view | Immutable retained baseline archive. |
| AMD-0001 Pre-P0 governance/context completion | https://drive.google.com/file/d/15MYKfh7zplOWoz80FvgPJOPi-Y6GfI7H/view | Active approved amendment completing pre-P0 governance, skills, context and bootstrap contracts. |
| Current Authority Index (Doc 37) | https://drive.google.com/file/d/1c5Ni8LGgQ-2JxpacoPAopSCO-al23ut2/view | Current effective authority and authorization state. |
| Pre-P0 Implementation Package | https://drive.google.com/drive/folders/15pVwFcj6TfbRQWAcR_54V7-DrrMJ7k28 | Ready-to-materialize AGENTS/skills/governance/project-memory/bootstrap/acceptance templates for TUN-P0-001. |

### Project-resource precedence

Project links are navigation aids, not independent authority. The governing order remains:

1. approved development documentation and approved Product/Architecture decisions;
2. synchronized FigJam architecture/workflows for flow-specific interpretation;
3. approved Figma User/Admin UX as implementation reference;
4. implementation/code, which must conform to the applicable authority.

If a Figma screen conflicts with an approved FRD, the FRD wins and the UI is revised. If FigJam conflicts with an approved FRD, the FRD wins and FigJam is corrected. A link change, rename, or file move must be updated in this register without silently changing the resource's authority role.


## 2. Runtime/platform

### .NET 10 support policy
- Authority: Microsoft
- URL: https://dotnet.microsoft.com/en-us/platform/support/policy
- Verified: 2026-09-20
- Key baseline: .NET 10 LTS, active, support through 2028-11-14.

### .NET 10
- https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview

### C# 14
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14

### EF Core 10
- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Baseline: EF Core 10 LTS aligned to .NET 10.

### Npgsql 10
- https://www.npgsql.org/efcore/release-notes/10.0.html

### PostgreSQL support
- https://www.postgresql.org/support/versioning/
- Baseline: PostgreSQL 18 supported through 2030-11-14; run current minor.

## 3. Cloud-native / operations

### OpenTelemetry .NET
- https://opentelemetry.io/docs/languages/dotnet/
- Baseline: traces, metrics, logs stable.

### .NET observability / Aspire Service Defaults
- https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
- https://learn.microsoft.com/dotnet/aspire/fundamentals/service-defaults

### Docker build best practices
- https://docs.docker.com/build/building/best-practices/

### OpenBao
- https://openbao.org/
- https://openbao.org/docs/what-is-openbao/
- Baseline: cloud-neutral open-source identity-based secrets/encryption manager.

## 4. API/event/security protocols

### OpenAPI contract/tooling
- .NET 10 first-party OpenAPI tooling: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- Baseline executable contract: OpenAPI 3.1 on .NET 10.
- OpenAPI 3.2 specification remains a forward compatibility target; .NET first-party generation starts with .NET 11.
- Specification: https://spec.openapis.org/oas/v3.2.1.html

### AsyncAPI 3.1.0
- https://www.asyncapi.com/docs/reference/specification/v3.1.0

### CloudEvents
- https://cloudevents.io/
- Baseline: 1.0-compatible specification family.

### OAuth Security BCP — RFC 9700
- https://www.rfc-editor.org/rfc/rfc9700.html

### Browser-based OAuth — RFC 10017
- https://www.rfc-editor.org/rfc/rfc10017.html
- Published August 2026; Best Current Practice.

### Authorization Server Metadata — RFC 8414
- https://www.rfc-editor.org/rfc/rfc8414.html

### Protected Resource Metadata — RFC 9728
- https://www.rfc-editor.org/rfc/rfc9728.html

### DPoP — RFC 9449
- https://www.rfc-editor.org/rfc/rfc9449.html

### WebAuthn Level 3
- https://www.w3.org/TR/webauthn/
- W3C Recommendation dated 2026-08-25.

## 5. Secure development / application security


### NIST SP 800-63 Revision 4 — Digital Identity Guidelines
- Authority: NIST
- URLs:
  - https://csrc.nist.gov/pubs/sp/800/63/4/final
  - https://csrc.nist.gov/pubs/sp/800/63/a/4/final
  - https://csrc.nist.gov/pubs/sp/800/63/b/4/final
  - https://csrc.nist.gov/pubs/sp/800/63/c/4/final
- Verified: 2026-09-21
- Baseline: Revision 4 was finalized in July 2025 and supersedes the SP 800-63-3 family. Tunner Identity policy uses Revision 4 as current guidance for proofing, authentication/authenticator management and federation.

### NIST SSDF 1.1
- https://csrc.nist.gov/pubs/sp/800/218/final
- Current final baseline.

### NIST SSDF 1.2 draft
- https://csrc.nist.gov/projects/ssdf/publications
- Draft, not to be represented as final.

### OWASP ASVS 5.0.0
- https://owasp.org/projects/asvs

### OWASP API Security Top 10 2023
- https://owasp.org/projects/api-security-project
- https://api-security.owasp.org/editions/2023/en/0x11-t10/

## 6. Supply chain

### SLSA 1.2
- https://slsa.dev/spec/v1.2/
- Approved current version.

### Sigstore/Cosign
- https://docs.sigstore.dev/
- https://docs.sigstore.dev/quickstart/quickstart-cosign/

### SPDX
- https://spdx.dev/use/specifications/
- Re-check exact current revision before implementing release SBOM.

## 7. Testing

### Testcontainers for .NET
- https://dotnet.testcontainers.org/

### Playwright .NET screenshots
- https://playwright.dev/dotnet/docs/screenshots

## 8. Process

### Scrum Guide
- https://scrumguides.org/download
- Current official version: November 2020.

## 9. Payment compliance

### PCI DSS v4.0.1
- https://blog.pcisecuritystandards.org/just-published-pci-dss-v4-0-1
- v4.0 retired 2024-12-31; v4.0.1 active.
- Future-dated requirements effective 2025-03-31.

## 10. U.S. privacy/communication

### California Privacy Protection Agency
- https://cppa.ca.gov/regulations/
- https://cppa.ca.gov/regulations/ccpa_updates.html
- 2026 regulations include risk assessment/cybersecurity audit/ADMT updates for applicable businesses.

### FTC CAN-SPAM
- https://www.ftc.gov/business-guidance/resources/can-spam-act-compliance-guide-business

### FTC Safeguards Rule
- https://www.ftc.gov/legal-library/browse/rules/safeguards-rule
- Applicability must be legally assessed; do not assume Tunner is a covered financial institution.

## 11. Canada privacy/communications

### PIPEDA — Office of the Privacy Commissioner of Canada
- https://www.priv.gc.ca/en/privacy-topics/privacy-laws-in-canada/the-personal-information-protection-and-electronic-documents-act-pipeda/r_o_p/

### Alberta PIPA
- https://www.alberta.ca/personal-information-protection-act

### British Columbia PIPA
- https://www.oipc.bc.ca/for-private-organizations/

### Quebec private-sector privacy
- https://www.cai.gouv.qc.ca/protection-renseignements-personnels/information-entreprises-privees

### CASL — CRTC
- https://crtc.gc.ca/eng/internet/anti/reg.htm

## 12. Accessibility

### WCAG 2.2
- https://www.w3.org/TR/wcag/
- Engineering target: Level AA.

## 13. Review cadence

- standards/protocol: at implementation and at least per major release;
- legal/compliance: before each launch jurisdiction expansion and on regulatory change signal;
- runtime dependencies: before each milestone/release;
- provider APIs: before integration implementation and provider major-version change;
- security advisories: continuous automated dependency monitoring.

## 14. Messaging, cache, storage and realtime technology closure

### RabbitMQ 4.3 release line
- Authority: RabbitMQ
- URL: https://www.rabbitmq.com/release-information
- Verified: 2026-09-21
- Baseline: RabbitMQ 4.3 is current; use the current supported 4.3 patch at implementation/release time.

### RabbitMQ quorum queues
- Authority: RabbitMQ
- URL: https://www.rabbitmq.com/docs/quorum-queues
- Verified: 2026-09-21
- Baseline: quorum queues are the default replicated/HA queue choice for critical long-lived workloads; publisher confirms remain required for publisher-side safety.

### RabbitMQ .NET client
- Authority: RabbitMQ
- URL: https://www.rabbitmq.com/client-libraries/dotnet
- URL: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
- Verified: 2026-09-21
- Baseline: supported 7.x client family; re-check current patch before implementation.

### ASP.NET Core distributed caching / Redis
- Authority: Microsoft
- URL: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-10.0
- Verified: 2026-09-21
- Baseline: `IDistributedCache`; Redis provider through `Microsoft.Extensions.Caching.StackExchangeRedis`.

### Redis 8 licensing
- Authority: Redis
- URL: https://redis.io/legal/licenses/
- Verified: 2026-09-21
- Baseline: Redis 8+ offers RSALv2, SSPLv1 and AGPLv3 options; license choice/compliance must be recorded in the software dependency register.

### ASP.NET Core SignalR scale-out with Redis
- Authority: Microsoft
- URL: https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0
- URL: https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0
- Verified: 2026-09-21
- Baseline: Redis backplane is the self-hosted scale-out mechanism; deployment topology/latency requirements apply.

### gRPC in browser apps / gRPC-Web
- Authority: Microsoft
- URL: https://learn.microsoft.com/en-us/aspnet/core/grpc/browser?view=aspnetcore-10.0
- Verified: 2026-09-21
- Baseline: browsers cannot call native gRPC directly; gRPC-Web is a browser-compatible option and is scoped selectively, not as Tunner's default public API.

### Cloudflare R2 — SELECTED PRIMARY OBJECT STORAGE
- Authority: Cloudflare R2 documentation
- URLs:
  - https://developers.cloudflare.com/r2/api/s3/api/
  - https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/
  - https://developers.cloudflare.com/r2/reference/data-location/
  - https://developers.cloudflare.com/r2/buckets/object-lifecycles/
  - https://developers.cloudflare.com/r2/buckets/bucket-locks/
  - https://developers.cloudflare.com/r2/reference/data-security/
- Reviewed: 2026-09-21
- Baseline: R2 exposes an S3-compatible API and Cloudflare documents AWS SDK for .NET usage. S3 feature parity is not complete. Location Hints are not residency guarantees; Jurisdictional Restrictions provide jurisdiction guarantees where offered. R2 encrypts objects/metadata at rest. Tunner uses provider-supported bucket lifecycle/lock controls and keeps authoritative retention/lifecycle metadata in PostgreSQL.


## 15. GitHub engineering governance

### GitHub Rulesets
- https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets
- Use Rulesets for protected branches/tags and required controls.

### GitHub Actions OIDC
- https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-cloud-providers
- Prefer short-lived federated cloud credentials over stored long-lived deployment credentials when provider support exists.

### Artifact attestations
- https://docs.github.com/en/actions/concepts/security/artifact-attestations
- Release provenance and SBOM attestations are part of release integrity.

## 16. Email provider baseline

### Resend — SELECTED PRIMARY TRANSACTIONAL EMAIL
- Official .NET SDK announcement: https://resend.com/changelog/announcing-the-dotnet-sdk
- Idempotency keys: https://resend.com/changelog/idempotency-keys
- Webhooks: https://www.resend.com/features/webhooks
- Webhook management/signing: https://resend.com/changelog/managing-webhooks-via-api
- Reviewed: 2026-09-21
- Baseline: use the official `Resend` .NET SDK. Resend idempotency keys are retained by the provider for 24 hours; Tunner's own durable delivery identity/outbox/inbox remains the long-lived duplicate-prevention authority. Verify webhook signatures and persist normalized delivery evidence.

### Mailgun — APPROVED ALTERNATE / DEFERRED
- Send API: https://documentation.mailgun.com/docs/mailgun/api-reference/send/mailgun/messages/post-v3--domain-name--messages
- Webhooks: https://documentation.mailgun.com/docs/mailgun/user-manual/webhooks/webhooks
- Reviewed: 2026-09-21
- Baseline: no MVP implementation. Add only after a governed resiliency, commercial, regional or provider-risk trigger.

## 17. Canada marketing/privacy operational references

### CASL consent
- https://ised-isde.canada.ca/site/canada-anti-spam-legislation/en/getting-consent-send-email
- Commercial electronic messaging requires applicable consent and proof/evidence.

### PIPEDA business guidance
- https://www.priv.gc.ca/en/privacy-topics/privacy-laws-in-canada/the-personal-information-protection-and-electronic-documents-act-pipeda/pipeda-compliance-help/guide_org/
- Privacy management, meaningful consent and breach obligations must be operationalized where applicable.

### Cross-border processing
- https://www.priv.gc.ca/en/privacy-topics/employers-and-employees/outsourcing/02_05_d_57_os_01/
- PIPEDA does not itself prohibit foreign processing; accountable safeguards/contractual controls remain required.

## 18. Local observability reference

### Grafana Docker OpenTelemetry LGTM
- https://grafana.com/docs/opentelemetry/docker-lgtm/
- Baseline use: development, demo and testing only; application telemetry remains OpenTelemetry/OTLP backend-neutral.


## 19. Settlement / funds-holding boundary
- Bank of Canada — *Case scenarios about holding funds* (RPAA): holding analysis depends on whether funds are at rest in a PSP's possession/control and the PSP is indebted to an end user; a one-week delay can still be holding. This is why Tunner does not treat a configured timer or workflow state as proof of payment/settlement finality or regulatory classification.
- Canada Retail Payment Activities Act, s.2 and s.20: payment-function/PSP definitions and safeguarding rules for end-user funds.
- FinCEN administrative rulings on merchant payment processors: U.S. money-transmitter treatment is facts-and-circumstances based; certain merchant payment-processing activity can fall outside money transmission when conditions are met, but there is no universal holding-period safe harbor.

## 20. Global commerce, Stripe and destination-market obligations

### Stripe.net — SELECTED PRIMARY PAYMENT SDK
- Authority: Stripe official SDK repository
- URL: https://github.com/stripe/stripe-dotnet
- Reviewed: 2026-09-21
- Baseline: official Stripe .NET SDK; use stable GA releases only. Tunner wraps it behind provider-neutral payment contracts, uses idempotency for mutating calls and verified/idempotent webhook ingestion.

### Stripe — global availability
- https://stripe.com/global
- Confirms supported Stripe businesses can sell to customers around the world, subject to Stripe availability/restrictions.

### Stripe — supported currencies
- https://docs.stripe.com/currencies
- Stripe supports card charges in many presentment currencies and distinguishes card/account currency, presentment currency and settlement currency. Tunner therefore keeps currency support policy-driven instead of hardcoding one customer currency.

### Stripe — prohibited/restricted businesses and jurisdictions
- https://stripe.com/legal/restricted-businesses
- Market/payment eligibility must respect Stripe high-risk jurisdictions/persons and product restrictions.

### Stripe/Connect — Tunner-managed merchant onboarding and provider-account patterns
- Authorities:
  - https://stripe.com/connect/features
  - https://docs.stripe.com/api/account_links/create?lang=dotnet
  - https://support.stripe.com/questions/know-your-customer-%28kyc%29-requirements-for-connected-accounts
  - https://support.stripe.com/questions/europe-updated-verification-requirements-for-connected-accounts-of-platforms
  - https://stripe.com/connect/pricing
- Verified: 2026-09-21
- Stripe Connect research: when a platform does not use Stripe-hosted onboarding, the platform is responsible for collecting required KYC information and submitting it through the Stripe API; Connect platform controls can manage connected-account onboarding/risk requirements and payouts. PDR-0006 therefore uses Tunner-managed canonical Merchant/KYC onboarding behind provider adapters, while provider verification/capability state remains external evidence and provider-native account/payment objects are not exposed in Connected Product contracts.

### Stripe Tax — tax calculation/readiness research
- Authorities:
  - https://docs.stripe.com/tax
  - https://docs.stripe.com/api/tax/registrations
  - https://docs.stripe.com/tax/supported-countries
- Verified: 2026-09-21
- Baseline research: Stripe Tax can calculate supported taxes and surface threshold/registration information, but a Stripe Tax registration object is not itself government registration. Under PDR-0009, Stripe Tax is the initial managed `ITaxEngine` adapter where supported. Tunner does not maintain proprietary tax-rate/jurisdiction/taxability calculation logic; it separately owns MarketEligibilityPolicy, TaxResponsibilityProfile, TaxEngineBinding, normalized TaxDetermination and evidence/readiness gates. Additional managed tax services may be added later through the same provider-neutral `ITaxEngine` foundation.

### Stripe Tax / indirect tax
- https://stripe.com/tax
- https://stripe.com/guides/introduction-to-sales-tax-vat-and-gst-compliance
- Used for tax-threshold monitoring/calculation/registration workflow research; use does not replace legal/accounting review.

### GDPR territorial scope — Article 3
- https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679
- Non-EU entities can fall within GDPR when offering goods/services to people in the Union.

### EU VAT One Stop Shop
- https://vat-one-stop-shop.ec.europa.eu/one-stop-shop_en
- Evidence that non-EU businesses selling services to EU consumers can have destination VAT obligations and can use the non-Union OSS mechanism where applicable.


### 13.X Financial orchestration and age-assurance closure research (2026-09-21)

- Bank of Canada — **Criteria for registering payment service providers** (updated 2026-06-29): RPAA scope can include initiation, authorization, transmission/reception/facilitation of EFT instructions and clearing/settlement services even when an entity does not hold funds. This is why Tunner uses a market/provider Financial Regulatory Classification Gate rather than assuming “no custody” alone eliminates registration risk.
- Bank of Canada — **Supervisory policy for online marketplaces** (2025-12-19): marketplaces using third-party PSPs end-to-end may be outside PSP scope where payment functions are only incidental, while marketplaces that themselves perform payment functions may be PSPs. The policy also confirms that scheduled merchant funds held by a marketplace can be treated as held end-user funds.
- Stripe — **KYC requirements for connected accounts / Connect platform controls**: where Stripe-hosted onboarding is not used, the platform is required to collect KYC information and submit it to Stripe; platform controls expose onboarding/risk requirements and payout controls.
- Office of the Privacy Commissioner of Canada — **Designing age assurance to be privacy-protective** and identification/authentication guidance: collect only the personal information necessary for age assurance and avoid retention beyond the purpose where not required. PDR-0007 therefore collects DOB for server-side age calculation but makes durable DOB retention policy-controlled rather than automatic.


## Payment checkout, multi-provider readiness and FX — 2026-09-23 refresh

### Stripe supported currencies / settlement
- Authority: Stripe.
- URL: https://docs.stripe.com/currencies
- Verified: 2026-09-23.
- Baseline: Stripe distinguishes card/account currency, presentment currency and settlement currency; supported card charges span many currencies; additional settlement currencies depend on account/country/bank configuration.

### Stripe Payment Element / Checkout presentation
- Authority: Stripe.
- URLs:
  - https://docs.stripe.com/payments/payment-element/migration
  - https://docs.stripe.com/payments/checkout/quickstarts
- Verified: 2026-09-23.
- Baseline: Stripe supports embedded provider-secure payment components as well as Stripe-hosted checkout. Tunner therefore treats presentation mode as a provider-adapter capability while retaining Tunner CheckoutOperation authority.

### Stripe provider-account readiness
- Authority: Stripe.
- URL: https://docs.stripe.com/api/capabilities/object
- Verified: 2026-09-23.
- Baseline: provider capabilities can be pending review/onboarding or disabled. Tunner routing must exclude non-charge-capable accounts rather than attempting to bypass verification/holds.

### Stripe FX evidence
- Authority: Stripe.
- URL: https://docs.stripe.com/api/balance_transactions/object
- Verified: 2026-09-23.
- Baseline: Stripe balance transactions expose settlement currency, fees and `exchange_rate` when conversion applies. Tunner normalizes this into immutable FxConversion/financial evidence.

### Stripe 3-D Secure / authentication
- Authority: Stripe.
- URLs:
  - https://docs.stripe.com/testing
  - https://docs.stripe.com/api/subscriptions/update
- Verified: 2026-09-23.
- Baseline: issuer/provider rules can require 3-D Secure; Stripe recommends automatic/SCA-driven handling. Tunner does not force 3DS universally but must support provider-required `REQUIRES_ACTION`.

### Adyen Web Components and payout-currency evidence
- Authority: Adyen.
- URLs:
  - https://docs.adyen.com/payment-methods/cards/web-component
  - https://docs.adyen.com/account/supported-currencies
- Verified: 2026-09-23.
- Baseline: Adyen provides provider-secure embedded components and supports configurable settlement/payout currencies subject to region/account constraints. This validates Tunner's provider-neutral embedded/hosted presentation and currency-capability model without selecting Adyen as an active provider.

## 21. AI agent orchestration, skills and context engineering

### OpenAI — Harness engineering / repository knowledge as system of record
- Authority: OpenAI Engineering.
- URL: https://openai.com/index/harness-engineering/
- Verified: 2026-09-29.
- Baseline: keep repository knowledge as the durable system of record; use a concise `AGENTS.md` as a map rather than a monolithic instruction manual; context is a scarce resource and structured, verifiable documentation reduces drift.

### OpenAI — Agent Skills
- Authority: OpenAI Developers.
- URL: https://developers.openai.com/api/docs/guides/tools-skills
- Verified: 2026-09-29.
- Baseline: reusable skills use modular `SKILL.md` manifests with optional supporting resources and can be loaded selectively. Tunner adopts a vendor-neutral skill-directory pattern compatible with this model.

### OpenAI — Agents API / orchestration and context management
- Authority: OpenAI Developers.
- URL: https://developers.openai.com/api/docs/guides/agents
- Verified: 2026-09-29.
- Baseline: long-running agent systems require harness-level context management, tool orchestration, state/observability and controlled handoffs rather than relying on one prompt/session history. Tunner keeps these concerns behind its own governance/context/orchestration contracts.

### GitHub — repository indexing for coding-agent context
- Authority: GitHub Docs.
- URL: https://docs.github.com/en/copilot/concepts/context/repository-indexing
- Verified: 2026-09-29.
- Baseline: repository indexing/semantic retrieval improves codebase context discovery. Tunner therefore requires an independently governed repository/authority index instead of repeated full-repository prompt loading.

### Anthropic — effective context engineering
- Authority: Anthropic Engineering.
- URL: https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents
- Verified: 2026-09-29.
- Baseline: context is finite and should be curated for high signal; long-horizon work benefits from compaction, structured notes and deliberate context management. Tunner uses bounded context plus governed escalation rather than assuming larger context windows remove the need for curation.

## 22. P0 governance tooling implementation research

### Microsoft — System.CommandLine
- Authority: Microsoft Learn / NuGet.
- URLs:
  - https://learn.microsoft.com/en-us/dotnet/standard/commandline/
  - https://www.nuget.org/packages/System.CommandLine
- Verified: 2026-09-29.
- Baseline: `System.CommandLine` is the Microsoft-supported CLI library family for .NET tools. Stable 2.0.12 was current on the verification date; Tunner pins the current stable 2.0.x patch when P0 implementation begins rather than adopting 3.0 preview packages.

### YamlDotNet
- Authority: NuGet package registry / upstream project.
- URL: https://www.nuget.org/packages/YamlDotNet
- Verified: 2026-09-29.
- Baseline: stable 18.1.0 was current on the verification date. Tunner uses the current stable 18.x patch for human-authored governance YAML unless implementation research identifies a material incompatibility.

### JsonSchema.Net
- Authority: NuGet package registry / upstream project.
- URL: https://www.nuget.org/packages/JsonSchema.Net
- Verified: 2026-09-29.
- Baseline: stable 9.4.0 was current on the verification date. Tunner uses the current stable 9.x patch for JSON Schema validation unless implementation research identifies a material incompatibility.
