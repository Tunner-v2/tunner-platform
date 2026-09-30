using System.Security.Cryptography;
using Tunner.Governance;

var failures = new List<string>();
var repositoryRoot = FindRepositoryRoot();
var fixtureRoot = Directory.CreateTempSubdirectory("tunner-governance-fixture-");

try
{
    CreateFixture(repositoryRoot, fixtureRoot);

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

    var gate = GovernanceApplication.CheckGate(fixtureRoot, "TUN-TRANSITION");
    Expect(gate.ExitCode == 1, "Gate check must reject missing evidence and role-review evidence.", failures);

    var transition = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-TRANSITION", "DONE");
    Expect(transition.ExitCode == 1, "Lifecycle check must reject DRAFT directly to DONE.", failures);

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
    fixtureRoot.Delete(true);
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
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "evidence"));
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