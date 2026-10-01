using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Tunner.Governance;

var failures = new List<string>();
var repositoryRoot = FindRepositoryRoot();
var fixtureRoot = Directory.CreateTempSubdirectory("tunner-governance-fixture-");

try
{
    CreateFixture(repositoryRoot, fixtureRoot);
    InitializeGit(fixtureRoot);

    var authority = AuthorityApplication.Verify(fixtureRoot);
    Expect(authority.ExitCode == 0, "A complete authority fixture must verify.", failures);

    var validation = GovernanceApplication.Validate(fixtureRoot);
    Expect(validation.ExitCode == 0, "A complete fixture must validate.", failures);

    var status = GovernanceApplication.Status(fixtureRoot);
    Expect(status.ExitCode == 0, "Status must be available for a valid fixture.", failures);

    var next = GovernanceApplication.Next(fixtureRoot);
    Expect(next.ExitCode == 0, "Next must be available for a valid fixture.", failures);
    var nextPayload = (NextPayload)next.Payload;
    Expect(nextPayload.Items.Single(item => item.WorkItemId == "TUN-LOCAL").Actionable, "LOCAL_VALIDATED dependency must permit local work.", failures);
    Expect(!nextPayload.Items.Single(item => item.WorkItemId == "TUN-MERGED").Actionable, "MERGED_TO_MAIN dependency must block the dependent work.", failures);
    Expect(!nextPayload.Items.Single(item => item.WorkItemId == "TUN-CHAIN").Actionable, "Configured local unmerged-chain limit must block further dependent chaining.", failures);
    Expect(nextPayload.HumanIntegrationActions.SingleOrDefault(item => item.WorkItemId == "TUN-MERGED" && item.PrerequisiteWorkItemId == "TUN-001") is not null, "Merged-main dependency must surface a separate human integration action.", failures);

    var sourcesCurrent = SourceRegistryApplication.Check(fixtureRoot, "TUN-001", "2026-09-30", 14);
    Expect(sourcesCurrent.ExitCode == 0, "Current registered source evidence must pass.", failures);
    var sourcesStale = SourceRegistryApplication.Check(fixtureRoot, "TUN-001", "2026-10-20", 14);
    Expect(sourcesStale.ExitCode == 1, "Source evidence outside the explicit freshness window must fail.", failures);
    File.AppendAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "source-evidence.json"), " ");
    var sourcesTampered = SourceRegistryApplication.Check(fixtureRoot, "TUN-001", "2026-09-30", 14);
    Expect(sourcesTampered.ExitCode == 1, "Changed source evidence must fail hash integrity.", failures);
    var contextOutput = new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "docs", "context", "current"));
    var contextBuild = ContextApplication.Build(fixtureRoot, "TUN-001", contextOutput);
    Expect(contextBuild.ExitCode == 0, "Context build must generate a bounded fixture pack.", failures);
    var contextPayload = (ContextBuildPayload)contextBuild.Payload;
    Expect(contextPayload.Included.Any(item => item.Path == "docs/authority/current-authority.json"), "Context build must include the current authority summary.", failures);
    var contextCurrent = ContextApplication.Verify(fixtureRoot, contextOutput);
    Expect(contextCurrent.ExitCode == 0, "A newly generated context pack must verify as current.", failures);
    File.AppendAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-001.yaml"), "\n# Fixture source changed");
    var contextStale = ContextApplication.Verify(fixtureRoot, contextOutput);
    Expect(contextStale.ExitCode == 1, "Context verify must report a changed included source as stale.", failures);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "authority"), "fixture authority reference" + Environment.NewLine);
    File.Copy(Path.Combine(repositoryRoot.FullName, "AGENTS.md"), Path.Combine(fixtureRoot.FullName, "AGENTS.md"), true);
    var fixtureSkillDirectory = Path.Combine(fixtureRoot.FullName, ".agent", "skills", "governance-engineer");
    Directory.CreateDirectory(fixtureSkillDirectory);
    File.Copy(Path.Combine(repositoryRoot.FullName, ".agent", "skills", "governance-engineer", "SKILL.md"), Path.Combine(fixtureSkillDirectory, "SKILL.md"), true);
    var fixtureSourceDirectory = Path.Combine(fixtureRoot.FullName, "src", "Tunner.Governance");
    Directory.CreateDirectory(fixtureSourceDirectory);
    File.Copy(Path.Combine(repositoryRoot.FullName, "src", "Tunner.Governance", "AdaptiveContextApplication.cs"), Path.Combine(fixtureSourceDirectory, "AdaptiveContextApplication.cs"), true);
    var adaptiveCoreOutput = new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "docs", "context", "adaptive-core"));
    var adaptiveCore = AdaptiveContextApplication.Build(fixtureRoot, "TUN-001", adaptiveCoreOutput, new AdaptiveContextRequest("CORE", null, "FULL"));
    Expect(adaptiveCore.ExitCode == 0 && ((ContextBuildPayload)adaptiveCore.Payload).Included.All(item => item.Reason is "repository entry contract" or "current authority summary" or "selected work item" or "explicit authority reference" or "activated role skill"), "CORE context must retain only mandatory context for the fixture.", failures);
    var adaptiveBudget = AdaptiveContextApplication.Build(fixtureRoot, "TUN-001", new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "docs", "context", "adaptive-budget")), new AdaptiveContextRequest("TASK", 1, "REPOSITORY_READ"));
    Expect(adaptiveBudget.ExitCode == 1 && ((ContextBuildPayload)adaptiveBudget.Payload).Outcome == "CONTEXT_BUDGET_INSUFFICIENT", "Context must fail closed when the token budget cannot contain mandatory authority.", failures);
    var adaptiveEscalation = AdaptiveContextApplication.Escalate(fixtureRoot, "TUN-001", "TASK", "module dependency is missing");
    Expect(adaptiveEscalation.ExitCode == 0 && ((AdaptiveEscalationPayload)adaptiveEscalation.Payload).NextMode == "EXPANDED", "Insufficient context must return controlled escalation instead of implicitly loading sources.", failures);
    var adaptiveFullOutput = new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "docs", "context", "adaptive-full"));
    var adaptiveFull = AdaptiveContextApplication.Build(fixtureRoot, "TUN-001", adaptiveFullOutput, new AdaptiveContextRequest("FULL_AUDIT", null, "FULL"));
    Expect(adaptiveFull.ExitCode == 0 && File.Exists(Path.Combine(fixtureRoot.FullName, "artifacts", "context-index", "index.json")), "FULL_AUDIT must create a disposable repository index rather than a prompt dump.", failures);
    Expect(AdaptiveContextApplication.Verify(fixtureRoot, adaptiveFullOutput).ExitCode == 0, "Fresh FULL_AUDIT context must verify.", failures);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "unrelated-context-source.txt"), "changed" + Environment.NewLine);
    Expect(AdaptiveContextApplication.Verify(fixtureRoot, adaptiveFullOutput).ExitCode == 1, "FULL_AUDIT index must become stale when an indexed repository source changes.", failures);

    RunGit(fixtureRoot, "add", ".");
    RunGit(fixtureRoot, "commit", "--quiet", "-m", "fixture pre-evidence state");
    var evidenceInput = Path.Combine(fixtureRoot.FullName, "governance", "evidence", "evidence-input.json");
    File.WriteAllText(evidenceInput, "{\"result\":\"pass\"}" + Environment.NewLine);
    RunGit(fixtureRoot, "add", "governance/evidence/evidence-input.json");
    RunGit(fixtureRoot, "commit", "--quiet", "-m", "fixture evidence input");
    var firstManifestPath = new FileInfo(Path.Combine(fixtureRoot.FullName, "artifacts", "evidence", "first.json"));
    var evidence = EvidenceApplication.Generate(
        fixtureRoot,
        "TUN-001",
        firstManifestPath,
        ["governance\\work-items\\TUN-001.yaml"],
        ["governance/evidence/evidence-input.json"],
        ["governance/evidence/evidence-input.json"],
        ["governance/evidence/evidence-input.json"]);
    Expect(evidence.ExitCode == 0, "Evidence generation must accept bounded local references.", failures);
    var generatedManifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(firstManifestPath.FullName));
    Expect(generatedManifest is not null && generatedManifest.ScopeId == "TUN-001" && generatedManifest.CommitSha.Length == 40, "Evidence manifest must record exact work-item and Git identity.", failures);
    Expect(generatedManifest is not null && generatedManifest.WorkingTreeState == "CLEAN" && generatedManifest.Builds.Count == 1 && generatedManifest.Tests.Count == 1 && generatedManifest.SecurityScans.Count == 1 && generatedManifest.Artifacts.Count == 1, "Evidence manifest must record all required evidence categories without duplicate content.", failures);
    var overwriteEvidence = EvidenceApplication.Generate(fixtureRoot, "TUN-001", firstManifestPath, [], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"]);
    Expect(overwriteEvidence.ExitCode == 2, "Evidence generation must reject an existing output path.", failures);
    var secondManifestPath = new FileInfo(Path.Combine(fixtureRoot.FullName, "artifacts", "evidence", "second.json"));
    var secondEvidence = EvidenceApplication.Generate(
        fixtureRoot,
        "TUN-001",
        secondManifestPath,
        [],
        ["governance/evidence/evidence-input.json"],
        ["governance/evidence/evidence-input.json"],
        ["governance/evidence/evidence-input.json"]);
    Expect(secondEvidence.ExitCode == 0 && File.ReadAllText(firstManifestPath.FullName) == File.ReadAllText(secondManifestPath.FullName), "Identical inputs must produce byte-identical manifests.", failures);
    var escapedOutput = new FileInfo(Path.Combine(fixtureRoot.Parent!.FullName, "escaped-manifest.json"));
    var escapedEvidence = EvidenceApplication.Generate(fixtureRoot, "TUN-001", escapedOutput, [], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"]);
    Expect(escapedEvidence.ExitCode == 2 && !escapedOutput.Exists, "Evidence generation must reject output paths outside the repository.", failures);
    var secretLikeFileName = string.Concat("gh", "p_", "abcdefghijklmnopqrstuvwxyz1234.txt");
    var secretNamedInput = Path.Combine(fixtureRoot.FullName, "governance", "evidence", secretLikeFileName);
    File.WriteAllText(secretNamedInput, "fixture" + Environment.NewLine);
    var secretOutput = new FileInfo(Path.Combine(fixtureRoot.FullName, "artifacts", "evidence", "secret.json"));
    var secretEvidence = EvidenceApplication.Generate(fixtureRoot, "TUN-001", secretOutput, [$"governance/evidence/{secretLikeFileName}"], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"], ["governance/evidence/evidence-input.json"]);
    Expect(secretEvidence.ExitCode == 2 && !secretOutput.Exists, "Evidence generation must reject a manifest that would expose a secret-shaped value.", failures);
    var gate = GovernanceApplication.CheckGate(fixtureRoot, "TUN-TRANSITION");
    Expect(gate.ExitCode == 1, "Gate check must reject missing evidence and role-review evidence.", failures);

    var transition = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-TRANSITION", "DONE");
    Expect(transition.ExitCode == 1, "Lifecycle check must reject DRAFT directly to DONE.", failures);

    var rolePolicyPath = Path.Combine(fixtureRoot.FullName, "governance", "policies", "role-activation-policy.yaml");
    File.Copy(Path.Combine(repositoryRoot.FullName, "governance", "policies", "role-activation-policy.yaml"), rolePolicyPath, true);
    WriteRoleMatrixWorkItem(fixtureRoot, "[full-stack-engineer, tester-qa-engineer, governance-engineer]");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "TUN-P0-027-ROLE-REVIEWS.json"), "{\"schema_version\":1}" + Environment.NewLine);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "role-matrix-ready.json"), "{\"result\":\"PASS\"}" + Environment.NewLine);
    var roleMissing = GovernanceApplication.CheckGate(fixtureRoot, "TUN-P0-027");
    Expect(roleMissing.ExitCode == 1 && ((GatePayload)roleMissing.Payload).Failures.Any(item => item.Contains("mandatory role is not activated: auditor", StringComparison.Ordinal)), "Gate must reject a calculated mandatory role omitted from activation.", failures);

    WriteRoleMatrixWorkItem(fixtureRoot, "[full-stack-engineer, tester-qa-engineer, governance-engineer, auditor]");
    foreach (var role in new[] { "full-stack-engineer", "tester-qa-engineer", "governance-engineer", "auditor" })
    {
        WriteStructuredReview(fixtureRoot, role, "PASS", "[]");
    }

    var roleReady = GovernanceApplication.CheckGate(fixtureRoot, "TUN-P0-027");
    Expect(roleReady.ExitCode == 0, "Gate must accept complete passing structured reviews for every calculated role.", failures);
    WriteStructuredReview(fixtureRoot, "auditor", "BLOCKED", "[audit evidence is incomplete]");
    var roleBlocked = GovernanceApplication.CheckGate(fixtureRoot, "TUN-P0-027");
    Expect(roleBlocked.ExitCode == 1 && ((GatePayload)roleBlocked.Payload).Failures.Any(item => item.Contains("mandatory role review did not pass: auditor", StringComparison.Ordinal)), "A blocking specialist review must block only its affected work-item gate.", failures);

    WriteOrchestratorWorkItem(fixtureRoot);
    var orchestratorStart = OrchestratorApplication.Start(fixtureRoot, "TUN-ORCH");
    Expect(orchestratorStart.ExitCode == 0 && ((OrchestratorPayload)orchestratorStart.Payload).Outcome == "ELIGIBLE", "Orchestrator must accept an eligible in-progress work item without mutating it.", failures);
    var orchestratorRun = OrchestratorApplication.Run(fixtureRoot, "TUN-ORCH");
    Expect(orchestratorRun.ExitCode == 0 && ((OrchestratorPayload)orchestratorRun.Payload).Outcome == "PLAN_READY", "Orchestrator run must return a plan instead of executing Product operations.", failures);
    var orchestratorPath = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-ORCH.yaml");
    File.WriteAllText(orchestratorPath, File.ReadAllText(orchestratorPath).Replace("status: IN_PROGRESS", "status: CODE_REVIEW"));
    var orchestratorReviewState = OrchestratorApplication.Start(fixtureRoot, "TUN-ORCH");
    Expect(orchestratorReviewState.ExitCode == 0, "Orchestrator must permit governed review-state validation work.", failures);
    var orchestratorRefused = OrchestratorApplication.Start(fixtureRoot, "TUN-TRANSITION");
    Expect(orchestratorRefused.ExitCode == 1 && ((OrchestratorPayload)orchestratorRefused.Payload).Outcome == "LIFECYCLE_REFUSED", "Orchestrator must refuse a non-ready lifecycle state.", failures);
    var authorityMirrorPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "mirror.txt");
    File.AppendAllText(authorityMirrorPath, "tampered");
    var authorityTampered = AuthorityApplication.Verify(fixtureRoot);
    Expect(authorityTampered.ExitCode == 1, "Authority verify must reject changed authority bytes.", failures);
    var validationBlocked = GovernanceApplication.Validate(fixtureRoot);
    Expect(validationBlocked.ExitCode == 1, "Governance validation must block when authority verification fails.", failures);
    var nextBlocked = GovernanceApplication.Next(fixtureRoot);
    Expect(nextBlocked.ExitCode == 1 && ((NextPayload)nextBlocked.Payload).Items.All(item => !item.Actionable), "Governance next must block execution when authority verification fails.", failures);
    var gateBlocked = GovernanceApplication.CheckGate(fixtureRoot, "TUN-001");
    Expect(gateBlocked.ExitCode == 1, "Governance gates must block when authority verification fails.", failures);
    var contextBlocked = ContextApplication.Build(fixtureRoot, "TUN-001", contextOutput);
    Expect(contextBlocked.ExitCode == 1, "Context build must block when authority verification fails.", failures);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "malformed.yaml"), "schema_version: [");
    var malformed = GovernanceApplication.Validate(fixtureRoot);
    Expect(malformed.ExitCode == 1, "Validation must reject malformed YAML.", failures);
}
finally
{
    DeleteFixture(fixtureRoot);
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("PASS: Tunner.Governance functional checks");
return 0;

static void Expect(bool condition, string message, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

static DirectoryInfo FindRepositoryRoot()
{
    for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Tunner.Governance.sln")))
        {
            return current;
        }
    }

    throw new InvalidOperationException("Unable to locate the repository root.");
}

static void CreateFixture(DirectoryInfo repositoryRoot, DirectoryInfo fixtureRoot)
{
    var schemaSource = new DirectoryInfo(Path.Combine(repositoryRoot.FullName, "governance", "schemas", "v1"));
    var schemaDestination = new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "governance", "schemas", "v1"));
    CopyDirectory(schemaSource, schemaDestination);
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "milestones"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "work-items"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "dependencies"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "policies"));
    File.Copy(Path.Combine(repositoryRoot.FullName, "governance", "policies", "role-activation-policy.yaml"), Path.Combine(fixtureRoot.FullName, "governance", "policies", "role-activation-policy.yaml"), true);
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "evidence"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "reviews"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "rd"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "docs", "authority"));
    var currentAuthorityPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "current-authority.json");
    var authorityMirrorPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "mirror.txt");
    File.WriteAllText(currentAuthorityPath, "{\"effective_authority\":\"fixture\"}" + Environment.NewLine);
    File.WriteAllText(authorityMirrorPath, "fixture authority bytes" + Environment.NewLine);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "BOOT-P0-001-integrity.json"), $$"""
{
  "checks": [
    { "local_path": "docs/authority/current-authority.json", "expected_sha256": "{{HashFile(currentAuthorityPath)}}" },
    { "local_path": "docs/authority/mirror.txt", "expected_sha256": "{{HashFile(authorityMirrorPath)}}" }
  ]
}
""");

    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "milestones", "MVP.yaml"), """
schema_version: 1
milestone_id: MVP
title: Fixture milestone
status: ACTIVE
goal: Validate governance behavior
scope: [governance]
authority_refs: [authority]
prerequisites: []
work_items: [TUN-001, TUN-TRANSITION, TUN-LOCAL, TUN-MERGED, TUN-CHAIN]
required_gates: []
required_roles: []
evidence_refs: []
known_risks: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");

    WriteWorkItem(fixtureRoot, "TUN-001", "VALIDATION", "[]");
    WriteWorkItem(fixtureRoot, "TUN-TRANSITION", "DRAFT", "[]");
    WriteWorkItem(fixtureRoot, "TUN-LOCAL", "VALIDATION", "[TUN-001]");
    WriteWorkItem(fixtureRoot, "TUN-CHAIN", "BACKLOG", "[TUN-LOCAL]");
    WriteWorkItem(fixtureRoot, "TUN-MERGED", "BACKLOG", "[TUN-001]");

    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "dependencies", "DEP-LOCAL.yaml"), """
schema_version: 1
dependency_id: DEP-LOCAL
work_item_id: TUN-LOCAL
prerequisite_work_item_id: TUN-001
required_readiness: LOCAL_VALIDATED
reason: Fixture local dependency
status: ACTIVE
evidence_refs: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "dependencies", "DEP-CHAIN.yaml"), """
schema_version: 1
dependency_id: DEP-CHAIN
work_item_id: TUN-CHAIN
prerequisite_work_item_id: TUN-LOCAL
required_readiness: LOCAL_VALIDATED
reason: Fixture chained local dependency
status: ACTIVE
evidence_refs: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "dependencies", "DEP-MERGED.yaml"), """
schema_version: 1
dependency_id: DEP-MERGED
work_item_id: TUN-MERGED
prerequisite_work_item_id: TUN-001
required_readiness: MERGED_TO_MAIN
reason: Fixture protected-main dependency
status: ACTIVE
evidence_refs: [governance/evidence/merged-main.json]
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");

    var sourceEvidencePath = Path.Combine(fixtureRoot.FullName, "governance", "evidence", "source-evidence.json");
    File.WriteAllText(sourceEvidencePath, """
{
  "schema_version": 1,
  "scope_type": "WORK_ITEM",
  "scope_id": "TUN-001",
  "reviewed_at": "2026-09-30T00:00:00Z",
  "sources": [{ "url": "https://example.test/source", "finding": "fixture primary source" }]
}
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "rd", "source-registry.json"), $$"""
{
  "schema_version": 1,
  "entries": [{ "scope_type": "WORK_ITEM", "scope_id": "TUN-001", "evidence_path": "governance/evidence/source-evidence.json", "evidence_sha256": "{{HashFile(sourceEvidencePath)}}", "reviewed_on": "2026-09-30", "source_authority": "PRIMARY" }]
}
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "policies", "pr-integration-policy.yaml"), """
schema_version: 1
policy_id: fixture-pr-integration
approval_pending_blocks_global_execution: false
continue_independent_work: true
continue_local_validation: true
default_dependency_readiness: LOCAL_VALIDATED
require_explicit_merged_main_for: []
dependent_unmerged_chain_limit: 1
concurrent_unmerged_work_limit: null
request_human_action_only_when_required: true
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "policies", "lifecycle-policy.yaml"), """
schema_version: 1
work_item:
  normal: [DRAFT, BACKLOG, REFINEMENT, READY, IN_PROGRESS, CODE_REVIEW, VALIDATION, PRODUCT_ACCEPTANCE, DONE]
  side: [BLOCKED, NEEDS_PRODUCT_DECISION]
milestone:
  normal: [PROPOSED, APPROVED, ACTIVE]
  side: [BLOCKED]
closure_requires_evidence: true
history_deletion_for_cleanup: false
""");
}

static void WriteWorkItem(DirectoryInfo fixtureRoot, string id, string status, string prerequisites)
{
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", $"{id}.yaml"), $"""
schema_version: 1
work_item_id: {id}
milestone_id: MVP
sprint_id: null
title: Fixture work item {id}
status: {status}
type: GOVERNANCE_TOOLING
authority_refs: [authority]
flow_refs: []
contract_refs: []
decision_refs: []
acceptance_criteria: [deterministic]
prerequisites: {prerequisites}
blockers: []
impact:
  architecture: true
  ui: false
  sdk: false
  financial: false
  compliance: false
  security: false
  devops: false
  support: false
activated_roles: [governance-engineer]
required_tests: []
required_evidence: [governance/evidence/missing.json]
context_policy: TASK
todos: []
defects: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");
}

static void WriteOrchestratorWorkItem(DirectoryInfo fixtureRoot)
{
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-ORCH.yaml"), """
schema_version: 1
work_item_id: TUN-ORCH
milestone_id: P0
sprint_id: null
title: Orchestrator fixture
status: IN_PROGRESS
type: GOVERNANCE_TOOLING
authority_refs: [authority]
flow_refs: []
contract_refs: []
decision_refs: []
acceptance_criteria: [deterministic]
prerequisites: []
blockers: []
impact: {architecture: false, ui: false, sdk: false, financial: false, compliance: false, security: false, devops: false, support: false}
activated_roles: [full-stack-engineer, governance-engineer, tester-qa-engineer, auditor]
required_tests: []
required_evidence: []
context_policy: TASK
todos: []
defects: []
created_at: 2026-09-30T06:00:00-04:00
updated_at: 2026-09-30T06:00:00-04:00
""");
}
static void WriteRoleMatrixWorkItem(DirectoryInfo fixtureRoot, string activatedRoles)
{
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-P0-027.yaml"), $"""
schema_version: 1
work_item_id: TUN-P0-027
milestone_id: P0
sprint_id: null
title: Role matrix fixture
status: VALIDATION
type: GOVERNANCE_TOOLING
authority_refs: [authority]
flow_refs: []
contract_refs: []
decision_refs: []
acceptance_criteria: [deterministic]
prerequisites: []
blockers: []
impact:
  architecture: false
  ui: false
  sdk: false
  financial: false
  compliance: false
  security: false
  devops: false
  support: false
activated_roles: {activatedRoles}
required_tests: []
required_evidence: [governance/evidence/role-matrix-ready.json]
context_policy: TASK
todos: []
defects: []
created_at: 2026-10-01T05:00:00-04:00
updated_at: 2026-10-01T05:00:00-04:00
""");
}

static void WriteStructuredReview(DirectoryInfo fixtureRoot, string role, string result, string blockingFindings)
{
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "reviews", $"TUN-P0-027-{role}.yaml"), $"""
schema_version: 1
review_id: REV-TUN-P0-027-{role}
work_item_id: TUN-P0-027
role: {role}
skill_id: {role}
skill_version: 1.0.0
skill_hash: fixture-hash
result: {result}
status: {result}
authority_refs: [authority]
context_artifacts: [governance/work-items/TUN-P0-027.yaml]
findings: []
required_actions: []
blocking_findings: {blockingFindings}
evidence_refs: [governance/evidence/role-matrix-ready.json]
context_expansion_requested: false
reviewed_at: 2026-10-01T05:00:00-04:00
""");
}
static void DeleteFixture(DirectoryInfo fixtureRoot)
{
    try
    {
        fixtureRoot.Delete(true);
    }
    catch (UnauthorizedAccessException)
    {
        foreach (var entry in fixtureRoot.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            entry.Attributes = FileAttributes.Normal;
        }

        fixtureRoot.Attributes = FileAttributes.Normal;
        fixtureRoot.Delete(true);
    }
}
static void InitializeGit(DirectoryInfo repository)
{
    File.WriteAllText(Path.Combine(repository.FullName, ".gitignore"), "artifacts/" + Environment.NewLine);
    RunGit(repository, "init", "--quiet");
    RunGit(repository, "config", "user.email", "fixture@tunner.local");
    RunGit(repository, "config", "user.name", "Tunner fixture");
    RunGit(repository, "add", ".");
    RunGit(repository, "commit", "--quiet", "-m", "fixture baseline");
}

static void RunGit(DirectoryInfo repository, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("git")
    {
        WorkingDirectory = repository.FullName,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git for the fixture.");
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Fixture git command failed: {error}");
    }
}
static string HashFile(string path)
    => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
{
    Directory.CreateDirectory(destination.FullName);
    foreach (var file in source.EnumerateFiles())
    {
        file.CopyTo(Path.Combine(destination.FullName, file.Name), true);
    }

    foreach (var directory in source.EnumerateDirectories())
    {
        CopyDirectory(directory, new DirectoryInfo(Path.Combine(destination.FullName, directory.Name)));
    }
}