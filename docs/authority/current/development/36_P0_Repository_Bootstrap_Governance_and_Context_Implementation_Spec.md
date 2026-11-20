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
<repo-root>/
  .tunner-root
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




1. repository authority mirror imports baseline 1.6.0 + all active approved amendments and passes provenance/hash verification;
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




## 18A. Design-fidelity governance/evidence capability — AMD-0008




Global change authority: https://drive.google.com/file/d/1DErZZpc3rIwzfpADnDzD9a361draTuhW/view. As the P0 governance/control-plane contract is implemented or revised, support UX work-item fields `figma_file/node/version`, approved screen ID, relevant FRD/flow, referenced component/tokens/assets, expected viewports/states, visual screenshots/diff, `UI_FIDELITY_DEFECT` history, proposed design deviations, owner approval evidence, and `UI_VISUAL_APPROVAL` gate result. Relevant specialist skill contracts must be active for UI work.




AI may generate a proposed missing-screen design but cannot mark it approved. Unapproved material deviations and missing approved visual elements (including gradients/glows/page backgrounds, field styling and complete composition) block acceptance of affected production UI. This addition does not convert UX work into P0 product feature implementation; P0 implements enabling governance contracts while actual screen work remains governed by the original P0/P1–P8 authorization boundaries.




## 18B. Global validation and URL governance — AMD-0009


AMD-0009 (https://drive.google.com/file/d/1VeaNofocTrsbXwdePg8VSVuembRQxr7A/view) applies across **all Tunner-owned current and future screens/forms**, not registration alone. Governance and evidence models must map each full-screen route to pathname, screen ID, Figma/FRD, authorization, reload/history/deep-link tests; map each form to shared approved Figma-derived validation components, applicable domain validator/backend checks and field/summary error states. Enforce `URL-001..006` and `VAL-001..008` for each affected screen/form; registration pass does not waive another form's gate. New material Figma visual variations require Product Owner approval under AMD-0008. P0/P1 scope boundaries unchanged.


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




## 22. Non-blocking PR approval implementation contract




P0 governance tooling must implement the distinction between local validation readiness and protected-main integration readiness so human PR approval does not become an unnecessary global development synchronization point.




### 22.1 Separate work and PR states




Work-item execution state and pull-request integration state are separate records. A work item may be `READY_FOR_MERGE` while its PR is `APPROVAL_PENDING`.




A pending approval remains visible as an integration action but is not automatically a work-item blocker.




### 22.2 Typed dependency readiness




Dependency records must declare the minimum state required from the prerequisite:




```yaml
prerequisite_id: TUN-P0-003
required_readiness: LOCAL_VALIDATED
```




or:




```yaml
prerequisite_id: TUN-P0-003
required_readiness: MERGED_TO_MAIN
```




The default must not silently escalate to `MERGED_TO_MAIN`. The work-item/backlog authority or governance policy must resolve the required readiness explicitly/deterministically.




### 22.3 `tunner governance next`




While one or more PRs are `APPROVAL_PENDING`, `next` must calculate and expose:




- independent agent-executable work;
- local-validation/review/evidence work on the pending lineage;
- dependent work whose readiness requirement is already met;
- pending human integration actions separately;
- scopes blocked specifically on `MERGED_TO_MAIN`;
- concurrency/dependent-chain limits and their reason when reached.




The agent should request human interaction only when no higher-priority governance-eligible agent work remains or when the human gate itself is the required next action for the affected scope.




### 22.4 Reviewability and concurrency




The policy must prevent unlimited unmerged dependent chains. Configure limits by governance policy (for example maximum open dependent chain or maximum concurrent unmerged work) rather than hard-coding a universal number in application logic.




Independent work may continue even when a dependent-chain limit has been reached.




### 22.5 Committed-state local readiness




`LOCAL_VALIDATED` is a committed-state readiness marker, not a dirty-worktree marker.




Governance tooling established in P0 must require, for repository-changing work:




```yaml
git_state:
  repository_changed: true
  validated_commit_sha: ""
  tested_commit_sha: ""
  work_item_commit_shas: []
  closure_commit_sha: null
  remaining_uncommitted_paths: []
  remaining_uncommitted_classification: NONE
```




Semantics:




- `validated_commit_sha` identifies the source state that passed required final gates;
- `tested_commit_sha` must resolve to the same effective behavior-affecting state;
- `work_item_commit_shas` records the cohesive commits owned by the item;
- `closure_commit_sha` may differ when only governance/evidence/handoff metadata is committed after validation;
- work-item-owned uncommitted changes make local readiness invalid;
- unrelated/pre-existing dirty files may remain only with explicit classification and must not be swept into the item;
- behavior-affecting changes after the validated SHA invalidate readiness until recommitted and revalidated;
- `repository_changed: false` is valid for evaluation/no-op items and does not require a synthetic commit.




This requirement applies to all later planes through AMD-0002's global applicability.




### 22.6 P0 acceptance additions




| ID | Scenario | Expected |
|---|---|---|
| P0-GOV-015 | PR awaits required approval | pending merge is reported but unrelated/local-eligible work continues |
| P0-GOV-016 | downstream prerequisite requirement is `LOCAL_VALIDATED` | downstream local execution may start after prerequisite validation evidence |
| P0-GOV-017 | downstream requirement is `MERGED_TO_MAIN` | dependent scope remains blocked until merge evidence |
| P0-GOV-018 | agent has pending human merge action plus executable work | human action and agent work are both surfaced; agent continues work |
| P0-GOV-019 | configured dependent-chain limit reached | no further dependent chain growth; independent work remains eligible |
| P0-GOV-024 | repository-changing work claims `LOCAL_VALIDATED` without validated commit SHA | governance refuses transition |
| P0-GOV-025 | work-item-owned files remain uncommitted at completed handoff | handoff/closure gate fails |
| P0-GOV-026 | unrelated/pre-existing dirty paths are present | paths remain untouched when explicitly classified; work-item commit excludes them |
| P0-GOV-027 | behavior-affecting change occurs after validated SHA | validation/evidence becomes stale until recommitted/revalidated |








## Location-independent repository root and path contract




P0 must establish one shared location-independent path contract before downstream tools depend on repository paths.




### Root marker and discovery




The repository root contains `.tunner-root`.




Resolution order:




1. explicit validated `--repo-root`;
2. validated `TUNNER_REPO_ROOT`;
3. Git top-level;
4. upward `.tunner-root` discovery;
5. fail clearly if no unique root is found.




The current working directory is never itself proof of repository root.




### Persisted path format




Repository-internal paths stored in YAML/JSON/Markdown/evidence/context/indexes use root-relative logical paths with `/` separators, for example:




```text
docs/authority/current-authority.json
governance/work-items/TUN-P0-035.yaml
tools/src/Tunner.Context/
```




Do not persist the host checkout prefix.




### Runtime absolute paths




A tool may resolve an absolute path in memory to access the filesystem. That absolute path is ephemeral runtime state. When an artifact is within the repository, persisted references are converted back to repository-relative form.




External filesystem locations are allowed only when explicitly configured/authorized as external runtime resources. They must not redefine repository structure or authority.




### Portable execution




- PowerShell/shell scripts locate the repository via the shared resolver, not caller CWD.
- .NET project/solution references remain relative.
- Docker/Compose paths derive from the repository location or native relative Compose semantics.
- GitHub Actions/CI workspace paths are runtime variables and are never committed as project paths.
- Test/evidence output uses relative source/artifact paths wherever the referenced item is in-repository.




### Portability gate




P0 cannot close until:




1. the repository is copied/cloned/moved to a different parent location;
2. no committed file is edited for path reasons;
3. authority/governance/context/dev/test commands pass;
4. nested-directory invocation passes;
5. path validation finds no unapproved machine-specific absolute repository path.




Additional acceptance tests:




| ID | Scenario | Expected |
|---|---|---|
| P0-GOV-020 | checkout location changes | P0 tools and build resolve correctly without path edits |
| P0-GOV-021 | command invoked below repo root | `.tunner-root`/Git discovery resolves root |
| P0-GOV-022 | generated evidence references an in-repo file | persisted path is repository-relative |
| P0-GOV-023 | committed config contains original machine drive/workspace path | path validation fails |








## 23. Post-closure Revision governance implementation contract




The P0 governance control plane must support post-closure Revision (`RV`) lineage so later maintenance does not require overwriting or repeatedly reopening historically closed work.




Repository state includes a diffable location such as:




```text
governance/revisions/
```




The exact folder may follow the established governance layout, but Revision records must remain repository-native, schema-validated and auditable.




Minimum implementation requirements:




1. schema validation for the Revision record defined by document 04;
2. deterministic monotonic ID allocation per original scope (`P0-RV001`, `P0-004-RV001`, etc.);
3. lookup of original scope and prior Revision lineage;
4. immutable original closure/evidence preservation;
5. lifecycle/gate enforcement for Revision state;
6. revision impact/blocking classification;
7. authority-boundary validation preventing an RV from silently changing ADR/PDR/amendment authority;
8. `governance next` integration;
9. context/project-memory/handoff generation including active/latest Revision state;
10. ordinary Git/commit/PR/evidence requirements for Revision implementation;
11. clean fresh-agent recovery of Revision state without chat history.




Revision child work uses the existing governed work-item machinery and carries `revision_ref` back to its owning Revision.




A closed original item is not mutated merely to indicate current Revision activity. Effective health is computed from original closure + applicable active/latest Revision state.




P0 acceptance additions:




| ID | Scenario | Expected |
|---|---|---|
| P0-RV-001 | create first milestone Revision | deterministic `RV001`; original milestone remains closed |
| P0-RV-002 | create later Revision | monotonic lineage resolves prior revision and original scope |
| P0-RV-003 | generate project state | historical status + active Revision shown without contradiction |
| P0-RV-004 | Revision attempts authority change without approved record | activation/gate fails |
| P0-RV-005 | fresh agent resumes active Revision | exact scope, blockers, evidence and next action recovered without chat |