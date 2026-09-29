> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 04 — Governance & Context Tooling Specification

## 1. Objective

Create lightweight repository-native tools before feature development so AI/human contributors cannot silently bypass required development stages.

Tools are outside the production solution.

## 2. Tools

### 2.1 `tunner-governance`

Responsibilities:

- milestone/sprint/work-item lifecycle;
- prerequisites;
- blockers;
- defects;
- reopen;
- amendments;
- Product Decisions;
- evidence gates;
- release readiness;
- “what next?” calculation;
- traceability validation.

Representative commands:

```text
tunner governance status
tunner governance next
tunner governance milestone show M02
tunner governance work start TUN-184
tunner governance block TUN-184 --reason UI_REQUIRED
tunner governance defect create
tunner governance reopen TUN-184
tunner governance decision create
tunner governance amendment create
tunner governance gate check TUN-184
tunner governance release check 0.4.0
```

The tool advises and enforces repository rules; it does not invent Product decisions.

### 2.2 `tunner-context`

Builds bounded context packages for handoff.

```text
tunner context build --milestone M02
tunner context build --work-item TUN-184
tunner context verify
```

Output:

- current authority summary;
- relevant business rules;
- applicable flows;
- ADRs;
- open decisions;
- current work state;
- recent Git changes;
- tests/evidence status;
- known defects;
- related contracts;
- explicitly excluded context.

It should favor retrieval by manifest over dumping the entire repository.

### 2.3 `tunner-dev`

Automation interface:

```text
tunner-dev doctor
tunner-dev setup
tunner-dev start
tunner-dev stop
tunner-dev reset
tunner-dev migrate
tunner-dev seed --profile development
tunner-dev health
tunner-dev logs
```

No Product Owner should need to install/configure dependencies manually beyond supported prerequisites.

### 2.4 `tunner-test`

```text
tunner-test unit
tunner-test integration
tunner-test contracts
tunner-test ui
tunner-test security
tunner-test dev
tunner-test sandbox
tunner-test staging
tunner-test production --safe
tunner-test evidence
```

Production safe mode forbids destructive tests.


### 2.5 `tunner-orchestrator`

Governed execution coordinator. It never becomes a source of Product or architecture authority.

Responsibilities:

- request eligibility from `tunner-governance`;
- obtain activated roles/skills and mandatory gates;
- request the bounded context policy from `tunner-context`;
- sequence implementation, tests and specialist reviews;
- collect evidence;
- stop/return control when governance reports a blocker;
- hand the completed evidence set back to governance for gate evaluation.

Representative unified commands may expose orchestration through the main `tunner` CLI:

```text
tunner work start TUN-184
tunner work context TUN-184
tunner work run TUN-184
tunner work validate TUN-184
tunner work handoff TUN-184
```

The clean user-facing CLI may delegate internally to governance, context, orchestrator, test and development tooling.

### 2.6 Governance owns orchestration policy

The orchestrator may optimize execution order but it cannot:

- bypass prerequisite or decision gates;
- suppress a mandatory role review;
- mark work DONE;
- authorize a release;
- widen Product/business scope;
- replace missing authority with generated assumptions.

Governance remains the control plane.

## 3. Storage model

Governance state is text-based, diffable and schema-validated.

Preferred:

- YAML for human-authored records;
- JSON Schema for validation;
- generated summaries are Markdown/JSON;
- IDs are immutable.

Do not use an opaque local governance database as the only source of truth.

## 4. Intelligent “next” calculation

`tunner governance next` evaluates:

- milestone dependencies;
- work prerequisites;
- blockers;
- failed gates;
- open high-severity defects;
- required decisions;
- release dependencies;
- documentation/evidence requirements.

It may recommend actionable work but cannot override Product backlog ordering without explicit policy.

## 5. Example status

```text
Milestone M02 — Account & Identity
Implementation       94%
Unit Tests           PASS
Integration Tests    PASS
Security             PASS
Documentation        PASS

Reopened:
  TUN-184

Pending:
  Product Acceptance: 3

Result: NOT READY TO CLOSE
Next required action: resolve REOPEN-0042
```

## 6. Governance schemas

Minimum objects:

- milestone;
- sprint;
- work item;
- dependency;
- gate;
- defect;
- reopen;
- Product Decision;
- amendment;
- ADR link;
- release;
- evidence manifest.

Every schema has `schema_version`.

## 7. Tool security

- no production credentials stored by tools;
- secrets resolved from approved environment/vault;
- production actions require explicit safe command and authorization;
- governance tool cannot mutate application business data;
- shell execution allow-list or explicit subcommands;
- logs redact secrets.

## 8. Tool versioning

Governance tooling versions independently from Tunner application.

A generated evidence manifest records tool version.

## 9. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| TOOL-001 | required decision unresolved | affected item cannot enter implementation-ready state |
| TOOL-002 | test evidence missing | work item cannot become DONE |
| TOOL-003 | context built for one work item | only relevant docs/contracts/history included plus authority index |
| TOOL-004 | malformed governance YAML | schema validation fails with precise location |
| TOOL-005 | production test command | destructive suite is automatically excluded |

## 10. Role-aware governance and agent skills

`tunner-governance` and `tunner-context` must support the multidisciplinary role model defined by document 12.

Minimum additions:

- work items declare `activated_roles`;
- governance calculates required roles from impact classification and rejects missing mandatory reviews;
- context packs include the applicable `.agent/skills/*/SKILL.md` manifests and only the supporting resources needed by the activated roles;
- role findings are stored as evidence linked to the work item;
- `tunner governance gate check` validates required role reviews;
- `tunner governance next` includes unresolved role findings, TODOs, defects, blockers and milestone dependencies;
- `tunner context build` includes current project state, exact next action and role-specific authority without relying on chat history.

Repository-native TODO records require at minimum:

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

Free-floating source-code TODOs are prohibited unless linked to a governed TODO/work item identifier.

## 11. Agent-skill validation

`AGENTS.md` and `.agent/skills/*/SKILL.md` are governed engineering instructions. Validation must ensure:

- all mandatory skill directories and `SKILL.md` manifests exist;
- no skill claims authority above FRD/PDR/ADR;
- each skill defines scope, required inputs, checks, outputs, blockers and handoff;
- role activation rules match document 12;
- generated context identifies the skill versions/hash used for the work item;
- skill changes affecting development gates are reviewed like governance-tool changes.


## 12. Adaptive context engine

`tunner-context` is a governed retrieval/indexing system, not a repository-dump command.

It must support:

- authority/repository index;
- module/domain/dependency graph;
- work-item-to-authority/flow/contract/decision links;
- role-aware retrieval;
- configurable token/input budgets;
- `CORE`, `TASK`, `EXPANDED`, `FULL_AUDIT` context modes;
- freshness/hash invalidation;
- bounded Git-history retrieval;
- cached generated summaries with source provenance;
- incremental context escalation;
- explicit `INSUFFICIENT_CONTEXT`;
- audit record of context selections/exclusions.

Repository **access scope** and prompt **context scope** are independent. An Auditor or Cyber Security Engineer may have full repository access while context is initially bounded.

`FULL_AUDIT` means full repository discoverability and governed retrieval, not necessarily placing every file in one model request.

## 13. Context selection algorithm

At minimum, context selection evaluates:

1. current work item/milestone;
2. authority hierarchy;
3. declared/derived module and domain;
4. referenced flows/contracts/decisions;
5. changed/target source files;
6. dependency and dependent graph;
7. required roles;
8. applicable tests/evidence;
9. recent relevant Git history;
10. open blockers/defects/TODOs;
11. context budget and maximum escalation mode.

Selection output includes both `included` and `excluded` artifacts with reasons.

## 14. Context cache integrity

Every generated context artifact stores:

- source artifact IDs/paths;
- content hashes;
- authority versions;
- relevant sections/locators;
- generator/tool version;
- generation timestamp;
- context-policy version;
- summary hash.

Changed source hash invalidates the derived artifact. Invalid/stale context cannot satisfy a governance gate.

## 15. Role-aware access defaults

- normal feature engineering: `TASK`;
- cross-module architecture/integration: `EXPANDED`;
- finance/security/compliance investigations: `EXPANDED` with full authorized retrieval;
- Auditor/QA/Security release investigations: may escalate to `FULL_AUDIT`;
- milestone/release closure: repository-wide evidence access is permitted and commonly required.

Sensitive data/secrets remain separately controlled and are never exposed merely because context mode is broad.

## 16. Additional tooling acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| TOOL-006 | orchestrator tries to execute blocked work item | governance denies execution |
| TOOL-007 | normal work item context requested | bounded relevant context returned with exclusions |
| TOOL-008 | full repository access role starts review | role can retrieve any authorized artifact without one-shot repository prompt dump |
| TOOL-009 | indexed source changes | dependent cached context becomes stale |
| TOOL-010 | context insufficient | controlled escalation expands dependency/domain scope |
| TOOL-011 | context budget reached | retrieval prioritizes authority/evidence and reports truncation/escalation need instead of silently omitting critical authority |
| TOOL-012 | mandatory role review missing | governance gate fails |


## 17. Skill discovery and context economy

`AGENTS.md` must remain a short navigational/control document. It must not duplicate the full FRD, role instructions, architecture catalog or historical context.

Each skill is a directory containing `SKILL.md` plus optional supporting references, scripts or templates. The context engine initially exposes only skill identity/description/activation metadata; full skill instructions are retrieved when governance activates that role.

This rule prevents role instructions for Finance, Compliance, DevOps, Security, UX and other disciplines from consuming context on unrelated tasks.


## 18. P0 governance-tool implementation baseline

To minimize runtime/tooling sprawl, the reference implementation for `tunner-governance`, `tunner-context`, `tunner-orchestrator` and the unified `tunner` CLI is a separate **.NET 10 / C# 14** tooling solution outside the production application solution.

Baseline package families:

- `System.CommandLine` stable 2.0.x for CLI parsing/help/completion; use the current stable patch at implementation time;
- `YamlDotNet` stable 18.x for human-authored governance YAML; use the current stable patch at implementation time;
- `JsonSchema.Net` stable 9.x for JSON Schema validation where the governance schema requires it; use the current stable patch at implementation time;
- built-in `System.Text.Json`, cryptographic hashing and Git/process integration where sufficient.

The governance state remains text/diff authority. Any generated search/index cache is disposable and reproducible; it never becomes the only source of project state.

## 19. Context retrieval order

Context resolution is hybrid and deterministic-first:

1. explicit work-item/authority/flow/contract/decision links;
2. module/dependency/symbol graph;
3. changed-file and relevant Git-history relationships;
4. lexical/path search;
5. semantic retrieval when useful;
6. governed repository-wide scan when required.

Semantic retrieval is a discovery aid, not authority resolution. A semantic hit never outranks an explicit authority or traceability link.

The MVP does not require a paid embedding/vector service. Semantic indexing may be added behind an adapter after a measured quality/cost trigger; deterministic indexes and lexical retrieval must remain available.
