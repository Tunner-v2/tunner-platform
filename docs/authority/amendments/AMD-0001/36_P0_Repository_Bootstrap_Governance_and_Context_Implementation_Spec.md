> **Tunner Development Authority Package**  
> **Status:** ACTIVE AMENDMENT AUTHORITY — AMD-0001  
> **Effective date:** 2026-09-29  
> **Applies to:** Locked Documentation Baseline 1.6.0

# 36 — P0 Repository Bootstrap, Governance, Context & Orchestration Implementation Specification

## 1. Purpose

Make P0 executable without requiring a developer or AI agent to invent repository layout, governance-tool boundaries, project-memory behavior, context strategy, orchestration control or GitHub-link handling.

P0 builds the development control plane. It does **not** implement Tunner product/business features.

## 2. Bootstrap premise

At P0 start there may be **no GitHub repository, GitHub Actions URL, branch history, generated context pack or governance state yet**. This is expected.

Their absence before repository bootstrap is **not a blocker**.

P0 creates them and immediately registers the resulting canonical identifiers/URLs in:

- `13_RD_Source_Register.md`;
- `docs/project/PROJECT_CONTEXT.md`;
- generated context authority index;
- release/evidence manifests where applicable.

No placeholder/fabricated URL is permitted.

## 3. Initial repository structure

```text
/
  AGENTS.md
  README.md
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  .gitignore

  .agent/
    AGENTS.md
    skills/

  docs/
    authority/
    decisions/
    project/
      PROJECT_CONTEXT.md
      CURRENT_STATE.md
      DECISION_INDEX.md
      RISK_REGISTER.md
      HANDOFF.md
    context/
      current/

  governance/
    schemas/
    milestones/
    sprints/
    work-items/
    todos/
    defects/
    reopens/
    decisions/
    amendments/
    releases/
    evidence/
    policies/

  tools/
    Tunner.Tooling.sln
    src/
      Tunner.Cli/
      Tunner.Governance/
      Tunner.Context/
      Tunner.Orchestrator/
      Tunner.Dev/
      Tunner.Testing/
    tests/

  src/                 # product solution placeholder only until authorized plane
  tests/               # product test roots/placeholders as required
  scripts/
  .github/
    CODEOWNERS
    pull_request_template.md
    workflows/
```

Production application projects must not depend on governance-tool assemblies.

## 4. Repository creation sequence

1. create deterministic local repository structure;
2. import locked authority baseline + active amendments;
3. initialize Git and protected branch conventions locally;
4. create canonical GitHub remote when account/organization/repository destination is available;
5. record the repository URL immediately;
6. push initial governed baseline through the approved bootstrap path;
7. configure Rulesets/CODEOWNERS/required checks;
8. create GitHub Actions workflows;
9. record canonical Actions/workflow links after they exist;
10. run governance/document/link validation.

Until steps 4/8 occur, the resource register records those resources as `BOOTSTRAP_GENERATED / NOT_YET_CREATED`, never as unresolved architecture.

## 4A. Authority import and execution mirror

P0 imports the exact approved authority package before normal work execution.

Repository layout:

```text
docs/authority/
  current-authority.json
  baselines/
    1.6.0/
      baseline-provenance.json
      # exact extracted/verified authority files or immutable archive + indexed extraction
  amendments/
    AMD-0001/
      amendment-provenance.json
      AMD-0001_Pre-P0_Governance_Agent_Skills_Context_and_Bootstrap_Completion.md
      35_AI_Agent_Skill_Catalog_and_Review_Contracts.md
      36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md
      37_Current_Authority_Index.md
```

The repository mirror must preserve content hashes and source IDs/URLs. It is an execution mirror, not a license for local semantic edits.

Commands:

```text
tunner authority status
tunner authority import
tunner authority verify
tunner authority diff
```

`tunner context build`, `tunner work start` and orchestrated implementation require a valid authority status.

If Drive is temporarily unavailable after a verified immutable import, the recorded mirror may remain usable under policy. New or changed authority cannot be assumed; it must be explicitly imported/verified when available.

## 4B. Bootstrap governance mode before self-hosting

A governance tool cannot enforce commits that predate its own implementation. P0 therefore uses a narrowly-scoped bootstrap governance mode.

Bootstrap phases:

```text
B0 — Authority/repository bootstrap
  → B1 — Governance schemas + manual schema validation
  → B2 — tunner-governance validator/status/gate MVP
  → B3 — Governance self-hosting cutover
  → B4 — Context/orchestrator/skills/dev/test tooling under self-hosted governance
  → P0 enablement gate
```

Before B3:

- only P0 control-plane/bootstrap work is permitted;
- each change has a bootstrap work record, acceptance criteria and evidence;
- document 35 role contracts are applied as review checklists even before automated role activation exists;
- at minimum Governance Engineer + Tester/QA review governance code; Auditor verifies traceability; Cyber Security/DevOps activate according to impact;
- no Product feature/business implementation is allowed;
- bootstrap commits carry a machine-detectable `Bootstrap-Governance: true` trailer or equivalent governed metadata.

At B3, the newly functional `tunner-governance` must:

1. import all bootstrap work records;
2. validate their schemas;
3. replay legal lifecycle/gate rules where applicable;
4. verify required bootstrap evidence;
5. report exceptions explicitly;
6. produce a signed/hashed self-hosting cutover evidence record.

The project cannot claim governance self-hosting or proceed toward P1–P8 until this replay passes.

There is no permanent “bootstrap bypass.” The exception ends at B3 and is limited to creating the control plane itself.

## 5. Governance-tool architecture

One .NET 10 / C# 14 tooling solution hosts separate libraries/commands:

```text
Tunner.Cli
  ├── Tunner.Governance
  ├── Tunner.Context
  ├── Tunner.Orchestrator
  ├── Tunner.Dev
  └── Tunner.Testing
```

The user-facing command is `tunner`; implementation libraries remain separable/testable.

Baseline libraries are governed by document 04 and refreshed at implementation time.

## 6. Governance state

Authoritative governance records are text-based, diffable and schema-validated. Generated indexes/caches are disposable.

Minimum schema objects:

- milestone;
- sprint;
- work item;
- dependency;
- TODO;
- defect;
- reopen;
- gate;
- Product Decision;
- amendment;
- ADR reference;
- release;
- evidence manifest;
- specialist review;
- context selection;
- handoff.

Every object has immutable ID and `schema_version`.

## 7. Work-item impact classification

Before execution, governance derives impact flags:

```text
architecture
api
sdk
ui
content
financial
compliance
privacy
security
admin_ops
support
data_schema
migration
provider
infrastructure
release
```

Impact flags determine mandatory skills, tests, evidence and context expansion.

Manual impact overrides require a recorded reason and cannot suppress a mandatory specialist without policy authorization.

## 8. Context index

The context engine indexes metadata rather than treating embeddings as authority.

Minimum artifact metadata:

```text
artifact_id
artifact_type
path
module
domain
authority_type
authority_version
work_items
flows
contracts
decisions
skills
dependencies
dependents
security_impact
financial_impact
compliance_impact
ui_impact
sdk_impact
last_changed_commit
content_hash
summary_hash
```

Initial retrieval order:

1. explicit work-item/authority links;
2. module/dependency/symbol graph;
3. changed files + relevant Git history;
4. lexical/path retrieval;
5. optional semantic retrieval;
6. governed repository-wide scan.

A paid vector/embedding service is not required for P0.

## 9. Access scope versus prompt scope

Access and prompt injection are distinct:

```yaml
repository_access: FULL | DOMAIN | TASK
context_mode: CORE | TASK | EXPANDED | FULL_AUDIT
```

Auditor, QA, Cyber Security, Solution Architect and other authorized roles may receive `repository_access: FULL` while `context_mode` starts `TASK` or `EXPANDED`.

`FULL_AUDIT` permits repository-wide discovery and partitioned retrieval. It does not require sending every file in one request.

## 10. Context budget policy

Context/token budgets are operational configuration and can vary by model/provider/work class.

The policy must preserve priority:

1. governing authority;
2. current work state/acceptance;
3. blocking decisions/risks;
4. relevant contracts/flows;
5. affected implementation;
6. tests/evidence;
7. supporting history/research.

If the budget cannot contain mandatory authority, the engine returns `CONTEXT_BUDGET_INSUFFICIENT`; it must not silently drop required authority.

## 11. Context freshness

Every generated context/summary records:

- source path/artifact ID;
- source hash;
- source authority version;
- relevant locator/section;
- generator/tool version;
- context-policy version;
- generated timestamp;
- summary hash.

Source hash change marks derived context `STALE`.

Stale context cannot satisfy a governance gate.

## 12. Escalation

Agents may return:

`INSUFFICIENT_CONTEXT`

Governed escalation proceeds:

`TASK dependency → module/domain → cross-domain → FULL_AUDIT`.

Each expansion records why it occurred and what was added.

## 13. Orchestration contract

`tunner-orchestrator` is never an authority.

Execution:

```text
governance eligibility
  → impact classification
  → role activation
  → context build
  → execution plan
  → implementation/tool calls
  → automated tests
  → specialist reviews
  → evidence collection
  → governance gate
  → handoff/next
```

It may parallelize independent checks. It cannot bypass a gate or mark work/release complete.

## 14. Project memory

Persistent state is repository-native:

- stable project facts → `PROJECT_CONTEXT.md`;
- active execution state → `CURRENT_STATE.md`;
- decisions → `DECISION_INDEX.md` + canonical PDR/ADR/amendment;
- risks → `RISK_REGISTER.md`;
- transfer state → `HANDOFF.md`.

Generated summaries are derived state and never override authority.

No critical fact may exist only in chat.

## 15. TODO policy

No free-floating critical TODO.

Every TODO record contains:

```yaml
id:
work_item_id:
title:
status:
priority:
owner_role:
prerequisites: []
acceptance_condition:
created_at:
updated_at:
```

Code TODO comments reference a governed ID.

## 16. GitHub and CI bootstrap

Before the remote exists, P0 validates local policy/config/templates.

After repository creation:

- protect `main`;
- require PR for normal changes;
- disallow force push/delete on protected history;
- configure CODEOWNERS for sensitive paths;
- configure required status checks;
- use reusable GitHub Actions workflows;
- add secret/dependency/SAST/container/SBOM checks according to P0 scope;
- use OIDC for deployment/cloud credentials when the eventual provider supports it;
- generate artifact checksums/attestations where available;
- record actual repository and workflow links in the resource register.

GitHub Issues/PRs are execution surfaces; repository governance records remain work-state authority.

## 17. P0/feature boundary

P0 may create placeholders required to prove build/test/governance wiring. It must not implement Product business behavior.

The following belong after the P0 gate:

- production User/Admin application feature scaffolding beyond minimum pipeline proof;
- Account/Identity business implementation;
- Resend business notification adapter behavior;
- `data_region` Product behavior;
- R2 business storage adapter behavior;
- payment/Stripe business adapter behavior;
- Product APIs/SDK functionality beyond governance/contract pipeline proof.

Their already-approved designs remain valid; only execution timing moves to their owning planes.

## 18. Minimum P0 enablement gate

P1–P8 remain blocked until machine-readable evidence proves:

1. repository authority mirror imports baseline 1.6.0 + AMD-0001 and passes provenance/hash verification;
2. governance schemas validate and bootstrap governance records are replayed successfully at self-hosting cutover;
3. lifecycle/gate engine blocks invalid transitions;
4. role activation works from impact classification;
5. all 20 mandatory skills exist and validate;
6. bounded context build works;
7. access scope vs prompt scope is enforced;
8. stale-context invalidation works;
9. `INSUFFICIENT_CONTEXT` escalation works;
10. orchestrator cannot bypass governance;
11. unit/integration/governance/security/document validation suites pass;
12. TODO/defect/handoff/evidence state persists;
13. clean/fresh agent reconstructs project/work state without chat;
14. Git/GitHub controls required at that point are configured and evidenced;
15. exact next authorized work is produced by governance.

## 19. Fresh-agent acceptance scenario

A new agent with no conversation history receives repository access.

Expected:

1. reads root agent contract;
2. runs governance status;
3. builds current context;
4. identifies active baseline + amendment;
5. identifies P0 milestone/work item;
6. activates required skills;
7. identifies blockers/TODOs/tests/evidence;
8. performs no unauthorized feature work;
9. reports exact next action;
10. can expand context to FULL_AUDIT when assigned an authorized audit.

PASS evidence is required before P0 feature enablement.

## 20. Cost/quality telemetry

Where provider/runtime telemetry permits, record:

- context mode;
- selected/excluded artifact counts;
- approximate context/input usage;
- cache hit/miss;
- stale-context regeneration;
- expansion reason;
- specialist activations;
- gate result.

Telemetry is used to optimize retrieval policy and cost, never to omit mandatory authority.

## 21. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| P0-GOV-001 | repository does not yet exist | bootstrap proceeds; missing repo URL is expected state, not architecture blocker |
| P0-GOV-002 | GitHub remote created | canonical URL is registered immediately |
| P0-GOV-003 | Actions workflows created | workflow links are registered immediately |
| P0-GOV-004 | blocked work reaches orchestrator | execution refused |
| P0-GOV-005 | ordinary work context built | TASK context contains mandatory authority and relevant implementation only |
| P0-GOV-006 | auditor requires broad review | full authorized access + partitioned retrieval |
| P0-GOV-007 | authority changes | dependent context cache invalidated |
| P0-GOV-008 | required specialist missing | DONE gate fails |
| P0-GOV-009 | feature implementation attempted before P0 enablement | governance blocks it |
| P0-GOV-010 | fresh-agent recovery test | PASS without prior chat history |
| P0-GOV-011 | authority mirror bytes are changed locally | `tunner authority verify` fails and implementation/context gates block |
| P0-GOV-012 | Drive temporarily unavailable after verified immutable import | verified mirror remains usable per policy; no new authority is fabricated |
| P0-GOV-013 | governance tool reaches self-hosting cutover | all pre-tool bootstrap records are imported/replayed/validated before normal governed execution continues |
| P0-GOV-014 | bootstrap mode attempts Product feature work | blocked; bootstrap exception is control-plane-only |
