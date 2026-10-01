using System.CommandLine;
using System.CommandLine.Parsing;

namespace Tunner.Governance;

public static class Program
{
    public static int Main(string[] args)
    {
        var repositoryOption = new Option<DirectoryInfo?>("--repository")
        {
            Description = "Repository root. Defaults to the current directory."
        };

        var root = new RootCommand("Tunner repository-native governance control-plane tool");
        root.Options.Add(repositoryOption);

        var governance = new Command("governance", "Read-only governance validation and readiness commands.");
        governance.Subcommands.Add(CreateValidateCommand(repositoryOption));
        governance.Subcommands.Add(CreateStatusCommand(repositoryOption));
        governance.Subcommands.Add(CreateNextCommand(repositoryOption));
        governance.Subcommands.Add(CreateGateCommand(repositoryOption));
        governance.Subcommands.Add(CreateTransitionCommand(repositoryOption));
        governance.Subcommands.Add(CreateRolesCommand(repositoryOption));
        root.Subcommands.Add(governance);
        root.Subcommands.Add(CreateWorkCommand(repositoryOption));
        root.Subcommands.Add(CreateAuthorityCommand(repositoryOption));
        root.Subcommands.Add(CreateContextCommand(repositoryOption));
        root.Subcommands.Add(CreateEvidenceCommand(repositoryOption));
        root.Subcommands.Add(CreateSourcesCommand(repositoryOption));
        root.Subcommands.Add(CreateTelemetryCommand(repositoryOption));

        return root.Parse(args).Invoke();
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
        var verify = new Command("verify", "Verify the imported authority mirror against bootstrap SHA-256 evidence.");
        verify.SetAction(parseResult => WriteResult(AuthorityApplication.Verify(Repository(parseResult, repositoryOption))));

        var authority = new Command("authority", "Read-only imported-authority verification commands.");
        authority.Subcommands.Add(verify);
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
        => parseResult.GetValue(repositoryOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());

    private static int WriteResult(CommandResult result)
    {
        Console.WriteLine(GovernanceApplication.Serialize(result));
        return result.ExitCode;
    }
}