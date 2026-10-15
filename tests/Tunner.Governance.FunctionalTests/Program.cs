using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tunner.Governance;

var failures = new List<string>();
var repositoryRoot = FindRepositoryRoot();
var fixtureRoot = Directory.CreateTempSubdirectory("tunner-governance-fixture-");

try
{
    RunRevisionChecks(repositoryRoot, failures);
    CreateFixture(repositoryRoot, fixtureRoot);
    InitializeGit(fixtureRoot);

    var explicitRoot = RepositoryRootResolver.Resolve(fixtureRoot);
    Expect(explicitRoot.FullName == fixtureRoot.FullName, "Explicit repository-root resolution must return the marked fixture root.", failures);
    var nestedRoot = Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "src", "nested"));
    var originalCurrentDirectory = Directory.GetCurrentDirectory();
    var originalEnvironmentRoot = Environment.GetEnvironmentVariable("TUNNER_REPO_ROOT");
    try
    {
        Directory.SetCurrentDirectory(nestedRoot.FullName);
        var nestedResolved = RepositoryRootResolver.Resolve().FullName;
        var gitRoot = new DirectoryInfo(RunGitOutput(fixtureRoot, "rev-parse", "--show-toplevel")).FullName;
        Expect(nestedResolved == gitRoot, "Nested-directory resolution must return the marked Git fixture root, including Windows short-path aliases.", failures);
        Environment.SetEnvironmentVariable("TUNNER_REPO_ROOT", fixtureRoot.FullName);
        Expect(RepositoryRootResolver.Resolve().FullName == fixtureRoot.FullName, "Environment repository-root resolution must return the marked fixture root.", failures);
    }
    finally
    {
        Environment.SetEnvironmentVariable("TUNNER_REPO_ROOT", originalEnvironmentRoot);
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
    Expect(RepositoryRootResolver.ToLogicalPath(fixtureRoot, fixtureRoot.FullName) == ".", "Repository root itself must serialize as a logical path.", failures);
    Expect(RepositoryRootResolver.ToLogicalPath(fixtureRoot, Path.Combine(fixtureRoot.FullName, "governance", "work-items")) == "governance/work-items", "Nested paths must serialize with canonical separators.", failures);
    var outsidePathRejected = false;
    try { RepositoryRootResolver.ToLogicalPath(fixtureRoot, fixtureRoot.Parent!.FullName); } catch (InvalidOperationException) { outsidePathRejected = true; }
    Expect(outsidePathRejected, "Logical path serialization must reject paths outside the repository root.", failures);

    var authority = AuthorityApplication.Verify(fixtureRoot);
    Expect(authority.ExitCode == 0, "A complete authority fixture must verify.", failures);
    var registeredPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "mirror.txt");
    var pristineBytes = File.ReadAllBytes(registeredPath);
    var mutatedBytes = pristineBytes.ToArray(); mutatedBytes[0] ^= 1;
    File.WriteAllBytes(registeredPath, mutatedBytes);
    Expect(AuthorityApplication.Verify(fixtureRoot).ExitCode != 0, "A single-byte registered artifact mutation must fail.", failures);
    File.WriteAllBytes(registeredPath, pristineBytes);
    var activePath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "amendments", "AMD-TEST", "AMD-TEST_fixture.md");
    var activeBytes = File.ReadAllBytes(activePath);
    var activeMutation = activeBytes.ToArray(); activeMutation[0] ^= 1;
    File.WriteAllBytes(activePath, activeMutation);
    Expect(AuthorityApplication.Verify(fixtureRoot).ExitCode != 0, "Active authority single-byte mutation must fail.", failures);
    File.WriteAllBytes(activePath, activeBytes);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "unrelated.txt"), "unrelated changed work");
    Expect(AuthorityApplication.Verify(fixtureRoot).ExitCode == 0, "Unrelated mutation must cause no false authority drift.", failures);

    var validation = GovernanceApplication.Validate(fixtureRoot);
    Expect(validation.ExitCode == 0, "A complete fixture must validate.", failures);
    WriteWorkItem(fixtureRoot, "TUN-REOPEN", "REOPENED", "[]");
    var reopenPath = Path.Combine(fixtureRoot.FullName, "governance", "reopens", "REOPEN-TUN-REOPEN-001.yaml");
    Directory.CreateDirectory(Path.GetDirectoryName(reopenPath)!);
    File.WriteAllText(reopenPath, """
schema_version: 1
reopen_id: REOPEN-TUN-REOPEN-001
work_item_id: TUN-REOPEN
previous_release: null
reason: Current acceptance lifecycle correction
detected_environment: FIXTURE
classification: GOVERNANCE_DEFECT
severity: SEV-2
new_acceptance_criteria: [Preserve prior evidence]
affected_versions: []
evidence: []
created_at: 2026-10-05T00:00:00Z
""");
    Expect(GovernanceApplication.CheckTransition(fixtureRoot, "TUN-REOPEN", "IN_PROGRESS").ExitCode == 0, "Approved active acceptance REOPENED work must resume with preserved causation.", failures);
    File.Delete(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-REOPEN.yaml"));
    File.Delete(reopenPath);
    WriteWorkItem(fixtureRoot, "TUN-CONTRACT", "CONTRACT_REQUIRED", "[]");
    Expect(GovernanceApplication.Validate(fixtureRoot).ExitCode == 1, "A missing-authority blocker must be rejected without a recorded Authority Resolution Pass.", failures);
    File.Delete(Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-CONTRACT.yaml"));

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
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "source-evidence.json"), File.ReadAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "source-evidence.json")).Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    var sourcesLineEndingNormalized = SourceRegistryApplication.Check(fixtureRoot, "TUN-001", "2026-09-30", 14);
    Expect(sourcesLineEndingNormalized.ExitCode == 0, "Source evidence must retain its registered hash after newline normalization.", failures);
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
    Expect(contextPayload.Included.Any(item => item.Path == "docs/authority/amendments/AMD-TEST/AMD-TEST_fixture.md" && item.Reason == "active authority amendment"), "Context build must include each active amendment declared by the verified authority index.", failures);
    var contextCurrent = ContextApplication.Verify(fixtureRoot, contextOutput);
    Expect(contextCurrent.ExitCode == 0, "A newly generated context pack must verify as current.", failures);
    var unselectedGovernancePath = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-TRANSITION.yaml");
    File.AppendAllText(unselectedGovernancePath, "\n# unselected authoritative state changed");
    Expect(ContextApplication.Verify(fixtureRoot, contextOutput).ExitCode != 0, "Unselected authoritative governance changes must invalidate derived project memory.", failures);
    Expect(ContextApplication.Build(fixtureRoot, "TUN-001", contextOutput).ExitCode == 0 && ContextApplication.Verify(fixtureRoot, contextOutput).ExitCode == 0, "Context build must regenerate stale durable state deterministically.", failures);
    var durableState = Path.Combine(fixtureRoot.FullName, "docs", "project", "CURRENT_STATE.md");
    File.AppendAllText(durableState, "\nContradictory fabricated state");
    Expect(ContextApplication.Verify(fixtureRoot, contextOutput).ExitCode != 0, "Contradictory derived memory must be detected.", failures);
    ContextApplication.Build(fixtureRoot, "TUN-001", contextOutput);
    Expect(!File.ReadAllText(durableState).Contains("Contradictory fabricated state", StringComparison.Ordinal), "Regeneration must replace stale derived conclusions using governance.", failures);
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
    Expect(adaptiveCore.ExitCode == 0 && ((ContextBuildPayload)adaptiveCore.Payload).Included.All(item => item.Reason is "repository entry contract" or "current authority summary" or "selected work item" or "explicit authority reference" or "activated role skill" or "historical milestone state"), "CORE context must retain only mandatory context for the fixture.", failures);
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

    foreach (var path in Directory.EnumerateFiles(fixtureRoot.FullName, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(fixtureRoot.FullName, x).Replace('\\', '/')).Where(x => !x.StartsWith(".git/", StringComparison.Ordinal) && !x.StartsWith("artifacts/", StringComparison.Ordinal)))
        RunGit(fixtureRoot, "add", "--", path);
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
    var normalValidationTransition = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-001", "PRODUCT_ACCEPTANCE");
    Expect(normalValidationTransition.ExitCode == 0, "Lifecycle check must retain the declared Product Acceptance path when no technical-only exception is recorded.", failures);
    var normalValidationCompletion = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-001", "DONE");
    Expect(normalValidationCompletion.ExitCode == 1, "Lifecycle check must not permit direct completion without the explicit technical-only exception.", failures);
    var technicalOnlyCompletion = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-TECHNICAL", "DONE");
    Expect(technicalOnlyCompletion.ExitCode == 0 && ((TransitionPayload)technicalOnlyCompletion.Payload).Reason.Contains("explicitly not required", StringComparison.Ordinal), "Lifecycle check must permit direct technical-only completion only when Product Acceptance is explicitly not required.", failures);

    WriteWorkItem(fixtureRoot, "TUN-P1-COMMIT", "VALIDATION", "[]", false);
    var p1CommitPath = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-P1-COMMIT.yaml");
    File.WriteAllText(p1CommitPath, File.ReadAllText(p1CommitPath).Replace("milestone_id: MVP", "milestone_id: P1", StringComparison.Ordinal).Replace("git_state: {repository_changed: false, validated_commit_sha: null, tested_commit_sha: null, work_item_commit_shas: [], closure_commit_sha: null, remaining_uncommitted_paths: [], remaining_uncommitted_classification: NONE}", "", StringComparison.Ordinal));
    var p1CandidateSha = PrepareCommittedFixture(fixtureRoot, "TUN-P1-COMMIT");
    var p1CompletionWithoutState = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-P1-COMMIT", "DONE");
    Expect(p1CompletionWithoutState.ExitCode == 1 && ((TransitionPayload)p1CompletionWithoutState.Payload).Reason.Contains("committed-state readiness", StringComparison.Ordinal), "P1 completion must reject repository-changing work that lacks committed-state evidence.", failures);
    File.AppendAllText(p1CommitPath, $"{Environment.NewLine}git_state:{Environment.NewLine}  repository_changed: true{Environment.NewLine}  validated_commit_sha: {p1CandidateSha}{Environment.NewLine}  tested_commit_sha: {p1CandidateSha}{Environment.NewLine}  work_item_commit_shas: [{p1CandidateSha}]{Environment.NewLine}  closure_commit_sha: null{Environment.NewLine}  remaining_uncommitted_paths: []{Environment.NewLine}  remaining_uncommitted_classification: NONE{Environment.NewLine}");
    var p1CompletionWithState = GovernanceApplication.CheckTransition(fixtureRoot, "TUN-P1-COMMIT", "DONE");
    if (p1CompletionWithState.ExitCode != 0) Console.Error.WriteLine(GovernanceApplication.Serialize(p1CompletionWithState));
    Expect(p1CompletionWithState.ExitCode == 0, "P1 completion must accept a valid committed validation candidate while allowing a later metadata closure commit.", failures);
    var p1ProofPath = Path.Combine(fixtureRoot.FullName, "governance", "evidence", "TUN-P1-COMMIT-proof.json");
    var savedProof = File.ReadAllText(p1ProofPath);
    File.Delete(p1ProofPath);
    Expect(GovernanceApplication.CheckTransition(fixtureRoot, "TUN-P1-COMMIT", "DONE").ExitCode != 0, "A resolvable SHA alone must not replace scope-bound owned-file evidence.", failures);
    File.WriteAllText(p1ProofPath, savedProof);
    var ownedPath = Path.Combine(fixtureRoot.FullName, "docs", "TUN-P1-COMMIT-behavior.txt");
    File.AppendAllText(ownedPath, "changed after validation");
    Expect(GovernanceApplication.CheckTransition(fixtureRoot, "TUN-P1-COMMIT", "DONE").ExitCode != 0, "Ordinary work must reject owned behavior changed after its validated SHA.", failures);
    File.WriteAllText(ownedPath, "fixture behavior");
    var futureText = File.ReadAllText(p1CommitPath);
    File.WriteAllText(p1CommitPath, futureText.Replace("milestone_id: P1", "milestone_id: P2", StringComparison.Ordinal));
    Expect(GovernanceApplication.CheckTransition(fixtureRoot, "TUN-P1-COMMIT", "DONE").ExitCode == 0, "Valid committed evidence applies consistently to a later milestone.", failures);
    File.WriteAllText(p1CommitPath, futureText);

    WriteWorkItem(fixtureRoot, "TUN-P1-UPSTREAM", "DONE", "[]");
    WriteWorkItem(fixtureRoot, "TUN-P1-DOWNSTREAM", "READY", "[TUN-P1-UPSTREAM]");
    var p1UpstreamPath = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-P1-UPSTREAM.yaml");
    var p1DownstreamPath = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-P1-DOWNSTREAM.yaml");
    File.WriteAllText(p1UpstreamPath, File.ReadAllText(p1UpstreamPath).Replace("milestone_id: MVP", "milestone_id: P1", StringComparison.Ordinal).Replace("git_state: {repository_changed: false, validated_commit_sha: null, tested_commit_sha: null, work_item_commit_shas: [], closure_commit_sha: null, remaining_uncommitted_paths: [], remaining_uncommitted_classification: NONE}", "", StringComparison.Ordinal));
    File.WriteAllText(p1DownstreamPath, File.ReadAllText(p1DownstreamPath).Replace("milestone_id: MVP", "milestone_id: P1", StringComparison.Ordinal));
    var p1DependencyCandidateSha = PrepareCommittedFixture(fixtureRoot, "TUN-P1-UPSTREAM");
    Expect(((GatePayload)GovernanceApplication.CheckGate(fixtureRoot, "TUN-P1-UPSTREAM").Payload).Failures.Any(x => x.Contains("git_state is absent", StringComparison.Ordinal)), "Own-item gates must reject the same missing committed state as dependency gates.", failures);
    var p1DownstreamBeforeEvidence = (NextPayload)GovernanceApplication.Next(fixtureRoot).Payload;
    Expect(!p1DownstreamBeforeEvidence.Items.Single(item => item.WorkItemId == "TUN-P1-DOWNSTREAM").Actionable, "A P1 downstream item must remain ineligible while its DONE prerequisite lacks committed-state evidence.", failures);
    File.AppendAllText(p1UpstreamPath, $"{Environment.NewLine}git_state:{Environment.NewLine}  repository_changed: true{Environment.NewLine}  validated_commit_sha: {p1DependencyCandidateSha}{Environment.NewLine}  tested_commit_sha: {p1DependencyCandidateSha}{Environment.NewLine}  work_item_commit_shas: [{p1DependencyCandidateSha}]{Environment.NewLine}  closure_commit_sha: null{Environment.NewLine}  remaining_uncommitted_paths: []{Environment.NewLine}  remaining_uncommitted_classification: NONE{Environment.NewLine}");
    var p1DownstreamAfterEvidence = (NextPayload)GovernanceApplication.Next(fixtureRoot).Payload;
    Expect(p1DownstreamAfterEvidence.Items.Single(item => item.WorkItemId == "TUN-P1-DOWNSTREAM").Actionable, "A P1 downstream item may become eligible when its DONE prerequisite has valid committed-state evidence.", failures);

    var milestoneClose = GovernanceApplication.CheckMilestoneClose(fixtureRoot, "MVP");
    RunGit(fixtureRoot, "add", ".");
    RunGit(fixtureRoot, "commit", "-m", "Anchor synthetic milestone review fixture");
    var snapshot = JsonSerializer.SerializeToNode(GovernanceApplication.MilestoneReviewSnapshot(fixtureRoot, "MVP").Payload)!;
    var milestoneReviewPath = Path.Combine(fixtureRoot.FullName, "governance", "evidence", "MVP-MILESTONE-ROLE-REVIEWS.json");
    File.WriteAllText(milestoneReviewPath, JsonSerializer.Serialize(new {
        scope_type = "MILESTONE", scope_id = "MVP", status = "PASS",
        reviewed_commit_sha = snapshot["ReviewedCommitSha"]!.GetValue<string>(),
        reviewed_scope_sha256 = snapshot["ReviewedScopeSha256"]!.GetValue<string>(),
        reviews = new[] { new { role = "auditor", conclusion = "PASS" } }
    }));
    Expect(!((MilestoneClosePayload)GovernanceApplication.CheckMilestoneClose(fixtureRoot, "MVP").Payload).Failures.Any(x => x.Contains("reviews lack current committed scope binding", StringComparison.Ordinal)), "Fresh milestone review binding is accepted independently of other unfinished work.", failures);
    var bindingScope = Path.Combine(fixtureRoot.FullName, "governance", "work-items", "TUN-001.yaml");
    var savedBindingScope = File.ReadAllText(bindingScope);
    File.AppendAllText(bindingScope, "\n# changed review scope\n");
    Expect(((MilestoneClosePayload)GovernanceApplication.CheckMilestoneClose(fixtureRoot, "MVP").Payload).Failures.Any(x => x.Contains("reviews lack current committed scope binding", StringComparison.Ordinal)), "Changed milestone scope must invalidate old PASS reviews.", failures);
    File.WriteAllText(bindingScope, savedBindingScope);
    Expect(milestoneClose.ExitCode == 1 && ((MilestoneClosePayload)milestoneClose.Payload).Failures.Any(item => item.Contains("work item is not DONE", StringComparison.Ordinal)) && ((MilestoneClosePayload)milestoneClose.Payload).Failures.Any(item => item.Contains("milestone role-review evidence missing", StringComparison.Ordinal)), "Milestone close-check must refuse incomplete work and missing milestone reviews.", failures);

    var milestoneTransition = GovernanceApplication.CheckMilestoneTransition(fixtureRoot, "MVP", "VALIDATION");
    if (milestoneTransition.ExitCode != 0) Console.Error.WriteLine(GovernanceApplication.Serialize(milestoneTransition));
    Expect(milestoneTransition.ExitCode == 0 && ((MilestoneTransitionPayload)milestoneTransition.Payload).Outcome == "ALLOWED", "Milestone lifecycle check must allow the next declared normal state.", failures);

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
    if (roleReady.ExitCode != 0) Console.Error.WriteLine(GovernanceApplication.Serialize(roleReady));
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
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, ".tunner-root"), "tunner-repository-root-v1" + Environment.NewLine);    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "milestones"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "work-items"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "dependencies"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "policies"));
    File.Copy(Path.Combine(repositoryRoot.FullName, "governance", "policies", "role-activation-policy.yaml"), Path.Combine(fixtureRoot.FullName, "governance", "policies", "role-activation-policy.yaml"), true);
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "evidence"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "reviews"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "governance", "rd"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "docs", "authority"));
    var fixtureAmendmentDirectory = Path.Combine(fixtureRoot.FullName, "docs", "authority", "amendments", "AMD-TEST");
    Directory.CreateDirectory(fixtureAmendmentDirectory);
    File.WriteAllText(Path.Combine(fixtureAmendmentDirectory, "AMD-TEST_fixture.md"), "fixture active amendment" + Environment.NewLine);
    var currentAuthorityPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "current-authority.json");
    var authorityMirrorPath = Path.Combine(fixtureRoot.FullName, "docs", "authority", "mirror.txt");
    File.WriteAllText(currentAuthorityPath, "{\"effective_authority\":\"fixture\",\"active_amendments\":[{\"id\":\"AMD-TEST\",\"path\":\"docs/authority/amendments/AMD-TEST\"}]}" + Environment.NewLine);
    File.WriteAllText(authorityMirrorPath, "fixture authority bytes" + Environment.NewLine);
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "evidence", "BOOT-P0-001-integrity.json"), $$"""
{
  "checks": [
    { "local_path": "docs/authority/current-authority.json", "expected_sha256": "{{HashFile(currentAuthorityPath)}}" },
    { "local_path": "docs/authority/mirror.txt", "expected_sha256": "{{HashFile(authorityMirrorPath)}}" },
    { "local_path": "docs/authority/amendments/AMD-TEST/AMD-TEST_fixture.md", "expected_sha256": "{{HashFile(Path.Combine(fixtureAmendmentDirectory, "AMD-TEST_fixture.md"))}}" }
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
work_items: [TUN-001, TUN-TRANSITION, TUN-TECHNICAL, TUN-LOCAL, TUN-MERGED, TUN-CHAIN]
required_gates: []
required_roles: [auditor]
evidence_refs: []
known_risks: []
created_at: 2026-09-30T00:00:00-04:00
updated_at: 2026-09-30T00:00:00-04:00
""");

    WriteWorkItem(fixtureRoot, "TUN-001", "VALIDATION", "[]");
    WriteWorkItem(fixtureRoot, "TUN-TRANSITION", "DRAFT", "[]");
    WriteWorkItem(fixtureRoot, "TUN-TECHNICAL", "VALIDATION", "[]", false);
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
  "entries": [{ "scope_type": "WORK_ITEM", "scope_id": "TUN-001", "evidence_path": "governance/evidence/source-evidence.json", "evidence_sha256": "{{HashSourceEvidence(sourceEvidencePath)}}", "reviewed_on": "2026-09-30", "source_authority": "PRIMARY" }]
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
  normal: [PROPOSED, APPROVED, ACTIVE, VALIDATION, PRODUCT_ACCEPTANCE, RELEASED, CLOSED]
  side: [BLOCKED]
closure_requires_evidence: true
history_deletion_for_cleanup: false
""");
}

static string PrepareCommittedFixture(DirectoryInfo fixture, string id)
{
    var behavior = "docs/" + id + "-behavior.txt";
    var proof = "governance/evidence/" + id + "-proof.json";
    var work = "governance/work-items/" + id + ".yaml";
    File.WriteAllText(Path.Combine(fixture.FullName, behavior), "fixture behavior");
    File.WriteAllText(Path.Combine(fixture.FullName, work), File.ReadAllText(Path.Combine(fixture.FullName, work))
        .Replace("required_evidence: [governance/evidence/missing.json]", "required_evidence: [" + proof + "]", StringComparison.Ordinal));
    File.WriteAllText(Path.Combine(fixture.FullName, proof), JsonSerializer.Serialize(new { scope_id = id, validation_kind = "COMMITTED", owned_behavior_paths = new[] { behavior } }));
    RunGit(fixture, "add", "--", work, behavior, proof);
    RunGit(fixture, "commit", "--quiet", "-m", "committed fixture ownership");
    var sha = RunGitOutput(fixture, "rev-parse", "HEAD");
    File.WriteAllText(Path.Combine(fixture.FullName, proof), JsonSerializer.Serialize(new { scope_id = id, validation_kind = "COMMITTED", validated_commit_sha = sha, tested_commit_sha = sha, result = "PASS", owned_behavior_paths = new[] { behavior } }));
    return sha;
}

static void WriteWorkItem(DirectoryInfo fixtureRoot, string id, string status, string prerequisites, bool? productAcceptanceRequired = null)
{
    const string noOpGitState = "git_state: {repository_changed: false, validated_commit_sha: null, tested_commit_sha: null, work_item_commit_shas: [], closure_commit_sha: null, remaining_uncommitted_paths: [], remaining_uncommitted_classification: NONE}";
    var productAcceptanceRequirement = productAcceptanceRequired.HasValue ? $"product_acceptance_required: {productAcceptanceRequired.Value.ToString().ToLowerInvariant()}{Environment.NewLine}" : string.Empty;
    File.WriteAllText(Path.Combine(fixtureRoot.FullName, "governance", "work-items", $"{id}.yaml"), $"""
schema_version: 1
work_item_id: {id}
milestone_id: MVP
sprint_id: null
title: Fixture work item {id}
status: {status}
{noOpGitState}
{productAcceptanceRequirement}type: GOVERNANCE_TOOLING
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
static void RunRevisionChecks(DirectoryInfo repositoryRoot, ICollection<string> failures)
{
    var fixture = Directory.CreateTempSubdirectory("tunner-governance-revision-fixture-");
    var assertions = 0;
    string[] declaredBlocked = ["TUN-RV-UNRELATED"];
    string[] proofRefs = ["governance/evidence/revision-proof.json"];
    string[] requiredAuditor = ["auditor"];
    string[] dependencyScope = ["TUN-001"];
    string[] priorDependency = ["P0-004-RV001"];
    void Check(bool condition, string message) { assertions++; Expect(condition, "RV: " + message, failures); }
    try
    {
        CreateFixture(repositoryRoot, fixture);
        File.Copy(Path.Combine(repositoryRoot.FullName, "governance", "policies", "lifecycle-policy.yaml"), Path.Combine(fixture.FullName, "governance", "policies", "lifecycle-policy.yaml"), true);
        var milestonePath = Path.Combine(fixture.FullName, "governance", "milestones", "P0.yaml");
        var milestone = File.ReadAllText(Path.Combine(fixture.FullName, "governance", "milestones", "MVP.yaml"))
            .Replace("milestone_id: MVP", "milestone_id: P0", StringComparison.Ordinal).Replace("status: ACTIVE", "status: CLOSED", StringComparison.Ordinal)
            .Replace("authority_refs: [authority]", "authority_refs: [docs/authority/mirror.txt]", StringComparison.Ordinal);
        File.WriteAllText(milestonePath, milestone);
        foreach (var id in new[] { "TUN-P0-004", "TUN-P0-999" })
        {
            WriteWorkItem(fixture, id, "DONE", "[]");
            var path = Path.Combine(fixture.FullName, "governance", "work-items", id + ".yaml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("milestone_id: MVP", "milestone_id: P0", StringComparison.Ordinal).Replace("authority_refs: [authority]", "authority_refs: [docs/authority/mirror.txt]", StringComparison.Ordinal));
        }
        WriteWorkItem(fixture, "TUN-RV-UNRELATED", "READY", "[]");
        Directory.CreateDirectory(Path.Combine(fixture.FullName, "docs", "project"));
        foreach (var name in new[] { "CURRENT_STATE", "PROJECT_CONTEXT", "HANDOFF" }) File.WriteAllText(Path.Combine(fixture.FullName, "docs", "project", name + ".md"), name);
        File.WriteAllText(Path.Combine(fixture.FullName, "AGENTS.md"), "fixture recovery entry");
        InitializeGit(fixture);
        var decisionPath = Path.Combine(fixture.FullName, "docs", "authority", "ADR-9999_Approved_Test.md");
        File.WriteAllText(decisionPath, "# ADR-9999\n\n**Status:** ACCEPTED\n");
        var integrityPath = Path.Combine(fixture.FullName, "governance", "evidence", "BOOT-P0-001-integrity.json");
        var integrity = JsonNode.Parse(File.ReadAllText(integrityPath))!;
        integrity["checks"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { local_path = "docs/authority/ADR-9999_Approved_Test.md", expected_sha256 = HashFile(decisionPath) }));
        File.WriteAllText(integrityPath, integrity.ToJsonString());
        var originalHash = HashFile(milestonePath);
        var workPath = Path.Combine(fixture.FullName, "governance", "work-items", "TUN-P0-004.yaml");
        var workHash = HashFile(workPath);
        RevisionCreateRequest Request(string scope) => new(scope, "CORRECTIVE", "Approved corrective work", "Later correction after historical closure", "NON_BLOCKING", [], [], [], ["deterministic correction"]);
        var created = RevisionApplication.Create(fixture, Request("P0") with { DecisionRefs = ["ADR-9999"] });
        Check(created.ExitCode == 0 && ((RevisionOperationPayload)created.Payload).RevisionId == "P0-RV001", "closed milestone allocates RV001");
        if (created.ExitCode != 0) { failures.Add(GovernanceApplication.Serialize(created)); return; }
        var mileRevision = Path.Combine(fixture.FullName, "governance", "revisions", "P0-RV001.yaml");
        Check(GovernanceApplication.Validate(fixture).ExitCode == 0, "existing accepted ADR resolves without a duplicate Product decision");
        Check(HashFile(milestonePath) == originalHash, "milestone historical closure/evidence remains byte-identical");
        Check(RevisionApplication.Create(fixture, Request("MVP")).ExitCode != 0, "active milestone correction retains REOPENED capability");
        Check(RevisionApplication.Create(fixture, Request("P0") with { Type = "SPECULATIVE" }).ExitCode != 0, "unapproved revision types fail schema");
        Check(RevisionApplication.Create(fixture, Request("P0") with { AuthorityRefs = ["docs/authority/missing.md"] }).ExitCode != 0, "missing/unverified authority cannot be invented");
        Check(RevisionApplication.Create(fixture, Request("P0") with { Impact = "SCOPED_BLOCKING" }).ExitCode != 0, "blocking impact requires bounded declared scope");
        Check(RevisionApplication.Create(fixture, Request("P0") with { BlockedScope = ["TUN-RV-UNRELATED"] }).ExitCode != 0, "NON_BLOCKING cannot declare blocked work");
        Check(RevisionApplication.Create(fixture, Request("P0") with { DecisionRefs = ["DEC-UNAPPROVED"] }).ExitCode != 0, "unapproved authority decisions do not activate Revision work");
        var workCreated = RevisionApplication.Create(fixture, Request("P0-004") with { ParentMilestoneRevisionId = "P0-RV001" });
        Check(workCreated.ExitCode == 0 && ((RevisionOperationPayload)workCreated.Payload).RevisionId == "P0-004-RV001", "canonical work-item alias resolves original TUN identifier");
        var workRevision = Path.Combine(fixture.FullName, "governance", "revisions", "P0-004-RV001.yaml");
        Check(HashFile(workPath) == workHash, "historical work-item completion/evidence remains byte-identical");
        string[] childRevisionRefs = ["P0-004-RV001"];
        EditRevision(mileRevision, "work_items", childRevisionRefs);
        Check(((RevisionOperationPayload)RevisionApplication.CloseCheck(fixture, "P0-RV001").Payload).Findings.Any(x => x.Contains("child Revision is not CLOSED", StringComparison.Ordinal)), "milestone Revision closure requires its linked child Revisions CLOSED");
        EditRevision(mileRevision, "work_items", Array.Empty<string>());
        var child = RevisionApplication.CreateChild(fixture, "P0-004-RV001", "Scoped child");
        Check(child.ExitCode == 0 && ((RevisionOperationPayload)child.Payload).RevisionId == "P0-004-RV001-001", "canonical positive child ID is allocated under existing work-item storage");
        Check(RevisionApplication.CreateChild(fixture, "P0-004-RV001", "Second child").ExitCode == 0, "child allocation increments without duplicates");
        Check(RevisionApplication.CheckTransition(fixture, "P0-004-RV001", "ACTIVE").ExitCode != 0, "proposal cannot skip approval");
        Check(RevisionApplication.CheckTransition(fixture, "P0-004-RV001", "APPROVED").ExitCode == 0, "next declared lifecycle transition is available");
        EditRevision(mileRevision, "status", "ACTIVE");
        var next = (NextPayload)GovernanceApplication.Next(fixture).Payload;
        Check(next.Items.Any(x => x.WorkItemId == "P0-RV001" && x.Actionable), "governance next exposes eligible active Revision");
        Check(next.Items.Single(x => x.WorkItemId == "TUN-RV-UNRELATED").Actionable, "NON_BLOCKING Revision preserves unrelated eligibility");
        foreach (var impact in new[] { "SCOPED_BLOCKING", "RELEASE_BLOCKING", "SECURITY_BLOCKING" })
        {
            EditRevision(mileRevision, "impact_classification", impact);
            EditRevision(mileRevision, "blocked_scope", declaredBlocked);
            var blockedNext = (NextPayload)GovernanceApplication.Next(fixture).Payload;
            Check(!blockedNext.Items.Single(x => x.WorkItemId == "TUN-RV-UNRELATED").Actionable, impact + " withholds only declared affected scope");
            Check(blockedNext.Items.Single(x => x.WorkItemId == "TUN-LOCAL").Actionable, impact + " does not suppress unrelated work");
        }
        EditRevision(mileRevision, "blocked_scope", dependencyScope);
        var dependentNext = (NextPayload)GovernanceApplication.Next(fixture).Payload;
        Check(!dependentNext.Items.Single(x => x.WorkItemId == "TUN-LOCAL").Actionable && !dependentNext.Items.Single(x => x.WorkItemId == "TUN-CHAIN").Actionable, "declared dependency records propagate bounded Revision impact transitively");
        Check(dependentNext.Items.Single(x => x.WorkItemId == "TUN-RV-UNRELATED").Actionable, "dependency impact preserves unrelated scope");
        EditRevision(mileRevision, "impact_classification", "NON_BLOCKING"); EditRevision(mileRevision, "blocked_scope", Array.Empty<string>());
        var second = RevisionApplication.Create(fixture, Request("TUN-P0-004"));
        Check(second.ExitCode == 0 && ((RevisionOperationPayload)second.Payload).RevisionId == "P0-004-RV002", "second Revision increments and preserves prior record");
        var secondPath = Path.Combine(fixture.FullName, "governance", "revisions", "P0-004-RV002.yaml");
        Check(File.ReadAllText(secondPath).Contains("P0-004-RV001", StringComparison.Ordinal), "previous Revision lineage is linked");
        Check(!((NextPayload)GovernanceApplication.Next(fixture).Payload).Items.Single(x => x.WorkItemId == "P0-004-RV002").Actionable, "proposal remains non-actionable before approval");
        EditRevision(secondPath, "status", "APPROVED");
        Check(((NextPayload)GovernanceApplication.Next(fixture).Payload).Items.Single(x => x.WorkItemId == "P0-004-RV002").Actionable, "previous lineage is not an undeclared prerequisite");
        EditRevision(secondPath, "prerequisites", priorDependency);
        Check(!((NextPayload)GovernanceApplication.Next(fixture).Payload).Items.Single(x => x.WorkItemId == "P0-004-RV002").Actionable, "explicit prior Revision prerequisite retains its scoped gate");
        EditRevision(secondPath, "prerequisites", Array.Empty<string>());
        var large = RevisionApplication.Create(fixture, Request("P0-999"));
        Check(large.ExitCode == 0, "independent scope starts its own sequence");
        var largePath = Path.Combine(fixture.FullName, "governance", "revisions", "P0-999-RV001.yaml");
        EditRevision(largePath, "revision_id", "P0-999-RV999");
        var importedPath = Path.Combine(fixture.FullName, "governance", "revisions", "P0-999-RV999.yaml");
        File.Move(largePath, importedPath);
        var beyond = RevisionApplication.Create(fixture, Request("P0-999"));
        Check(beyond.ExitCode == 0 && ((RevisionOperationPayload)beyond.Payload).RevisionId == "P0-999-RV1000", "sequence has no RV999 ceiling");
        var duplicate = Path.Combine(fixture.FullName, "governance", "revisions", "duplicate.yaml");
        File.Copy(secondPath, duplicate);
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "duplicate Revision IDs are rejected without crashing");
        File.Delete(duplicate);
        var good = File.ReadAllText(secondPath);
        EditRevision(secondPath, "revision_type", "UNKNOWN");
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "executable schema rejects bad enum");
        File.WriteAllText(secondPath, good);
        EditRevision(secondPath, "blocked_scope", "invalid scalar");
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "executable schema rejects wrong nested field shape");
        File.WriteAllText(secondPath, good);
        var context = new DirectoryInfo(Path.Combine(fixture.FullName, "artifacts", "revision-context"));
        var pack = AdaptiveContextApplication.Build(fixture, "P0-004-RV002", context, new AdaptiveContextRequest("CORE", null, "REPOSITORY_READ"));
        Check(pack.ExitCode == 0, "Revision context builds in CORE mode");
        if (pack.ExitCode == 0)
        {
            var manifest = JsonSerializer.Deserialize<ContextManifest>(File.ReadAllText(Path.Combine(context.FullName, "manifest.json")))!;
            Check(manifest.Revision?.OriginalScopeId == "TUN-P0-004", "fresh-agent manifest resolves original work-item identity");
            Check(manifest.Included.Any(x => x.Path.EndsWith("P0-004-RV001.yaml", StringComparison.Ordinal)), "bounded context includes immediate previous lineage");
            Check(manifest.Included.All(x => !x.Path.EndsWith("P0-999-RV999.yaml", StringComparison.Ordinal)), "unrelated Revision history is excluded");
            Check(manifest.HistoricalScopes!.Any(x => x.Id == "P0" && x.HistoricalStatus == "CLOSED") && manifest.HistoricalScopes!.Any(x => x.Id == "TUN-P0-004" && x.HistoricalStatus == "DONE"), "generated memory separates historical closure from active RV state");
            Check(File.ReadAllText(Path.Combine(context.FullName, "HANDOFF.md")).Contains("Exact next action", StringComparison.Ordinal), "fresh-agent recovery records exact Revision action");
            Check(AdaptiveContextApplication.Verify(fixture, context).ExitCode == 0, "fresh Revision context verifies");
            var extra = RevisionApplication.Create(fixture, Request("P0"));
            Check(extra.ExitCode == 0 && AdaptiveContextApplication.Verify(fixture, context).ExitCode != 0, "new lineage invalidates context even when old selected sources are unchanged");
        }
        Check(AdaptiveContextApplication.Build(fixture, "", context).ExitCode == 0, "canonical context build selects a current governed scope without --current");
        Check(RevisionApplication.CloseCheck(fixture, "P0-004-RV001").ExitCode != 0, "closure refuses incomplete children, validation, and committed evidence");
        var proofPath = Path.Combine(fixture.FullName, "governance", "evidence", "revision-proof.json");
        string[] ownedBehaviorPaths = ["docs/authority/current-authority.json"];
        File.WriteAllText(proofPath, JsonSerializer.Serialize(new { scope_id = "P0-999-RV999", result = "PENDING", owned_behavior_paths = ownedBehaviorPaths }));
        EditRevision(importedPath, "status", "VALIDATION");
        EditRevision(importedPath, "product_acceptance_required", false);
        EditRevision(importedPath, "evidence_refs", proofRefs);
        RunGit(fixture, "add", "--", "governance/revisions/P0-999-RV999.yaml", "governance/evidence/revision-proof.json");
        RunGit(fixture, "commit", "--quiet", "-m", "Revision validation candidate");
        var sha = RunGitOutput(fixture, "rev-parse", "HEAD");
        EditRevision(importedPath, "git_state", new { repository_changed = true, validated_commit_sha = sha, tested_commit_sha = sha, work_item_commit_shas = new[] { sha }, closure_commit_sha = (string?)null, remaining_uncommitted_paths = Array.Empty<string>(), remaining_uncommitted_classification = "PREEXISTING_UNRELATED" });
        File.WriteAllText(proofPath, JsonSerializer.Serialize(new { scope_id = "P0-999-RV999", validation_kind = "COMMITTED", validated_commit_sha = sha, tested_commit_sha = sha, result = "PASS", owned_behavior_paths = ownedBehaviorPaths }));
        Check(RevisionApplication.CloseCheck(fixture, "P0-999-RV999").ExitCode == 0, "closure checks exact committed Revision identity and new evidence with unrelated dirty fixture work preserved");
        var validProof = File.ReadAllText(proofPath);
        File.WriteAllText(proofPath, validProof.Replace("\"tested_commit_sha\":\"" + sha, "\"tested_commit_sha\":\"0000000000000000000000000000000000000000", StringComparison.Ordinal));
        Check(RevisionApplication.CloseCheck(fixture, "P0-999-RV999").ExitCode != 0, "evidence tested SHA mismatch is rejected");
        File.WriteAllText(proofPath, validProof);
        var behaviorPath = Path.Combine(fixture.FullName, "docs", "authority", "current-authority.json");
        var behaviorBytes = File.ReadAllBytes(behaviorPath);
        File.AppendAllText(behaviorPath, " ");
        Check(RevisionApplication.CloseCheck(fixture, "P0-999-RV999").ExitCode != 0, "behavior changes after validation invalidate committed readiness");
        File.WriteAllBytes(behaviorPath, behaviorBytes);
        EditRevision(importedPath, "git_state", new { repository_changed = false, validated_commit_sha = (string?)null, tested_commit_sha = (string?)null, work_item_commit_shas = Array.Empty<string>(), closure_commit_sha = (string?)null, remaining_uncommitted_paths = Array.Empty<string>(), remaining_uncommitted_classification = "NONE" });
        Check(RevisionApplication.CloseCheck(fixture, "P0-999-RV999").ExitCode != 0, "repository-native Revision cannot bypass committed lineage using false repository_changed");
        EditRevision(importedPath, "git_state", new { repository_changed = true, validated_commit_sha = sha, tested_commit_sha = sha, work_item_commit_shas = new[] { sha }, closure_commit_sha = (string?)null, remaining_uncommitted_paths = Array.Empty<string>(), remaining_uncommitted_classification = "PREEXISTING_UNRELATED" });
        EditRevision(importedPath, "required_roles", requiredAuditor);
        Check(RevisionApplication.CloseCheck(fixture, "P0-999-RV999").ExitCode != 0, "declared specialist review is mandatory for closure");
        EditRevision(importedPath, "required_roles", Array.Empty<string>());
        EditRevision(importedPath, "revision_type", "IMPROVEMENT");
        Check(GovernanceApplication.Validate(fixture).ExitCode == 0, "open Revision may refine non-identity fields");
        EditRevision(importedPath, "created_at", "2026-10-05T00:00:00Z");
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "committed Revision identity is immutable");
        EditRevision(importedPath, "created_at", ReadYamlScalar(RunGitOutput(fixture, "show", sha + ":governance/revisions/P0-999-RV999.yaml"), "created_at"));
        EditRevision(importedPath, "status", "CLOSED"); EditRevision(importedPath, "closed_at", "2026-10-05T15:00:00Z");
        RunGit(fixture, "add", "--", "governance/revisions/P0-999-RV999.yaml"); RunGit(fixture, "commit", "--quiet", "-m", "Revision closure metadata");
        Check(RevisionApplication.CheckTransition(fixture, "P0-999-RV999", "ACTIVE").ExitCode != 0, "closed Revision cannot be reopened");
        var closed = File.ReadAllText(importedPath);
        EditRevision(importedPath, "reason", "rewritten history");
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "closed Revision evidence/history cannot be silently edited");
        File.WriteAllText(importedPath, closed);
        File.Delete(importedPath);
        Check(GovernanceApplication.Validate(fixture).ExitCode != 0, "committed Revision deletion is rejected");
        File.WriteAllText(importedPath, closed);
        var manifestFile = new FileInfo(Path.Combine(fixture.FullName, "artifacts", "evidence", "revision-manifest.json"));
        var evidence = EvidenceApplication.Generate(fixture, "P0-RV001", manifestFile, [], ["governance/evidence/revision-proof.json"], ["governance/evidence/revision-proof.json"], ["governance/evidence/revision-proof.json"]);
        Check(evidence.ExitCode == 0 && JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(manifestFile.FullName))?.RevisionRef == "P0-RV001", "generated evidence links the Revision scope");
        Check(File.ReadAllText(Path.Combine(repositoryRoot.FullName, "governance", "policies", "lifecycle-policy.yaml")).Contains("REOPENED", StringComparison.Ordinal), "active-cycle REOPENED capability remains in approved policy");
        var archive = "docs/authority/baselines/1.6.0/Tunner_Development_Documentation_Baseline_1.6.0_LOCKED.zip";
        if (File.Exists(Path.Combine(repositoryRoot.FullName, archive)))
        {
            Check(RunGitOutput(repositoryRoot, "check-attr", "text", "--", archive).Contains("unset", StringComparison.Ordinal), "binary ZIP override prevents text normalization");
            Check(RunGitOutput(repositoryRoot, "hash-object", archive) == RunGitOutput(repositoryRoot, "hash-object", "--no-filters", archive), "ZIP filtered and raw blob hashes agree");
        }
        Console.WriteLine($"PASS: {assertions} RV schema/lifecycle/context/recovery/portability checks");
    }
    finally { DeleteFixture(fixture); }
}

static void EditRevision(string path, string field, object value)
{
    var stream = new YamlDotNet.RepresentationModel.YamlStream(); stream.Load(new StringReader(File.ReadAllText(path)));
    var mapping = (YamlDotNet.RepresentationModel.YamlMappingNode)stream.Documents[0].RootNode;
    var replacement = new YamlDotNet.RepresentationModel.YamlStream(); replacement.Load(new StringReader(JsonSerializer.Serialize(value)));
    mapping.Children[new YamlDotNet.RepresentationModel.YamlScalarNode(field)] = replacement.Documents[0].RootNode;
    using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture); stream.Save(writer, false);
    File.WriteAllText(path, writer.ToString());
}

static string ReadYamlScalar(string text, string field)
{
    var stream = new YamlDotNet.RepresentationModel.YamlStream(); stream.Load(new StringReader(text));
    return ((YamlDotNet.RepresentationModel.YamlScalarNode)((YamlDotNet.RepresentationModel.YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlDotNet.RepresentationModel.YamlScalarNode(field)]).Value ?? "";
}

static void DeleteFixture(DirectoryInfo fixtureRoot)
{
    var verifiedFixturePath = Path.GetFullPath(fixtureRoot.FullName);
    var verifiedTempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!verifiedFixturePath.StartsWith(verifiedTempRoot, StringComparison.OrdinalIgnoreCase) || !fixtureRoot.Name.StartsWith("tunner-governance-", StringComparison.Ordinal)) throw new InvalidOperationException("Fixture cleanup target is outside the generated test-fixture boundary.");
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
    foreach (var path in Directory.EnumerateFiles(repository.FullName, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(repository.FullName, x).Replace('\\', '/')).Where(x => !x.StartsWith(".git/", StringComparison.Ordinal) && !x.StartsWith("artifacts/", StringComparison.Ordinal)))
        RunGit(repository, "add", "--", path);
    RunGit(repository, "commit", "--quiet", "-m", "fixture baseline");
}

static string RunGitOutput(DirectoryInfo repository, params string[] arguments)
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
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Fixture git command failed: {error}");
    }

    return output.Trim();
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

static string HashSourceEvidence(string path)
{
    var text = File.ReadAllText(path)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal);
    return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text))).ToLowerInvariant();
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
