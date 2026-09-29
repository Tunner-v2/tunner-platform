> **Tunner Development Authority Package**  
> **Status:** ACTIVE AMENDMENT AUTHORITY — AMD-0001  
> **Effective date:** 2026-09-29  
> **Applies to:** Locked Documentation Baseline 1.6.0

# 35 — AI Agent Skill Catalog & Review Contracts

## 1. Purpose

Define implementation-grade specialist skill contracts for Tunner AI/human development. Skills are governed review/execution lenses over one shared project authority and memory system. A skill does not create an independent source of truth.

Canonical repository form after P0 bootstrap:

```text
.agent/
  AGENTS.md
  skills/
    <skill-name>/
      SKILL.md
      references/      # optional
      templates/       # optional
      scripts/         # optional
```

`AGENTS.md` is a concise map and startup contract. It must not duplicate the FRD, this catalog, or historical project context.

## 2. Common SKILL.md contract

Every skill manifest must contain:

```yaml
skill_id:
name:
version:
status:
mission:
activation:
required_authority: []
required_context: []
checks: []
required_outputs: []
blocking_conditions: []
prohibited_actions: []
handoff: []
evidence_types: []
```

Rules:

1. the skill version/hash used by a work item is captured in context/evidence;
2. role activation is determined by governance impact rules, not by model preference;
3. a skill may request `INSUFFICIENT_CONTEXT`;
4. a skill may block affected work when its mandatory control fails;
5. a skill cannot approve Product decisions on behalf of the Product Owner;
6. a skill cannot override FRD/PDR/ADR authority;
7. reviews are evidence-bearing and must reference the work item and source authority.

## 3. Full-stack Engineer

**Mission:** implement approved vertical slices across API/worker/domain/BFF/frontend boundaries while preserving module ownership and contracts.

**Activates:** every implementation item; stronger activation for cross-layer work.

**Must inspect:** work item, applicable FRD/contracts/flows, module boundaries, ADR/PDR, tests and affected code.

**Checks:** domain ownership; transaction boundaries; idempotency; concurrency; errors; observability; configuration; migrations; frontend/backend consistency; no placeholder production behavior.

**Outputs:** implementation, tests, changed-file inventory, technical notes, evidence.

**Blocks:** missing contract/business authority, unsafe side-effect semantics, unresolved migration/data risk.

**Must not:** invent business rules, bypass adapters, expose secrets, weaken tests/security to make builds pass.

## 4. Solution Architect

**Mission:** protect system boundaries, architecture coherence, scalability, resilience and compatibility.

**Activates:** architecture/module/integration changes; shared infrastructure; cross-domain dependencies; large refactors.

**Checks:** dependency direction, module ownership, sync/async boundary, transaction/outbox semantics, external finality, provider abstraction, failure modes, scaling topology, ADR trigger.

**Outputs:** architecture review evidence; ADR recommendation when material.

**Blocks:** circular/forbidden dependency, authority leakage, unjustified new service/framework, unhandled distributed failure or compatibility risk.

## 5. Project Manager

**Mission:** preserve executable delivery state without turning governance into manual waterfall.

**Activates:** milestone/sprint planning, scope change, dependency/blocker management, handoff, milestone/release closure.

**Checks:** prerequisites; work-state legality; dependency ordering; blockers; scope change classification; evidence completeness; exact next action.

**Outputs:** milestone/sprint/work-item state and handoff/next-work evidence.

**Blocks:** closure with incomplete mandatory evidence or unresolved blocking dependency.

## 6. UI/UX Engineer

**Mission:** produce usable, accessible, coherent user/operator journeys subordinate to FRD authority.

**Activates:** any user/admin surface, state, action or journey.

**Checks:** journey completeness; responsive behavior; accessibility; loading/empty/error/permission/UNKNOWN states; destructive confirmation; step-up/approval UX; Figma alignment; UI_REQUIRED gaps.

**Outputs:** UI/UX review, Figma change requirement or implementation guidance, visual acceptance evidence.

**Blocks:** required state/action absent, UI contradicts FRD, accessibility-critical failure.

## 7. Frontend Engineer

**Mission:** implement reusable, accessible frontend components and BFF integration without leaking backend technical semantics.

**Activates:** frontend/component/BFF-facing work.

**Checks:** component reuse; state/query handling; schema validation; accessibility; content-key resolution; safe error mapping; secure session/BFF behavior; frontend tests.

**Outputs:** frontend implementation/test evidence.

**Blocks:** browser token exposure, hard-coded business semantics, raw technical state presented as ordinary user content.

## 8. Tester & QA Engineer

**Mission:** prove approved behavior and expose regressions across happy, negative and failure paths.

**Activates:** every implementation; milestone/release closure; regression investigation.

**Checks:** acceptance criteria; duplicates; idempotency; concurrency; retries; partial failure; UNKNOWN external state; cancellation; correction; auth failure; policy version change; recovery; UI/regression coverage.

**Outputs:** test plan/results, defect records, evidence manifest.

**Access:** may request FULL_AUDIT repository access; prompt context remains bounded/partitioned unless justified.

**Blocks:** missing required tests, unexplained failure, unverified acceptance criteria.

## 9. Financial Specialist

**Mission:** protect monetary correctness and accounting/settlement invariants.

**Activates:** price, fee, tax, FX, payment, invoice, credit, receivable, ledger, settlement, payable, payout, reconciliation.

**Checks:** currency/precision/rounding; immutable economic snapshots; debit/credit invariants; fee attribution; tax evidence; FX treatment; settlement/payout lineage; reversals/corrections; reconciliation/finality.

**Outputs:** financial review/evidence and invariant tests.

**Blocks:** ambiguous economic treatment, unbalanced ledger, silent currency conversion, provider data redefining Product economics.

**Must not:** invent commercial policy.

## 10. Compliance Specialist

**Mission:** ensure privacy, payments, market, tax/compliance and regulated-operation boundaries are represented as deterministic policy/evidence gates.

**Activates:** personal/sensitive data, identity/KYC, payment/provider, tax, market activation, privacy requests, marketing/consent, regulatory classifications.

**Checks:** applicable authority, evidence, consent/legal basis where defined, provider/jurisdiction gates, retention, auditability, UNKNOWN handling.

**Outputs:** compliance review, policy/evidence requirements, scoped blocker where applicability is unresolved.

**Must not:** provide unsupported legal conclusions or invent jurisdictional rules.

## 11. Auditor

**Mission:** independently verify traceability, evidence integrity, approvals/SoD, history and release/milestone claims.

**Activates:** sensitive work, finance/compliance/security controls, milestone/release closure, governance changes.

**Checks:** requirement→work→code→test→evidence traceability; immutable history; approval/SoD; reconciliation; manifest/hash/provenance; reopen lineage.

**Access:** full authorized repository/evidence access, normally retrieved in partitions.

**Outputs:** audit findings with PASS/FAIL/BLOCKED per control, evidence references.

**Blocks:** unverifiable claim, missing evidence, mutable history replacing original evidence.

## 12. Founder/Product Owner

**Mission:** represent explicit Product Owner business authority and acceptance boundaries.

**AI limitation:** an AI skill may identify Product impact, conflicts, choices and decision requests, but **cannot impersonate Product Owner approval**.

**Activates:** unresolved business behavior, commercial/product boundary change, explicit Product acceptance.

**Outputs:** decision request/impact analysis; records actual human Product Owner decision when provided.

**Blocks:** affected scope where existing authority does not resolve material business behavior.

## 13. Admin/Operator

**Mission:** validate safe, traceable operational control-center behavior.

**Activates:** admin/control-center operations, approvals, configuration, diagnostics, recovery.

**Checks:** permissions; step-up; safe actions; confirmation; status clarity; diagnostics; recoverability; audit evidence; operational automation.

**Outputs:** operator acceptance findings.

## 14. Support Engineer

**Mission:** ensure incidents/cases can be diagnosed, correlated, escalated and resolved without unsafe backend manipulation.

**Activates:** support workflows, user-impacting operational failures, case tooling.

**Checks:** case intake; correlation IDs; user-safe evidence; escalation; restricted actions; recovery; communications; audit trail.

**Outputs:** support-workflow review and diagnostic requirements.

## 15. End-user UX Reviewer

**Mission:** review the experience from the intended user's task perspective.

**Activates:** user-facing journeys/content.

**Checks:** clarity; task completion; cognitive load; user-oriented terminology; actionable errors/guidance; accessibility/friction.

**Must not:** override security/compliance/business constraints for convenience.

**Outputs:** UX findings prioritized by task impact.

## 16. Content Writer

**Mission:** create user/operator-oriented presentation content under governed content-management rules.

**Activates:** labels, titles, instructions, empty states, errors, notifications, emails, disclosures/help text.

**Checks:** audience/task fit; terminology; tone; consistency; accessibility/readability; sensitive-content approval class.

**Must not:** alter prices, permissions, eligibility, financial calculations, workflows, policy or compliance decisions.

**Outputs:** content proposals/keys/variants and review evidence.

## 17. Business Analyst

**Mission:** transform approved business authority into implementation-ready scenarios without inventing policy.

**Activates:** requirement decomposition, cross-domain scenario design, acceptance modeling, discovered requirement/gap.

**Checks:** actors; preconditions; triggers; business rules; states/transitions; exceptions; ownership; data/contract effects; UI/operator effects; examples; acceptance criteria; traceability.

**Outputs:** requirement decomposition, scenario matrix, acceptance criteria, gap/decision request.

**Blocks:** business ambiguity affecting observable behavior, money, ownership, compliance, API/event compatibility or user workflow.

## 18. SDK Engineer

**Mission:** make Tunner Product integration contract-first, ergonomic, compatible and testable.

**Activates:** public API/SDK, auth client, generated clients, events/webhooks, SDK packaging/versioning/examples.

**Checks:** OpenAPI compatibility; generated/manual layer boundary; authentication; idempotency; retries only where safe; cancellation/timeouts; stable errors; serialization; event/webhook verification; version policy; diagnostics; examples.

**Outputs:** SDK implementation/review, compatibility matrix, package/test evidence, integration examples.

**Blocks:** SDK hides incompatible API change, unsafe retry, leaked provider-specific contract, undocumented breaking change.

## 19. Governance Engineer

**Mission:** implement and protect Tunner's executable development governance.

**Activates:** governance schema, lifecycle, gates, role activation, context policy, orchestration policy, evidence/handoff.

**Checks:** deterministic state transitions; schema validation; no bypass; scoped blocking; evidence requirements; authority precedence; reproducibility.

**Outputs:** governance-tool implementation/review and gate tests.

**Blocks:** any path allowing ungoverned DONE/release, silent authority override, free-floating critical TODO/state.

## 20. R&D Engineer

**Mission:** establish current primary-source evidence before material technical/provider/protocol/security/compliance implementation.

**Activates:** freshness-sensitive or external technology/standard/provider decision.

**Checks:** primary source; version/date; support/EOL; normative/informative status; breaking/security changes; compatibility with locked architecture.

**Outputs:** source-register update, research note, ADR input.

**Blocks:** material implementation relying on stale/unverifiable external facts.

## 21. Cyber Security Engineer

**Mission:** protect confidentiality, integrity, availability and abuse resistance.

**Activates:** authn/authz, sessions/tokens, secrets/crypto, sensitive data, provider/webhook trust, dependency/supply-chain, network exposure, security controls.

**Checks:** threat model; least privilege; trust boundaries; input/output validation; secrets; encryption/key handling; replay/idempotency; SSRF/injection/XSS/CSRF as applicable; dependency risk; logging/redaction; security tests.

**Outputs:** threat/security review, required controls/tests, security evidence.

**Access:** may request FULL_AUDIT for security investigation.

**Blocks:** critical/high unresolved security risk or required control absent.

## 22. DevOps Engineer

**Mission:** provide deterministic, secure build/runtime/deployment automation and operational delivery.

**Activates:** Docker, CI/CD, environment/bootstrap, observability infra, secrets delivery, deployment, artifact provenance, rollback, IaC.

**Checks:** reproducibility; health/readiness; environment isolation; least-privilege credentials; OIDC where supported; artifact integrity; rollback; migration ordering; telemetry; failure recovery.

**Outputs:** automation/IaC/workflow changes and operational evidence.

**Blocks:** non-reproducible build/deployment, unsafe secret handling, untraceable artifact or untested rollback where required.

## 23. Role activation precedence

Governance activates the union of all applicable role triggers. It may remove a non-mandatory role only when the work-item record explains why. Mandatory roles defined by document 12 or this catalog cannot be suppressed by the orchestrator.

Specialist findings block only affected scope unless systemic risk makes broader blocking necessary.

## 24. Skill handoff contract

Every activated skill returns:

```yaml
skill_id:
skill_version:
work_item_id:
result: PASS | FAIL | BLOCKED | NOT_APPLICABLE
authority_reviewed: []
context_artifacts: []
findings: []
required_actions: []
evidence: []
context_expansion_requested: false
```

No free-form review may satisfy a mandatory role gate without this structured result or an equivalent schema-versioned record.

## 25. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| SKILL-001 | payment work starts | Full-stack + QA + Financial + Auditor activate; Compliance/Security according to impact |
| SKILL-002 | SDK contract changes | SDK Engineer + QA activate; Architect when boundary changes |
| SKILL-003 | governance schema changes | Governance Engineer + QA + Auditor activate |
| SKILL-004 | user-facing content changes | UI/UX + End-user UX + Content Writer activate |
| SKILL-005 | AI Founder role proposes approval | governance refuses to treat AI output as Product Owner approval |
| SKILL-006 | skill lacks required context | structured `INSUFFICIENT_CONTEXT`/expansion path is used |
| SKILL-007 | required skill review absent | work cannot become DONE |
