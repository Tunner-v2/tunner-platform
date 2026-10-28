using System.CommandLine;
using System.CommandLine.Parsing;

namespace Tunner.Governance;

public static class Program
{
    public static int Main(string[] args)
    {
        var repositoryOption = new Option<DirectoryInfo?>("--repo-root")
        {
            Description = "Validated Tunner repository root. Resolution otherwise uses TUNNER_REPO_ROOT, Git top-level, then .tunner-root discovery."
        };
        repositoryOption.Aliases.Add("--repository");

        var root = new RootCommand("Tunner repository-native governance control-plane tool");
        root.Options.Add(repositoryOption);

        var governance = new Command("governance", "Read-only governance validation and readiness commands.");
        governance.Subcommands.Add(CreateValidateCommand(repositoryOption));
        governance.Subcommands.Add(CreateStatusCommand(repositoryOption));
        governance.Subcommands.Add(CreateNextCommand(repositoryOption));
        governance.Subcommands.Add(CreateGateCommand(repositoryOption));
        governance.Subcommands.Add(CreateTransitionCommand(repositoryOption));
        governance.Subcommands.Add(CreateMilestoneCommand(repositoryOption));
        governance.Subcommands.Add(CreateRolesCommand(repositoryOption));
        governance.Subcommands.Add(CreateCommitReadinessCommand(repositoryOption));
        governance.Subcommands.Add(CreateRevisionCommand(repositoryOption));
        governance.Subcommands.Add(CreateUiComplianceCommand(repositoryOption));
        root.Subcommands.Add(governance);
        root.Subcommands.Add(CreateWorkCommand(repositoryOption));
        var bootstrapReplay = new Command("replay", "Replay bootstrap records and refuse unresolved exceptions.");
        bootstrapReplay.SetAction(parseResult => WriteResult(BootstrapApplication.Replay(Repository(parseResult, repositoryOption))));
        var bootstrap = new Command("bootstrap", "Read-only bootstrap replay controls."); bootstrap.Subcommands.Add(bootstrapReplay); root.Subcommands.Add(bootstrap);
        root.Subcommands.Add(CreateAuthorityCommand(repositoryOption));
        root.Subcommands.Add(CreateContextCommand(repositoryOption));
        root.Subcommands.Add(CreateEvidenceCommand(repositoryOption));
        root.Subcommands.Add(CreateSourcesCommand(repositoryOption));
        root.Subcommands.Add(CreateTelemetryCommand(repositoryOption));

        return root.Parse(args).Invoke();
    }

    private static Command CreateUiComplianceCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var scope = new Option<string>("--scope") { DefaultValueFactory = _ => "GLOBAL_UI" };
        var evidence = new Option<string>("--evidence") { DefaultValueFactory = _ => "governance/evidence/GLOBAL-UI-COMPLIANCE.json" };
        var check = new Command("check", "Verify global per-route/form AMD-0008/0009 acceptance evidence.");
        check.Options.Add(scope); check.Options.Add(evidence);
        check.SetAction(p => WriteResult(UiComplianceApplication.Check(Repository(p, repositoryOption), p.GetValue(scope)!, p.GetValue(evidence)!)));
        var command = new Command("ui", "Governed global UI acceptance controls."); command.Subcommands.Add(check); return command;
    }
    private static Command CreateRevisionCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var scope = new Option<string>("--scope");
        var type = new Option<string>("--type");
        var title = new Option<string>("--title");
        var reason = new Option<string>("--reason");
        var impact = new Option<string>("--impact");
        var blocked = new Option<string[]>("--blocked-scope");
        var authority = new Option<string[]>("--authority-ref");
        var decisions = new Option<string[]>("--decision-ref");
        var criteria = new Option<string[]>("--acceptance");
        var parent = new Option<string?>("--parent-milestone-revision");
        var create = new Command("create", "Allocate post-closure Revision lineage without changing original closure.");
        foreach (var option in new Option[] { scope, type, title, reason, impact, blocked, authority, decisions, criteria, parent }) create.Options.Add(option);
        create.SetAction(p => WriteResult(RevisionApplication.Create(Repository(p, repositoryOption), new RevisionCreateRequest(
            p.GetValue(scope) ?? "", p.GetValue(type) ?? "CORRECTIVE", p.GetValue(title) ?? "", p.GetValue(reason) ?? "",
            p.GetValue(impact) ?? "NON_BLOCKING", p.GetValue(blocked) ?? [], p.GetValue(authority) ?? [],
            p.GetValue(decisions) ?? [], p.GetValue(criteria) ?? [], p.GetValue(parent)))));
        var revision = new Command("revision", "Repository-native post-closure work lineage.");
        revision.Subcommands.Add(create);
        foreach (var name in new[] { "show", "close-check" })
        {
            var id = new Argument<string>("revision-id");
            var command = new Command(name, "Read-only Revision lineage and evidence check.");
            command.Arguments.Add(id);
            command.SetAction(p => WriteResult(name == "show" ? RevisionApplication.Show(Repository(p, repositoryOption), p.GetValue(id) ?? "") : RevisionApplication.CloseCheck(Repository(p, repositoryOption), p.GetValue(id) ?? "")));
            revision.Subcommands.Add(command);
        }
        var childId = new Argument<string>("revision-id");
        var childTitle = new Option<string>("--title");
        var child = new Command("child-create", "Allocate a governed child work item under an open Revision.");
        child.Arguments.Add(childId); child.Options.Add(childTitle);
        child.SetAction(p => WriteResult(RevisionApplication.CreateChild(Repository(p, repositoryOption), p.GetValue(childId) ?? "", p.GetValue(childTitle) ?? "")));
        revision.Subcommands.Add(child);
        var transitionId = new Argument<string>("revision-id");
        var destination = new Argument<string>("destination");
        var transitionCheck = new Command("check", "Check Revision lifecycle without changing state.");
        transitionCheck.Arguments.Add(transitionId); transitionCheck.Arguments.Add(destination);
        transitionCheck.SetAction(p => WriteResult(RevisionApplication.CheckTransition(Repository(p, repositoryOption), p.GetValue(transitionId) ?? "", p.GetValue(destination) ?? "")));
        var transition = new Command("transition", "Revision lifecycle eligibility.");
        transition.Subcommands.Add(transitionCheck); revision.Subcommands.Add(transition);
        return revision;
    }

    private static Command CreateValidateCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var command = new Command("validate", "Validate supported governance YAML records and their schema catalog.");
        command.SetAction(parseResult => WriteResult(GovernanceApplication.Validate(Repository(parseResult, repositoryOption))));
        return command;
    }

    private static Command CreateStatusCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var command = new Command("status", "Report milestones, work items, blockers, prerequisites, and validation state.");
        command.SetAction(parseResult => WriteResult(GovernanceApplication.Status(Repository(parseResult, repositoryOption))));
        return command;
    }

    private static Command CreateNextCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var command = new Command("next", "Report active and eligible work items with deterministic reasons.");
        command.SetAction(parseResult => WriteResult(GovernanceApplication.Next(Repository(parseResult, repositoryOption))));
        return command;
    }

    private static Command CreateGateCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Argument<string>("work-item") { Description = "Work item identifier, for example TUN-P0-004." };
        var check = new Command("check", "Check a work item’s read-only readiness gate.");
        check.Arguments.Add(workItem);
        check.SetAction(parseResult => WriteResult(GovernanceApplication.CheckGate(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(workItem) ?? string.Empty)));

        var gate = new Command("gate", "Evaluate read-only governance gates.");
        gate.Subcommands.Add(check);
        return gate;
    }

    private static Command CreateTransitionCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Argument<string>("work-item") { Description = "Work item identifier." };
        var destination = new Argument<string>("destination") { Description = "Requested lifecycle destination state." };
        var check = new Command("check", "Check a lifecycle transition without mutating the record.");
        check.Arguments.Add(workItem);
        check.Arguments.Add(destination);
        check.SetAction(parseResult => WriteResult(GovernanceApplication.CheckTransition(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(workItem) ?? string.Empty,
            parseResult.GetValue(destination) ?? string.Empty)));

        var transition = new Command("transition", "Check lifecycle transition legality without mutation.");
        transition.Subcommands.Add(check);
        return transition;
    }

    private static Command CreateMilestoneCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var milestone = new Argument<string>("milestone") { Description = "Milestone identifier, for example P0." };

        var closeCheck = new Command("close-check", "Evaluate the read-only evidence and governance requirements for milestone closure.");
        closeCheck.Arguments.Add(milestone);
        closeCheck.SetAction(parseResult => WriteResult(GovernanceApplication.CheckMilestoneClose(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(milestone) ?? string.Empty)));

        var destination = new Argument<string>("destination") { Description = "Requested milestone lifecycle destination state." };
        var transitionCheck = new Command("check", "Check a milestone lifecycle transition without mutating the record.");
        transitionCheck.Arguments.Add(milestone);
        transitionCheck.Arguments.Add(destination);
        transitionCheck.SetAction(parseResult => WriteResult(GovernanceApplication.CheckMilestoneTransition(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(milestone) ?? string.Empty,
            parseResult.GetValue(destination) ?? string.Empty)));

        var transition = new Command("transition", "Check milestone lifecycle transition legality without mutation.");
        transition.Subcommands.Add(transitionCheck);

        var command = new Command("milestone", "Read-only milestone closure and lifecycle controls.");
        var snapshot = new Command("review-snapshot", "Return the exact committed scope binding for milestone role reviews.");
        snapshot.Arguments.Add(milestone);
        snapshot.SetAction(p => WriteResult(GovernanceApplication.MilestoneReviewSnapshot(Repository(p, repositoryOption), p.GetValue(milestone) ?? string.Empty)));
        command.Subcommands.Add(snapshot);
        command.Subcommands.Add(closeCheck);
        command.Subcommands.Add(transition);
        return command;
    }
    private static Command CreateRolesCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Argument<string>("work-item") { Description = "Work item identifier." };
        var calculate = new Command("calculate", "Calculate mandatory roles from the declared impact classification.");
        calculate.Arguments.Add(workItem);
        calculate.SetAction(parseResult => WriteResult(RoleActivationApplication.Calculate(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(workItem) ?? string.Empty)));

        var roles = new Command("roles", "Calculate and validate role-activation requirements.");
        roles.Subcommands.Add(calculate);
        return roles;
    }

    private static Command CreateCommitReadinessCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var milestone = new Option<string?>("--milestone") { Description = "Optional milestone scope for the committed-state readiness audit." };
        var audit = new Command("audit", "Audit committed-state readiness without mutating governance records.");
        audit.Options.Add(milestone);
        audit.SetAction(parseResult =>
        {
            var findings = CommitReadinessApplication.Audit(Repository(parseResult, repositoryOption), parseResult.GetValue(milestone));
            return WriteResult(new CommandResult(findings.Count == 0 ? 0 : 1, "commit-readiness audit", new { Outcome = findings.Count == 0 ? "PASS" : "REMEDIATION_REQUIRED", Findings = findings }));
        });
        var command = new Command("commit-readiness", "Read-only AMD-0002 committed-state readiness controls.");
        command.Subcommands.Add(audit);
        return command;
    }
    private static Command CreateWorkCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Argument<string>("work-item") { Description = "Work item identifier." };
        Command CommandFor(string name, string description, Func<DirectoryInfo, string, CommandResult> action)
        {
            var command = new Command(name, description);
            command.Arguments.Add(workItem);
            command.SetAction(parseResult => WriteResult(action(Repository(parseResult, repositoryOption), parseResult.GetValue(workItem) ?? string.Empty)));
            return command;
        }

        var contextOutput = new Option<DirectoryInfo?>("--output") { Description = "Context output directory; defaults to docs/context/current." };
        var context = new Command("context", "Delegate bounded context generation after governance eligibility succeeds.");
        context.Arguments.Add(workItem);
        context.Options.Add(contextOutput);
        context.SetAction(parseResult =>
        {
            var repository = Repository(parseResult, repositoryOption);
            return WriteResult(OrchestratorApplication.Context(repository, parseResult.GetValue(workItem) ?? string.Empty, parseResult.GetValue(contextOutput) ?? new DirectoryInfo(Path.Combine(repository.FullName, "docs", "context", "current"))));
        });

        var work = new Command("work", "Governance-controlled orchestration dispatcher; it does not mutate work state or execute Product operations.");
        work.Subcommands.Add(CommandFor("start", "Confirm that governance permits orchestration.", OrchestratorApplication.Start));
        work.Subcommands.Add(context);
        work.Subcommands.Add(CommandFor("run", "Return the governed execution plan without performing Product operations.", OrchestratorApplication.Run));
        work.Subcommands.Add(CommandFor("validate", "Delegate final readiness to governance gate check.", OrchestratorApplication.Validate));
        work.Subcommands.Add(CommandFor("handoff", "Return deterministic handoff instructions only after a passing governance gate.", OrchestratorApplication.Handoff));
        return work;
    }
    private static Command CreateAuthorityCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var status = new Command("status", "Report the locally imported authority lineage and hash-verification state.");
        status.SetAction(parseResult => WriteResult(AuthorityApplication.Status(Repository(parseResult, repositoryOption))));

        var import = new Command("import", "Verify the already materialized authority mirror without fetching or selecting remote authority.");
        import.SetAction(parseResult => WriteResult(AuthorityApplication.Import(Repository(parseResult, repositoryOption))));

        var verify = new Command("verify", "Verify the imported authority mirror against bootstrap SHA-256 evidence.");
        verify.SetAction(parseResult => WriteResult(AuthorityApplication.Verify(Repository(parseResult, repositoryOption))));

        var diff = new Command("diff", "Report offline mirror integrity; canonical-source candidates must be explicitly fetched and hash-recorded.");
        diff.SetAction(parseResult => WriteResult(AuthorityApplication.Diff(Repository(parseResult, repositoryOption))));

        var authority = new Command("authority", "Read-only imported-authority verification commands.");
        authority.Subcommands.Add(status);
        authority.Subcommands.Add(import);
        authority.Subcommands.Add(verify);
        authority.Subcommands.Add(diff);
        return authority;
    }
    private static Command CreateContextCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Option<string>("--work-item") { Description = "Governed work item identifier to package." };
        var output = new Option<DirectoryInfo?>("--output") { Description = "Generated pack directory. Defaults to docs/context/current." };
        var mode = new Option<string>("--mode") { Description = "CORE, TASK, EXPANDED, or FULL_AUDIT. Defaults to TASK." };
        var tokenBudget = new Option<int?>("--token-budget") { Description = "Maximum approximate prompt-input token budget." };
        var accessScope = new Option<string>("--access-scope") { Description = "Authorized repository access scope; independent from prompt selection." };
        var build = new Command("build", "Generate a bounded, manifest-first adaptive context pack.");
        build.Options.Add(workItem); build.Options.Add(output); build.Options.Add(mode); build.Options.Add(tokenBudget); build.Options.Add(accessScope);
        build.SetAction(parseResult =>
        {
            var repository = Repository(parseResult, repositoryOption);
            var destination = parseResult.GetValue(output) ?? new DirectoryInfo(Path.Combine(repository.FullName, "docs", "context", "current"));
            return WriteResult(AdaptiveContextApplication.Build(repository, parseResult.GetValue(workItem) ?? string.Empty, destination, new AdaptiveContextRequest(parseResult.GetValue(mode) ?? "TASK", parseResult.GetValue(tokenBudget), parseResult.GetValue(accessScope) ?? "REPOSITORY_READ")));
        });
        var verify = new Command("verify", "Verify the generated context pack source hashes and adaptive index freshness.");
        verify.Options.Add(output);
        verify.SetAction(parseResult =>
        {
            var repository = Repository(parseResult, repositoryOption);
            var destination = parseResult.GetValue(output) ?? new DirectoryInfo(Path.Combine(repository.FullName, "docs", "context", "current"));
            return WriteResult(AdaptiveContextApplication.Verify(repository, destination));
        });
        var indexOutput = new Option<FileInfo?>("--output") { Description = "Disposable index path; defaults to artifacts/context-index/index.json." };
        var indexBuild = new Command("build", "Build a deterministic disposable authority/repository discovery index.");
        indexBuild.Options.Add(indexOutput);
        indexBuild.SetAction(parseResult =>
        {
            var repository = Repository(parseResult, repositoryOption);
            return WriteResult(AdaptiveContextApplication.BuildIndex(repository, parseResult.GetValue(indexOutput) ?? new FileInfo(Path.Combine(repository.FullName, "artifacts", "context-index", "index.json"))));
        });
        var index = new Command("index", "Build disposable repository discovery indexes.");
        index.Subcommands.Add(indexBuild);
        var escalationWorkItem = new Argument<string>("work-item") { Description = "Governed work item identifier." };
        var from = new Option<string>("--from") { Description = "Current context mode." };
        var reason = new Option<string>("--reason") { Description = "Why the current bounded context is insufficient." };
        var escalate = new Command("escalate", "Return the next governed context mode without implicitly loading sources.");
        escalate.Arguments.Add(escalationWorkItem); escalate.Options.Add(from); escalate.Options.Add(reason);
        escalate.SetAction(parseResult => WriteResult(AdaptiveContextApplication.Escalate(Repository(parseResult, repositoryOption), parseResult.GetValue(escalationWorkItem) ?? string.Empty, parseResult.GetValue(from) ?? "TASK", parseResult.GetValue(reason) ?? string.Empty)));
        var context = new Command("context", "Generate, verify, index, and escalate bounded repository context.");
        context.Subcommands.Add(build); context.Subcommands.Add(verify); context.Subcommands.Add(index); context.Subcommands.Add(escalate);
        return context;
    }
    private static Command CreateEvidenceCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var scopeId = new Option<string>("--scope-id") { Description = "Governed work-item identifier represented by the manifest." };
        var output = new Option<FileInfo?>("--output") { Description = "New JSON manifest path beneath artifacts/evidence/." };
        var artifact = new Option<string[]>("--artifact") { Description = "Additional repository-relative artifact to hash. Repeat for multiple artifacts." };
        var build = new Option<string[]>("--build-reference") { Description = "Repository-relative build evidence to hash. Repeat for multiple references." };
        var test = new Option<string[]>("--test-reference") { Description = "Repository-relative test evidence to hash. Repeat for multiple references." };
        var securityScan = new Option<string[]>("--security-scan-reference") { Description = "Repository-relative security-scan evidence to hash. Repeat for multiple references." };

        var generate = new Command("generate", "Create a deterministic local evidence manifest without release or production claims.");
        generate.Options.Add(scopeId);
        generate.Options.Add(output);
        generate.Options.Add(artifact);
        generate.Options.Add(build);
        generate.Options.Add(test);
        generate.Options.Add(securityScan);
        generate.SetAction(parseResult => WriteResult(EvidenceApplication.Generate(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(scopeId) ?? string.Empty,
            parseResult.GetValue(output),
            parseResult.GetValue(artifact) ?? [],
            parseResult.GetValue(build) ?? [],
            parseResult.GetValue(test) ?? [],
            parseResult.GetValue(securityScan) ?? [])));

        var evidence = new Command("evidence", "Generate bounded, local evidence manifests.");
        evidence.Subcommands.Add(generate);
        return evidence;
    }
    private static Command CreateTelemetryCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var workItem = new Option<string>("--work-item");
        var output = new Option<FileInfo?>("--output");
        var mode = new Option<string>("--context-mode");
        var included = new Option<int>("--included");
        var excluded = new Option<int>("--excluded");
        var tokens = new Option<int?>("--approximate-input-tokens");
        var cache = new Option<string>("--cache-outcome");
        var stale = new Option<bool>("--stale-regenerated");
        var expansion = new Option<string?>("--expansion-reason");
        var roles = new Option<string[]>("--role");
        var gate = new Option<string>("--gate-outcome");
        var record = new Command("record", "Write a local, secret-safe context quality telemetry record.");
        foreach(var option in new Option[]{workItem,output,mode,included,excluded,tokens,cache,stale,expansion,roles,gate}) record.Options.Add(option);
        record.SetAction(p => WriteResult(ContextTelemetryApplication.Record(Repository(p, repositoryOption), p.GetValue(workItem) ?? string.Empty, p.GetValue(output), new ContextTelemetryInput(p.GetValue(mode) ?? "TASK",p.GetValue(included),p.GetValue(excluded),p.GetValue(tokens),p.GetValue(cache) ?? "NOT_AVAILABLE",p.GetValue(stale),p.GetValue(expansion),p.GetValue(roles) ?? [],p.GetValue(gate) ?? "UNKNOWN"))));
        var telemetry = new Command("telemetry", "Record repository-local governance/context quality metadata.");
        telemetry.Subcommands.Add(record);
        return telemetry;
    }
    private static Command CreateSourcesCommand(Option<DirectoryInfo?> repositoryOption)
    {
        var scopeId = new Option<string?>("--scope-id") { Description = "Optional registered work-item or decision identifier to check." };
        var asOf = new Option<string>("--as-of") { Description = "Required ISO-8601 date used for deterministic freshness evaluation." };
        var maxAgeDays = new Option<int?>("--max-age-days") { Description = "Required non-negative freshness window in days." };
        var check = new Command("check", "Check registered material scopes against local primary-source evidence.");
        check.Options.Add(scopeId);
        check.Options.Add(asOf);
        check.Options.Add(maxAgeDays);
        check.SetAction(parseResult => WriteResult(SourceRegistryApplication.Check(
            Repository(parseResult, repositoryOption),
            parseResult.GetValue(scopeId),
            parseResult.GetValue(asOf) ?? string.Empty,
            parseResult.GetValue(maxAgeDays))));

        var sources = new Command("sources", "Verify local R&D source-registry evidence without external browsing.");
        sources.Subcommands.Add(check);
        return sources;
    }
    private static DirectoryInfo Repository(ParseResult parseResult, Option<DirectoryInfo?> repositoryOption)
        => RepositoryRootResolver.Resolve(parseResult.GetValue(repositoryOption));

    private static int WriteResult(CommandResult result)
    {
        Console.WriteLine(GovernanceApplication.Serialize(result));
        return result.ExitCode;
    }
}
