using Tunner.Governance;

var failures = new List<string>();
var repositoryRoot = FindRepositoryRoot();
var fixtureRoot = Directory.CreateTempSubdirectory("tunner-governance-fixture-");

try
{
    CreateFixture(repositoryRoot, fixtureRoot);

    var validation = GovernanceApplication.Validate(fixtureRoot);
    Expect(validation.ExitCode == 0, "A complete fixture must validate.", failures);

    var status = GovernanceApplication.Status(fixtureRoot);
    Expect(status.ExitCode == 0, "Status must be available for a valid fixture.", failures);

    var next = GovernanceApplication.Next(fixtureRoot);
    Expect(next.ExitCode == 0, "Next must be available for a valid fixture.", failures);

    var contextOutput = new DirectoryInfo(Path.Combine(fixtureRoot.FullName, "docs", "context", "current"));
    var contextBuild = ContextApplication.Build(fixtureRoot, "TUN-001", contextOutput);
    Expect(contextBuild.ExitCode == 0, "Context build must generate a bounded fixture pack.", failures);
    var contextCurrent = ContextApplication.Verify(fixtureRoot, contextOutput);
    Expect(contextCurrent.ExitCode == 0, "A newly generated context pack must verify as current.", failures);
    File.AppendAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-001.yaml"), "\n# Fixture source changed");
    var contextStale = ContextApplication.Verify(fixtureRoot, contextOutput);
    Expect(contextStale.ExitCode == 1, "Context verify must report a changed included source as stale.", failures);

    var gate = GovernanceApplication.CheckGate(fixtureRoot, "TUN-001");
    Expect(gate.ExitCode == 1, "Gate check must reject missing evidence and role-review evidence.", failures);

    var transition = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-001", "DONE");
    Expect(transition.ExitCode == 1, "Lifecycle check must reject DRAFT directly to DONE.", failures);

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
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "policies"));

    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "milestones", "MVP.yaml"), """
schema_version: 1
milestone_id: MVP
title: Fixture milestone
status: ACTIVE
goal: Validate governance behavior
scope: [governance]
authority_refs: [authority]
prerequisites: []
work_items: [TUN-001]
required_gates: []
required_roles: []
evidence_refs: []
known_risks: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-001.yaml"), """
schema_version: 1
work_item_id: TUN-001
milestone_id: MVP
sprint_id: null
title: Fixture work item
status: DRAFT
type: GOVERNANCE_TOOLING
authority_refs: [authority]
flow_refs: []
contract_refs: []
decision_refs: []
acceptance_criteria: [deterministic]
prerequisites: []
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