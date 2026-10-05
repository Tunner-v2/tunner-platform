> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 04 — Governance & Context Tooling Specification

## 1. Objective

Create lightweight repository-native tools before feature development so AI/human contributors cannot silently bypass required development stages.

Tools are outside the production solution.

## 2. Tools

### 2.0 `tunner-authority`

Maintains the repository's hash-verified execution mirror of approved Development Documentation authority.

Representative commands:

```text
tunner authority status
tunner authority import
tunner authority verify
tunner authority diff
```

Responsibilities:

- register immutable baseline archive/version/hash and source URL/ID;
- register active approved amendments and their source IDs/hashes;
- import exact approved authority bytes into the repository authority mirror;
- verify repository mirror hashes before context generation/implementation;
- report drift without silently choosing one side;
- block context/implementation when a required authority artifact fails verification;
- preserve superseded/archived authority history.

The authoritative semantic package before repository bootstrap is the approved Drive baseline + active approved amendments. After import, the repository copy is an **execution mirror**, valid only while its provenance/hash verification passes. `tunner-authority` never invents or auto-merges semantic differences.

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
- evidence manifest;
- post-closure revision.

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

## 20. Normative skill and P0 implementation authorities

Detailed mandatory skill behavior is defined by:

- `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md`.

No P0 developer may reduce the role contracts to a role name/prompt only.

Repository bootstrap, governance/context/orchestrator implementation boundaries, context-cost controls and the P0 feature-development enablement gate are defined by:

- `36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md`.

The GitHub repository and GitHub Actions are **P0-generated execution resources**. Their absence before P0 starts is expected. Actual URLs are registered after creation; placeholder URLs are prohibited.

## 21. Authority mirror prerequisite

Authority verification is a prerequisite to generated context and governed implementation.

`tunner context build` must refuse to treat a repository authority mirror as current when `tunner authority verify` fails. An unavailable remote source does not invalidate an already verified immutable baseline automatically; the local mirror remains usable according to its recorded provenance/freshness policy, while any requested/new authority update waits for verification.

The P0 implementation details are normative in document 36.

## 22. Bootstrap trust and self-hosting cutover

Before `tunner-governance` can enforce itself, P0 uses the bootstrap governance protocol in document 36. This is not a general bypass.

The governance tool must support importing/replaying bootstrap records and producing self-hosting cutover evidence. After successful cutover, unrestricted bootstrap mode is disabled and ordinary lifecycle/gate enforcement becomes mandatory for all remaining work.

## 23. PR integration state and non-blocking local progress

`tunner-governance` must model work readiness separately from pull-request integration state.

At minimum, dependency records support a required readiness value:

```yaml
required_readiness: LOCAL_VALIDATED | MERGED_TO_MAIN
```

`LOCAL_VALIDATED` means the prerequisite has passed its required build/test/review/evidence gates against an identified committed validation candidate on the governed branch lineage, with no work-item-owned behavior-affecting changes left uncommitted after that validation. `MERGED_TO_MAIN` means the dependent operation is not valid until protected-main integration has occurred.

`tunner governance next` must:

1. treat `APPROVAL_PENDING` as an integration state, not an automatic global blocker;
2. continue to expose independent eligible work;
3. expose dependent work when its prerequisite requirement is satisfied by `LOCAL_VALIDATED`;
4. block only dependent scope requiring `MERGED_TO_MAIN` until merge;
5. preserve the pending PR/integration action in status output;
6. respect configurable limits on open dependent PR chains/concurrent unmerged work;
7. never bypass a human merge/authority gate merely to keep execution moving.

The orchestrator may continue branch commits, testing, evidence generation, documentation/context refresh, specialist reviews and other governance-eligible work while human PR approval is pending.

Human-interaction minimization is a design objective: request human action only when a documented authority/integration policy requires it, not as a default synchronization point.

### Committed-state readiness enforcement — AMD-0002 clarification

`tunner-governance`, evidence generation and handoff validation must enforce:

- `repository_changed`;
- `validated_commit_sha`;
- `tested_commit_sha` or equivalent proof that final validation ran against the validation candidate;
- `work_item_commit_shas`;
- optional `closure_commit_sha`;
- remaining uncommitted paths plus classification.

A repository-changing item cannot transition to `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE`, or completed handoff when:

- `validated_commit_sha` is missing;
- work-item-owned changes remain uncommitted;
- required final test/review/evidence was generated against a different source state;
- behavior-affecting files changed after the validated SHA without revalidation.

Unrelated/pre-existing dirty paths may be tolerated only when explicitly classified and excluded from the current item. Governance must never coerce an agent into committing unrelated user work.

A metadata-only closure commit after successful validation is permitted. If that later commit changes source, contract, runtime configuration, migrations, tests, or other behavior-affecting artifacts, local readiness becomes stale and validation must run again.

### Additional tooling acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| TOOL-013 | PR approval pending + independent work exists | `governance next` returns eligible work plus pending integration action |
| TOOL-014 | dependency requires `LOCAL_VALIDATED` and prerequisite passed local gates | dependent work eligible before merge |
| TOOL-015 | dependency requires `MERGED_TO_MAIN` | dependent work blocked until protected-main merge evidence exists |
| TOOL-016 | PR approval pending while tests/evidence remain | orchestrator continues authorized local validation work |
| TOOL-017 | configured unmerged-chain/concurrency limit reached | additional dependent work is withheld without blocking unrelated eligible work |
| TOOL-022 | repository-changing work has no validated commit SHA | `LOCAL_VALIDATED`/`READY_FOR_MERGE`/`DONE` gate fails |
| TOOL-023 | tests/evidence reference a different commit than the validation candidate | gate fails or evidence is regenerated against the candidate |
| TOOL-024 | behavior-affecting files change after validated SHA | local readiness becomes stale until recommit/revalidation |
| TOOL-025 | unrelated dirty paths exist and are classified | governance preserves them and excludes them from work-item commit/readiness ownership |
| TOOL-026 | work item changes no repository artifact | `repository_changed: false` allows completion without synthetic commit |


## Location-independent path resolution

All Tunner tooling must use the repository path policy defined by document 02 and AMD-0003.

`tunner-authority`, `tunner-governance`, `tunner-context`, `tunner-dev`, `tunner-test` and the orchestrator must share one repository-root/path abstraction rather than independently concatenating absolute paths.

Minimum behavior:

- discover/validate `<repo-root>` dynamically;
- persist repository artifacts as root-relative canonical paths;
- normalize separators for stored logical paths;
- accept runtime absolute paths only as resolved ephemeral values or explicit external configuration;
- reject hardcoded machine/workspace absolute paths during documentation/config/script validation;
- remain functional when the checkout is moved to a different directory or drive;
- never use the current process working directory as an implicit repository-root authority.

Additional acceptance tests:

| ID | Scenario | Expected |
|---|---|---|
| TOOL-018 | checkout is moved to a different parent directory/drive | governance/context/dev/test tools work without modifying committed paths |
| TOOL-019 | command starts from a nested repository directory | standard root resolver finds `.tunner-root`/Git root and resolves the same artifacts |
| TOOL-020 | committed governed config contains a machine-specific absolute repository path | validation fails unless the field is explicitly classified as an external runtime path |
| TOOL-021 | CI provides a different workspace path | workspace is resolved dynamically and no CI absolute path is persisted into repository state |


## 24. Authority-resolution and duplicate-decision enforcement — AMD-0005

`tunner-governance` must treat authority resolution as a prerequisite to missing-authority decisions/blockers.

### Decision/blocker creation

Before accepting `decision create` or a missing-authority blocker, governance validates a schema-versioned Authority Resolution Pass containing exact reviewed authority references and the unresolved gap.

The tool must reject creation when:

- authority verification is not current;
- applicable authority review is absent;
- an active applicable ADR/PDR/amendment/development rule already resolves the question;
- the proposed decision materially duplicates active authority without explicit correction/supersession/conflict/revisit lineage;
- the alleged gap is only the absence of a trigger for a conditional/selective capability;
- context is known insufficient and retrieval has not been expanded.

When context is insufficient, return `INSUFFICIENT_CONTEXT`; do not manufacture a decision request.

### Authority-aware discovery

The context/authority index must support exact refs plus bounded aliases/terms sufficient to find applicable authority, but search/index results are discovery aids only. Final resolution cites verified source authority.

Generated context must not present stale or nonexistent legacy decision filenames as applicable authority. Such references are regeneration/tooling defects, not decision inputs.

### `governance next`

`governance next` must distinguish:

- genuine required authority gap;
- implementation defect/nonconformance;
- intentional authority-change proposal;
- conditional activation trigger absent;
- insufficient context.

An untriggered optional/selective capability does not block unrelated work or manufacture an ADR dependency.

### Acceptance

| ID | Scenario | Expected |
|---|---|---|
| TOOL-AUTH-001 | decision create without authority-resolution evidence | rejected |
| TOOL-AUTH-002 | active ADR already resolves proposed choice | duplicate decision rejected; existing ADR returned |
| TOOL-AUTH-003 | selective technology has no trigger | no missing-contract blocker; trigger remains dormant |
| TOOL-AUTH-004 | context index emits nonexistent legacy decision ref | context verification fails/regenerates; ref is not treated as authority |
| TOOL-AUTH-005 | authority search remains inconclusive | `INSUFFICIENT_CONTEXT` and bounded expansion occur before blocker/decision creation |


## 25. Post-closure Revision (`RV`) governance

`tunner-governance` must support append-only Revision lineage for work discovered after a milestone/work item was legitimately closed.

Revision support must not reinterpret `AMD-*` authority amendments. `RV` is work/lifecycle lineage; ADR/PDR/amendment remains authority lineage.

Minimum revision record:

```yaml
schema_version: 1
revision_id: ""
revision_scope: MILESTONE # MILESTONE | WORK_ITEM
original_scope_id: ""
parent_revision_id: null
parent_milestone_revision_id: null
title: ""
revision_type: CORRECTIVE
status: PROPOSED
reason: ""
authority_refs: []
decision_refs: []
defect_refs: []
change_refs: []
impact_classification: SCOPED_BLOCKING
blocked_scope: []
acceptance_criteria: []
prerequisites: []
work_items: []
required_gates: []
required_roles: []
evidence_refs: []
created_at: null
closed_at: null
```

Identifier allocation is monotonic per original scope. Examples: `P0-RV001`, `P0-004-RV001`. Numeric formatting is not an artificial count limit.

Representative commands:

```text
tunner governance revision create --scope P0 --type CORRECTIVE --title "..."
tunner governance revision show P0-RV001
tunner governance revision close-check P0-RV001
```

Equivalent CLI shape is acceptable, but the governed semantics are mandatory.

`governance next` must:

1. expose eligible active Revision work together with ordinary eligible work;
2. preserve historical original `CLOSED`/`DONE` state;
3. respect revision impact/blocking scope rather than globally invalidating unrelated work;
4. order revision dependencies and previous-revision lineage deterministically;
5. refuse a Revision that attempts to change authority without the applicable approved ADR/PDR/amendment;
6. include active/latest revisions in generated current state, context and handoff.

Context generation for a Revision loads the original scope/evidence plus only the delta-relevant authority, source, tests, defects/changes and prior Revision lineage required for the task.

Acceptance:

| ID | Scenario | Expected |
|---|---|---|
| TOOL-REV-001 | closed milestone gets new corrective work | revision ID allocated; original remains CLOSED |
| TOOL-REV-002 | closed work item gets second revision | `RV002` links original/prior lineage without overwriting either |
| TOOL-REV-003 | revision is SCOPED_BLOCKING | only declared dependent scope is withheld by `governance next` |
| TOOL-REV-004 | revision tries to change authority without approved authority record | revision activation/gate fails |
| TOOL-REV-005 | current-state/context generated with active revision | historical closure and active revision are both visible and consistent |
