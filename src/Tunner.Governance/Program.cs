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
        root.Subcommands.Add(governance);

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
        var workItem = new Argument<string>("work-item", "Work item identifier, for example TUN-P0-004.");
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
        var workItem = new Argument<string>("work-item", "Work item identifier.");
        var destination = new Argument<string>("destination", "Requested lifecycle destination state.");
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

    private static DirectoryInfo Repository(ParseResult parseResult, Option<DirectoryInfo?> repositoryOption)
        => parseResult.GetValue(repositoryOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());

    private static int WriteResult(CommandResult result)
    {
        Console.WriteLine(GovernanceApplication.Serialize(result));
        return result.ExitCode;
    }
}