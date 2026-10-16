# Tunner Architecture Flowchart Interpretation & Development Guidelines

**Document status:** Development Interpretation Authority  
**Applies to:** Tunner Platform Architecture & Master Workflow  
**Primary architecture source:** Tunner FigJam architecture board  
**Audience:** Product owners, solution architects, backend engineers, frontend engineers, QA engineers, DevOps/SRE engineers, security engineers, AI coding agents, reviewers, and future Tunner contributors  
**Purpose:** Prevent over-engineering, duplicated implementations, authority drift, and incorrect simplification while translating Tunner architecture flowcharts into working software.

---

## 1. Purpose of This Guideline

The Tunner architecture flowcharts are intentionally detailed because they must preserve business authority, external-side-effect safety, financial correctness, traceability, operational recoverability, auditability, and future extensibility.

That detail must **not** be interpreted as an instruction to create one service, database, queue, worker, manager, registry, coordinator, or deployment unit for every diagram box or every flowchart section.

This guideline defines how the architecture must be interpreted during PRD/FRD preparation and software development.

The development objective is:

> **Preserve rich business behavior and strict authority boundaries while keeping the implementation topology as simple as practical.**

The correct implementation philosophy is:

- strong business/domain boundaries;
- simple deployment topology initially;
- shared infrastructure for cross-cutting mechanics;
- owner-domain code for business rules;
- explicit contracts and versioning where the business requires them;
- automation where it removes routine work;
- no generic platform abstraction before repeated real use cases justify it;
- no duplicated business authority;
- no hidden manual operational dependency;
- no simplification that removes legitimate business states or safety controls.

---

# 2. Source-of-Truth Hierarchy

When implementation decisions are unclear, use this precedence order.

## 2.1 Business authority comes first

The Tunner business model and ownership boundaries are non-negotiable.

A technical shortcut, framework convention, UI convenience, provider behavior, database design, or AI-generated suggestion must never change the business authority defined by Tunner.

## 2.2 Canonical detailed architecture flows

Detailed domain flows are the authoritative behavioral interpretation of the business model.

Examples include:

- Identity flows
- Billing Account flows
- Subscription flows
- Commercial flows
- Finance/Ledger flows
- Provider flows
- Notification flows
- Support flows
- Risk and Compliance flows
- Audit and Governance flows
- Internal Access flows
- Platform Reliability flows
- Connected Product Integration flows
- Data Lifecycle flows
- Privacy flows

## 2.3 Functional architecture sections

A domain-level “Functional Architecture” section explains the domain’s components, relationships, major responsibilities, and boundaries.

It does not automatically define separate runtime services.

## 2.4 Overview/reference flows

Some earlier diagrams intentionally show a complete journey across multiple domains.

They are useful for understanding the full customer/system journey but do not create duplicate business engines.

When an overview references a canonical detailed flow, the detailed flow controls implementation behavior.

## 2.5 Provider-specific examples

Stripe and SendGrid diagrams are concrete adapter/provider examples.

They do not create separate Stripe or SendGrid business domains.

Generic Tunner Provider, Payment/Finance, Notification, and Integration contracts remain authoritative.

---

# 3. Strict Business Rules — Never Simplify These Away

Implementation simplification is encouraged only when it does not change the business.

The following rules must be preserved even if they appear to make a flow longer.

## 3.1 Connected Product business-state boundary

Tunner does **not** own Connected Product:

- organizations;
- workspaces;
- teams;
- Product memberships;
- Product roles;
- feature entitlements;
- Product user/feature usage;
- session consumption;
- Product operational state.

Connected Products remain authoritative for these concepts.

Tunner may retain purpose-limited identifiers, references, immutable commercial snapshots, integration evidence, or audit evidence required to perform Tunner-owned responsibilities.

Those records do not transfer Product business-state authority to Tunner.

### Development consequence

Do not create Tunner tables, APIs, or background jobs that behave as the authoritative workspace/team/entitlement system for a Connected Product.

A reference such as `product_workspace_id` may exist when needed for correlation, but it remains a reference to Product-owned state unless a specific Tunner contract explicitly says otherwise.

---

## 3.2 Billing Account membership is not Product membership

Tunner can own Billing Account membership and delegated billing authority.

That must not be confused with Connected Product workspace/team membership.

These may refer to the same human in some scenarios, but they are different business concepts and have different authority.

---

## 3.3 Subscription is a commercial/billing concept

Tunner owns Tunner subscription commercial and billing lifecycle.

The Connected Product owns operational feature/entitlement consequences.

A successful Tunner subscription operation may produce a Product handoff/result, but Tunner must not directly mutate Product-local entitlement state unless a future explicit contract changes that ownership.

---

## 3.4 Product metering boundary

Product usage and consumption metering are not Tunner billing responsibilities by default.

A Connected Product may have its own usage model.

Tunner technical telemetry and protection counters may measure:

- API request rate;
- latency;
- errors;
- saturation;
- concurrency;
- integration delivery lag;
- security signals;
- capacity usage;
- abuse/cost-protection signals.

These technical counters must not silently become:

- Product feature usage;
- Product session usage;
- Product consumption billing;
- Product entitlement authority.

---

## 3.5 External side-effect safety

Payment, provider, email, webhook, verification, transfer, payout, or similar external calls cannot be treated like local database writes.

When an external outcome is uncertain, preserve an explicit state such as:

- `PROCESSING`
- `WAITING_EXTERNAL`
- `UNKNOWN_EXTERNAL_STATE`

Do not retry, fail over, compensate, or recreate the operation until the owning domain can prove that doing so will not duplicate the external side effect.

---

## 3.6 Payment preflight rule

Required foreground dependencies must be validated before submitting an external payment.

For example, if the selected merchant/provider route is unavailable, ineligible, misconfigured, or cannot safely accept the operation, Tunner must not send the charge and “repair” the problem afterward.

---

## 3.7 Event delivery does not equal business application

The following are separate facts:

1. the publishing domain committed a business fact;
2. an event was accepted for delivery;
3. a webhook was successfully delivered;
4. the Connected Product acknowledged the message;
5. the Connected Product successfully applied its own business rule.

Do not collapse these into a single “success” status.

---

## 3.8 Redrive/replay does not recreate the original business action

Redriving a webhook/event delivery must reuse the existing event/delivery lineage.

It must not re-run the original:

- subscription activation;
- payment;
- refund;
- notification intent;
- identity change;
- other originating business operation.

---

## 3.9 Browser return is not business authority

Redirects and browser return URLs are user-experience mechanisms.

A Product must confirm authoritative Tunner state server-side before granting Product entitlement or taking another sensitive business action.

---

## 3.10 Historical semantics must remain immutable

When a price, policy, fee, tax rule, provider configuration, workflow definition, protection policy, commercial agreement, or similar contract changes, historical records must continue to resolve against the version that was effective when the event occurred.

Do not rewrite history by resolving old transactions through current configuration.

---

# 4. How to Read a Flowchart

A flowchart describes **behavior and authority**, not source-code class count.

## 4.1 Shape interpretation

Typical interpretation:

- **Process rectangle:** a responsibility/action.
- **Decision diamond:** a business or technical decision whose outcomes have materially different behavior.
- **Connector:** ordering/dependency/decision path.
- **Functional Architecture box:** logical capability or responsibility.
- **Registry:** durable versioned configuration/evidence set; not necessarily a separate service.
- **Manager/Coordinator:** application responsibility coordinating owner-domain actions; not automatically a microservice.
- **Governor:** continuous conformance responsibility; not permission to directly mutate foreign-domain data.
- **Read Model/Projection:** derived query representation; not the authoritative source of truth.

## 4.2 One box does not equal one class

A box may be implemented as:

- a method;
- application service;
- command handler;
- domain policy;
- validator;
- state-machine transition;
- background handler;
- repository operation;
- integration adapter;
- shared platform primitive.

Choose the smallest implementation abstraction that keeps the business clear.

## 4.3 One flow does not equal one microservice

A flow can cross multiple modules.

The flow documents the business journey.

Each participating module should execute only the responsibilities it owns.

---

# 5. What Counts as Over-Engineering

Over-engineering means adding technical abstraction, deployment boundaries, infrastructure, frameworks, or generalized engines that exceed current business needs and increase failure/debugging/maintenance cost without preserving additional required behavior.

## 5.1 One architecture section = one microservice

**Over-engineered:** creating 100+ services because the architecture contains many sections.

**Correct treatment:** sections are logical behavior contracts.

Start with a cohesive modular application and split only when there is demonstrated benefit.

---

## 5.2 Separate database per logical box

**Over-engineered:** assigning a separate physical database to every Manager, Registry, or domain sub-flow immediately.

**Correct treatment:** preserve logical data ownership first.

A common physical database can initially contain module-owned schemas/tables when appropriate.

Rules:

- each table has one owning module;
- foreign modules do not write the owner's tables directly;
- cross-module access occurs through defined application/domain contracts, events, or read projections;
- physical extraction can occur later without changing business semantics.

---

## 5.3 Separate queue for every event

**Over-engineered:** creating a different broker/queue infrastructure for every event type.

**Correct treatment:** use the shared execution/event infrastructure and route events by contract/subscription.

Isolation should be added where throughput, risk, provider behavior, or fault containment requires it.

---

## 5.4 Reimplementing retry logic inside each domain

Retry/backoff/DLQ concepts appear in many diagrams because each domain must define the semantic conditions under which retry is allowed.

That does **not** mean each module should build a new retry engine.

### Shared infrastructure owns

- scheduling;
- backoff timing;
- queue mechanics;
- delivery attempts;
- dead-letter storage;
- leases;
- retry counters;
- redrive mechanics.

### Owning domain decides

- whether retry is semantically safe;
- whether a new external attempt is permitted;
- when an unknown outcome requires reconciliation;
- what idempotency boundary applies.

---

## 5.5 Reimplementing approval workflows

Approval, step-up, segregation-of-duties, and governed actions appear in many flows.

Do not create a different approval framework per domain.

Use shared approval/audit capabilities.

The owning domain provides:

- target resource;
- action type;
- candidate state;
- risk/policy context;
- monetary/customer impact;
- expected versions;
- execution callback/command.

The shared governance capability handles:

- approval object;
- approver eligibility;
- SoD;
- decision evidence;
- expiry;
- step-up binding;
- audit history.

---

## 5.6 Building the generic Workflow DSL too early

The architecture contains a future-capable Workflow Definition JSON DSL, durable workflow runtime, and continuous Governor.

These are valid architecture capabilities, but they are the highest risk for premature abstraction.

### Do not start development by building a general workflow platform.

Default sequence:

1. implement owner-domain invariants in compiled/tested C#;
2. implement orchestration with typed commands/handlers/state machines;
3. use the shared execution infrastructure for timers, retries, events, and durable state;
4. observe repeated orchestration patterns;
5. extract a declarative JSON workflow definition only when multiple real workflows benefit;
6. introduce generalized Governor rules only where repeated conformance/remediation patterns justify them.

### JSON configuration is appropriate for

- policy values;
- routing rules;
- retry policies;
- notification configuration;
- capability assignments;
- workflow configuration that is genuinely repeatable;
- versioned operational configuration.

### JSON configuration must not contain

- arbitrary executable application code;
- bypasses of domain invariants;
- direct SQL/data mutation;
- unbounded loops;
- hidden external side effects.

---

## 5.7 Creating a second commercial engine inside Finance

Finance may consume commercial snapshots and calculate economics.

It must not duplicate Catalog/Pricing/Agreement/Tax authority.

The Commercial domain is authoritative for the applicable Tunner commercial contract.

Finance stores/uses immutable event-time references and evidence.

---

## 5.8 Creating provider-specific business domains

Do not build “Stripe Billing Domain” or “SendGrid Notification Domain.”

Stripe, SendGrid, and future providers are adapters/configurations underneath Tunner-owned provider/payment/notification contracts.

---

## 5.9 Creating a second Integration incident system

Connected Product Integration has diagnostic and operational flows.

Platform Reliability already owns generic service/dependency incident mechanics.

Integration should supply:

- Product/environment evidence;
- endpoint state;
- contract compatibility;
- delivery evidence;
- SDK/API diagnostic evidence.

Platform incident infrastructure supplies the generic incident lifecycle.

---

## 5.10 Treating every read model as a durable independent service

Read models are query projections.

They can initially live in the same application/database technology when appropriate.

Extract them only if scale, workload shape, security, or latency requires it.

---

# 6. Complexity That Is Legitimate and Must Not Be Removed

Some flows look complicated because the business is complicated.

Do not simplify these by deleting states or merging authority.

## 6.1 Payments and finance

Keep explicit handling for:

- authorization/capture differences;
- payment attempts;
- provider finality;
- unknown outcomes;
- retries;
- refunds;
- disputes;
- credits;
- invoice allocation;
- reconciliation;
- settlement;
- payout;
- immutable ledger evidence.

The implementation can reuse shared mechanics, but the business states must remain explicit.

---

## 6.2 Subscription lifecycle

Keep distinct semantics for:

- free activation;
- paid activation;
- initial payment;
- trials/introductory phases;
- renewal;
- dunning;
- recovery;
- proration;
- plan/item changes;
- scheduled changes;
- pause/resume;
- cancellation;
- Product result/handoff.

These are not unnecessary technical states.

They represent materially different commercial/business outcomes.

---

## 6.3 Privacy and data lifecycle

Privacy flowcharts are separate processes, not separate microservices.

The complexity is justified because request handling may require:

- identity verification;
- discovery;
- export;
- correction;
- restriction;
- erasure/anonymization;
- retention conflicts;
- legal holds;
- immutable financial/audit evidence;
- processor/transfer governance;
- incident assessment.

Implement them within a cohesive Privacy/Data Governance module that delegates record operations to owning domains.

---

## 6.4 Disaster recovery

RPO/RTO/protection configuration and actual recovery execution are intentionally separated.

This prevents recovery jobs from inventing policy.

It does not require a dedicated DR microservice.

---

## 6.5 Reliability budgets

Reliability Budget/SLO flows are control contracts.

Initially they may consist of:

- policy records;
- telemetry evaluation;
- scheduled/background evaluation;
- read models;
- change/incident policy integration.

Do not create a large standalone SRE platform unless Tunner later needs one.

---

# 7. Apparent Duplications That Are Not Actual Duplications

The architecture intentionally contains multiple views of the same business journey.

The important question is whether they have the **same authority**. Usually they do not.

---

## 7.1 Overview flows vs canonical detailed flows

Examples:

- Direct registration overview vs detailed Identity flows
- Subscription checkout overview vs detailed Subscription flows
- Finance overview vs Finance/Subscription/Commercial domain flows
- Support overview vs detailed Support flows
- Product integration overview vs detailed Integration flows

### Treatment

Use overview flows to understand the complete journey.

Use canonical detailed flows to implement behavior.

Do not build both.

---

## 7.2 Functional Architecture vs detailed lifecycle flows

A Functional Architecture section shows the domain landscape.

Detailed flows describe individual lifecycle operations.

The Functional Architecture is not a second orchestration engine.

---

## 7.3 Stripe/SendGrid examples vs generic Provider domains

Provider-specific diagrams answer:

> How does this provider satisfy Tunner’s generic contract?

They do not create new authority.

Provider-specific code should remain behind adapters.

---

## 7.4 Finance commercial view vs Commercial domain

Commercial determines the billing-safe terms/version.

Finance consumes event-time commercial evidence to calculate and record financial consequences.

Those are complementary responsibilities.

---

## 7.5 Product Registry vs Product business state

Tunner Product Registry contains integration metadata:

- product identity;
- environment;
- client configuration;
- capabilities;
- API/SDK compatibility;
- webhook/event configuration.

It does not become the Product’s internal organization/workspace/team/entitlement database.

---

## 7.6 Billing Account relationship vs Product relationship

Tunner may need both:

- Account-to-Product relationship;
- Billing Account-to-Product commercial relationship.

Those relationships serve different authority and billing purposes.

Do not merge them simply because they refer to the same Product.

---

## 7.7 Tunner subscription result vs Product entitlement state

Tunner can say:

> Subscription operation succeeded.

The Product can separately say:

> I successfully applied the subscription result to my local entitlement model.

These are intentionally different states.

---

## 7.8 Webhook delivery result vs Product Application Result

A webhook `HTTP 2xx` indicates transport/application endpoint acceptance.

It does not prove the Connected Product successfully completed its local business operation.

Keep both evidence types where the integration contract requires them.

---

## 7.9 Provider payment object vs Tunner payment object

Provider objects are execution artifacts.

Tunner payment/financial records are Tunner authority.

Store provider IDs as external references; do not use provider records as the Tunner domain model.

---

## 7.10 Product catalog vs Tunner billing-safe commercial snapshot

The Product may own package/features/catalog semantics.

Tunner requires a versioned billing-safe commercial projection/snapshot to charge correctly and preserve invoice history.

That snapshot is not a takeover of Product catalog authority.

---

## 7.11 Technical API protection vs usage billing

Rate limits, concurrency limits, request counters, and expensive-operation controls protect Tunner infrastructure.

They are not Product usage billing.

---

## 7.12 Audit evidence vs business source of truth

Audit stores evidence of actions/decisions/state transitions.

Audit should not become the source used to mutate current business state.

---

# 8. Suggested Initial Tunner Module Map

The following is a recommended **logical module** map.

It is not a requirement that every module deploy independently.

## 8.1 Identity & Account

Responsibilities:

- Tunner Account
- registration
- verified contacts
- authentication
- passkeys/MFA
- sessions/devices
- account state
- account recovery
- Product-origin authentication/SSO

Approximate architecture coverage:

- Identity flows 36–45
- relevant high-level flows 02/02A/07

---

## 8.2 Billing Account & Authority

Responsibilities:

- payer context;
- Billing Account lifecycle;
- Billing Account membership;
- delegated billing capabilities;
- billing profile;
- payment method references;
- Product billing relationships.

Coverage:

- flows 46–56

---

## 8.3 Subscription

Responsibilities:

- subscription operation;
- free/paid activation;
- lifecycle state;
- renewal;
- trials;
- dunning;
- plan/item change;
- pause/resume/cancel;
- Product handoff result.

Coverage:

- flows 57–72

---

## 8.4 Commercial

Responsibilities:

- billing-safe Product catalog projection;
- pricing;
- currency/cadence;
- agreement;
- fee;
- tax determination;
- immutable commercial snapshot;
- commercial versioning/change.

Coverage:

- flows 73–86
- Tax remittance flow where applicable

---

## 8.5 Finance & Ledger

Responsibilities:

- payment records/attempts;
- invoices/receivables;
- credits/refunds/disputes;
- ledger;
- reconciliation;
- Product payable;
- reserves;
- settlement;
- transfer/payout;
- financial exceptions.

Coverage:

- Finance flows 19–27
- consumes Subscription/Commercial/Provider contracts

---

## 8.6 Provider & Merchant Integration

Responsibilities:

- Provider instances;
- credentials;
- Merchant/provider accounts;
- routing;
- capability/readiness;
- provider callbacks/events;
- provider lifecycle.

Coverage:

- flows 87–92

Stripe/SendGrid diagrams are adapter examples, not additional domain modules.

---

## 8.7 Notification

Responsibilities:

- notification intents;
- recipient controls;
- preferences/suppression;
- templates;
- routing;
- provider delivery;
- in-app notifications;
- delivery evidence.

Coverage:

- flows 93–99

---

## 8.8 Connected Product Integration

Responsibilities:

- Product registry;
- environments;
- OAuth clients;
- SDK/API contract compatibility;
- capabilities;
- event/webhook subscriptions;
- Product event delivery;
- Product Application Result;
- API protection;
- self-service diagnostics/readiness.

Coverage:

- flows 128–135
- INT-11 API protection
- INT-12 self-service/readiness

---

## 8.9 Support

Responsibilities:

- cases;
- intake;
- assignment;
- investigation;
- specialist handoff;
- privileged-action requests;
- resolution;
- reporting.

Coverage:

- flows 100–105

---

## 8.10 Risk, Security & Compliance

Responsibilities:

- risk cases;
- identity verification review;
- merchant compliance review;
- policy exceptions;
- security investigation/containment.

Coverage:

- flows 45 and 106–110

Do not duplicate Identity business state here.

Risk/Security produces decisions/restrictions/evidence through contracts.

---

## 8.11 Audit, Governance & Internal Access

Responsibilities:

- audit evidence;
- approvals;
- SoD;
- policy publication;
- workforce identities;
- roles/permissions/scopes;
- privileged/JIT access;
- certification;
- offboarding.

Coverage:

- flows 111–122

This group may be separated internally into Audit/Governance and Workforce IAM modules if code size or team ownership warrants it.

---

## 8.12 Platform Reliability & Operations

Responsibilities:

- health;
- incidents;
- maintenance/change;
- SLO/reliability budget;
- DR execution;
- recovery verification.

Coverage:

- flows 123–127A

---

## 8.13 Data Lifecycle & Privacy

Responsibilities:

- retention policies;
- archive/restore;
- legal/evidence holds;
- purge/anonymization eligibility;
- privacy requests;
- privacy processing governance;
- privacy incident coordination.

Coverage:

- flows 142–150

The module coordinates owner-domain handlers instead of becoming a central PII database.

---

## 8.14 Execution Infrastructure

Shared platform capability, not a business domain.

Responsibilities:

- atomic outbox;
- event subscription registry;
- dispatcher;
- consumer inbox;
- queue processing;
- worker lease;
- retry/backoff;
- DLQ;
- durable timers;
- idempotency support;
- operation execution primitives.

Coverage:

- flows 28–35

---

## 8.15 Automation/Governor

Treat as an **evolutionary platform module**.

Initial responsibility:

- typed workflow orchestration;
- workflow run state where required;
- reusable conformance checks;
- automation principal handling.

Later responsibility when justified:

- generic Workflow Definition JSON DSL;
- generic Governor;
- generalized remediation engine;
- AI-governed tool framework.

Coverage:

- flows 136–140

Do not make this the first platform built.

---

# 9. Suggested Initial Deployment Shape

Logical modules should not be confused with processes.

A reasonable initial topology can remain small.

Example:

## 9.1 Tunner API / Application Host

Contains most synchronous application modules and APIs.

## 9.2 Tunner Worker Host

Processes:

- outbox dispatch;
- event consumers;
- scheduled jobs;
- reconciliation;
- provider callbacks after intake;
- notification delivery;
- lifecycle/background work.

## 9.3 Database

Initially one managed relational database is acceptable when it supports the selected deployment and operational needs.

Maintain module ownership through schemas/tables and code boundaries.

Do not allow “same database” to become “any module may mutate any table.”

## 9.4 Cache

Shared technical cache where justified.

Never make cache authoritative business state.

## 9.5 Message/Event Infrastructure

Introduce a broker when required by throughput/durability/topology.

The architecture requires durable asynchronous semantics, not a particular vendor or an unnecessary number of queues.

## 9.6 Secrets/KMS

Central secure secret/key abstraction.

No raw provider credentials in application tables/logs.

## 9.7 Observability

Shared logs, traces, metrics, correlation IDs, technical alerts.

Do not build separate observability stacks per module.

---

# 10. Shared Primitives — Implement Once

These concepts occur repeatedly in the flowcharts because every flow must show its safety behavior.

They should be reusable infrastructure.

## 10.1 Authorization

Shared authentication/authorization primitives with owner-domain capability/policy evaluation.

## 10.2 Idempotency

Common operation/idempotency abstraction.

Domain owns idempotency semantics; infrastructure assists storage/enforcement.

## 10.3 Expected-version / optimistic concurrency

Reusable mechanism.

Do not invent different concurrency approaches for each module without reason.

## 10.4 Audit

Single append-only evidence capability with domain-specific event types/payload contracts.

## 10.5 Approval / SoD / step-up

Shared governed-action mechanism.

## 10.6 Outbox

Shared transaction/outbox mechanism.

## 10.7 Event dispatch and consumer inbox

Shared backbone.

## 10.8 Retry / scheduling / DLQ

Shared execution infrastructure.

## 10.9 Policy/version resolution

Common version/effective-time patterns.

Each domain owns its policy definitions.

## 10.10 Error/problem contracts

Use consistent API error representation and stable error codes.

Domain-specific errors remain domain-owned.

## 10.11 Correlation/causation

Standard operation, event, request, and causation identifiers across modules.

## 10.12 Observability

Standard tracing/logging/metrics instrumentation.

---

# 11. Rules for Cross-Domain Execution

Use the following decision rule.

## 11.1 Use synchronous/foreground execution when

The current operation cannot safely proceed without another authority/dependency result.

Examples:

- authorization;
- billing authority;
- current provider readiness before charge submission;
- required risk gate;
- commercial contract resolution required to price an operation;
- exact current state/version validation.

## 11.2 Use durable post-commit events when

The owning domain has already committed its fact and another domain can react independently.

Examples:

- audit projection;
- notification;
- Product webhook;
- analytics/read model;
- operational alert;
- downstream independent reconciliation.

A subscriber failure must not roll back the publisher.

---

# 12. Rules for Data Modeling

## 12.1 Every authoritative record has one owner

A table can be physically located anywhere, but ownership must be clear.

## 12.2 Other modules store references, not copied mutable authority

Copies may be allowed as:

- immutable event-time snapshots;
- read models;
- external references;
- audit evidence.

Do not create two mutable authoritative versions of the same concept.

## 12.3 Snapshot data must state purpose

A commercial snapshot is a historical billing artifact.

It is not the live Product catalog.

A Product result snapshot is integration evidence.

It is not Product operational state.

## 12.4 Every versioned contract should support lineage

Typical fields may include:

- stable ID;
- version;
- status;
- effective from/to;
- supersedes;
- owner;
- created/approved evidence;
- applicable scope.

Exact schema remains domain-specific.

---

# 13. Rules for API Development

- APIs express owner-domain operations.
- Controllers/endpoints stay thin.
- Domain/application services enforce invariants.
- External provider APIs are accessed through adapters.
- Every mutation requiring idempotency receives a stable operation/idempotency boundary.
- Expected versions are used where concurrent state mutation matters.
- Product APIs expose Tunner authority only.
- Do not expose internal database layout as the public contract.
- Version APIs/contracts intentionally.
- Stable error codes must distinguish business rejection, authorization failure, dependency unavailability, retryable processing failure, and unresolved external state.

---

# 14. Rules for UI Development

The flowcharts define behavior, not individual screen count.

Do not create a UI screen for every architecture node.

UI should expose:

- current actionable state;
- blockers;
- evidence;
- next allowed actions;
- risk/impact;
- effective versions where material;
- correct authority boundaries;
- correlation/reference information needed for support/operations.

Sensitive actions must use the same backend operation contracts as APIs/automation.

The UI is never the authority boundary.

---

# 15. Rules for AI and Automation Development

AI may assist with:

- classification;
- summaries;
- recommendations;
- diagnostics;
- workflow suggestions;
- support context;
- operational investigation;
- proposing governed operations.

AI must not become authoritative for:

- payment finality;
- ledger posting correctness;
- authorization;
- Product entitlement state;
- identity verification finality;
- provider external finality;
- legal/compliance determinations unless an approved deterministic/policy path explicitly supports it.

State-changing AI tool calls must be re-authorized through the same owner-domain operation APIs used by humans/services.

---

# 16. Do Not Hardcode Policy Values From the Flowcharts

Architecture diagrams define required contracts, not universal values.

Do not invent:

- RPO/RTO;
- SLO targets;
- error-budget percentages;
- retry counts/times;
- legal/privacy deadlines;
- retention durations;
- cross-border transfer rules;
- tax/legal thresholds;
- approval thresholds;
- age/consent rules.

These values must come from approved versioned policy/configuration applicable to the relevant environment/jurisdiction/product/operation.

---

# 17. Development Decision Checklist

Before implementing any diagram box, ask:

1. **Who owns this state?**
2. **Is this authoritative state, reference data, immutable snapshot, or read model?**
3. **Is this box a logical responsibility or does it truly need a separate deployable component?**
4. **Does shared infrastructure already solve the technical mechanics?**
5. **Is the behavior business-specific or generic infrastructure?**
6. **Does another canonical flow already own this behavior?**
7. **Is this an overview/reference flow rather than implementation authority?**
8. **Does this operation require foreground dependency validation?**
9. **Can post-commit work be independent and event-driven?**
10. **Could retry duplicate an external side effect?**
11. **What happens if the provider outcome is unknown?**
12. **Which version/policy must be retained for historical lineage?**
13. **Can the implementation stay compiled/typed rather than adding a new generic abstraction?**
14. **Would splitting this into another service solve a demonstrated operational problem?**
15. **Does the implementation preserve Tunner/Product authority boundaries?**

If these questions are not answerable, do not invent behavior. Resolve the contract during FRD preparation.

---

# 18. Anti-Patterns That Must Be Rejected During Code Review

Reject changes that:

- create a new microservice solely because a flowchart section exists;
- create a new database solely because a Registry box exists;
- allow one module to directly write another module’s authoritative tables;
- create Product workspace/team/entitlement authority inside Tunner;
- use Tunner technical telemetry as Product usage billing;
- treat browser redirect success as subscription/entitlement authority;
- retry an uncertain payment/provider side effect without reconciliation;
- treat webhook delivery as Product application success;
- replay an event by recreating its originating business operation;
- duplicate commercial rules inside Finance;
- duplicate Provider business rules inside Stripe/SendGrid adapters;
- duplicate approval/audit/retry engines;
- implement the generic JSON workflow framework before concrete repeated use cases require it;
- let AI directly mutate sensitive domain state;
- hardcode legal, recovery, SLO, or compliance values from assumptions;
- remove legitimate states merely to make the workflow look simpler;
- add manual operator steps where deterministic automation can safely complete the work.

---

# 19. Simplicity Test for a Proposed Design

A proposed implementation is probably too complex when:

- the same business fact exists as mutable authority in multiple modules;
- there are multiple orchestration engines;
- each module has its own retry/audit/approval framework;
- a single customer action crosses many network services without a scaling/fault-isolation reason;
- development requires distributed transactions for ordinary CRUD;
- engineers cannot identify the owner of a business record;
- a simple rule requires editing a generic workflow platform rather than owner-domain code;
- debugging requires following many queues/services for a basic synchronous operation;
- provider-specific code leaks into the core domain;
- implementation has more technical services than meaningful business ownership boundaries.

A design is appropriately simple when:

- business authority is obvious;
- normal operations have short execution paths;
- external uncertainty is explicit;
- cross-cutting mechanics are shared;
- modules are independently understandable and testable;
- deployments are few;
- behavior can later be extracted without changing the domain contract.

---

# 20. Testing Expectations Derived From the Flowcharts

Every canonical flow should produce tests for at least:

- happy path;
- unauthorized actor;
- invalid state;
- stale expected version;
- dependency unavailable;
- duplicate/idempotent request;
- retryable technical failure;
- non-retryable failure;
- unknown external outcome where relevant;
- duplicate event delivery;
- out-of-order/stale event where relevant;
- policy/version change;
- permission/SoD/approval path where relevant;
- compensation path where legitimate;
- Product boundary enforcement;
- audit/evidence creation;
- post-commit subscriber failure isolation.

Financial flows additionally require reconciliation and invariant tests.

---

# 21. PRD/FRD Handoff Rule

The FigJam architecture should not be translated directly into code from visual interpretation alone.

Before implementation of a domain/module, the architecture must be converted into a development specification containing:

- business purpose;
- actors;
- authority/ownership;
- states and transitions;
- data model and field definitions;
- invariants;
- APIs/commands;
- request/response contracts;
- events;
- external provider contracts;
- permissions;
- policy/version inputs;
- idempotency;
- concurrency handling;
- failure/error taxonomy;
- retry/reconciliation behavior;
- audit/evidence requirements;
- observability;
- UI-visible statuses/actions;
- acceptance criteria;
- test scenarios;
- explicit non-goals/boundaries.

## Recommended next-document strategy

For Tunner, the architecture is already detailed enough that the next artifact should be a **development-oriented FRD/specification package**, with a short PRD/business context section where needed.

The FRD should be generated module-by-module from the canonical flows and must cite the relevant `TUN-FLOW-*` references.

Do not write a large generic PRD that repeats the diagrams without providing implementable contracts.

The purpose of the next document is to eliminate interpretation before coding begins.

---

# 22. Suggested .NET Solution Interpretation

A possible project structure is shown only to clarify module boundaries; the exact repository structure may be refined in the FRD/development standard.

```text
src/
  Tunner.Api/
  Tunner.Worker/

  Modules/
    Identity/
    Billing/
    Subscription/
    Commercial/
    Finance/
    Providers/
    Notifications/
    Integration/
    Support/
    RiskSecurity/
    Governance/
    InternalAccess/
    Reliability/
    DataPrivacy/

  Platform/
    Execution/
    Eventing/
    Scheduling/
    Idempotency/
    Authorization/
    Approval/
    Audit/
    Observability/
    Secrets/
    Persistence/

  Automation/
    TypedWorkflows/
    # Generic DSL/Governor added progressively when justified.
```

Each module may contain:

```text
Domain/
Application/
Infrastructure/
Contracts/
Tests/
```

Keep framework abstractions proportional to module complexity.

A small CRUD-oriented capability does not require the same domain-model ceremony as Finance, Subscription, or Identity.

---

# 23. Final Architecture Interpretation Rule

When there is tension between “simpler code” and “correct business behavior,” preserve the business behavior.

When there is tension between “more infrastructure” and “a simpler implementation with the same business behavior,” choose the simpler implementation.

Therefore:

> **Simplify topology, not business truth.**

> **Reuse infrastructure, not business authority.**

> **Keep owner-domain invariants compiled and testable.**

> **Use events for independent post-commit reactions, not as a reason to make every interaction asynchronous.**

> **Treat diagrams as behavioral contracts, not deployment diagrams.**

> **Introduce generic automation only after repeated real workflows justify it.**

> **Never allow an implementation convenience to move Connected Product authority into Tunner.**

---

# 24. Status and Usage

This guideline must be read together with the Tunner architecture flowcharts during:

- PRD/FRD preparation;
- solution design;
- implementation planning;
- task generation for developers/AI agents;
- code review;
- QA/test-case design;
- DevOps deployment design;
- architecture review.

If a developer or AI agent believes a flow is duplicated or over-engineered, the correct action is **not to silently remove it**.

First classify it as one of:

1. canonical domain behavior;
2. overview;
3. reference/provider example;
4. functional architecture;
5. shared infrastructure behavior;
6. read model/evidence view;
7. future/evolutionary platform capability.

Then implement only the authority that category requires.

---

**End of guideline.**
