namespace Tunner.Governance;

/// <summary>
/// A governed dispatcher. It has no authority to mutate lifecycle state, merge
/// pull requests, release software, or execute Product operations.
/// </summary>
public static class OrchestratorApplication
{
    public static CommandResult Start(DirectoryInfo repository, string workItemId)
    {
        var authority = AuthorityApplication.Verify(repository);
        if (authority.ExitCode != 0)
        {
            return Blocked(workItemId, "AUTHORITY_INVALID", ((AuthorityPayload)authority.Payload).Findings);
        }

        var diagnostics = new List<Diagnostic>();
        var records = GovernanceApplication.LoadRecords(repository, diagnostics);
        var workItems = GovernanceApplication.WorkItems(records);
        if (!workItems.TryGetValue(workItemId, out var workItem))
        {
            return Blocked(workItemId, "NOT_FOUND", ["work item does not exist"]);
        }

        var state = workItem.Scalar("status") ?? "UNKNOWN";
        if (!new[] { "READY", "IN_PROGRESS", "CODE_REVIEW", "VALIDATION" }.Contains(state, StringComparer.Ordinal))
        {
            return Blocked(workItemId, "LIFECYCLE_REFUSED", [$"work item state '{state}' is not eligible for orchestrated execution"]);
        }

        var next = GovernanceApplication.Next(repository);
        var item = ((NextPayload)next.Payload).Items.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.WorkItemId, workItemId));
        var hardBlock = item is not null && (item.Reason.Contains("prerequisite '", StringComparison.Ordinal) || item.Reason.Contains("blocker:", StringComparison.Ordinal) || item.Reason.Contains("authority verification failed", StringComparison.Ordinal) || item.Reason.Contains("unmerged", StringComparison.Ordinal));
        if (next.ExitCode != 0 || item is null || (!item.Actionable && !StringComparer.Ordinal.Equals(item.Reason, "all recorded prerequisites and blockers permit work")) || hardBlock)
        {
            return Blocked(workItemId, "GOVERNANCE_REFUSED", item is null ? ["governance did not expose the requested work item as actionable"] : [item.Reason]);
        }

        var roles = (RoleCalculationPayload)RoleActivationApplication.Calculate(repository, workItemId).Payload;
        if (roles.Findings.Count > 0)
        {
            return Blocked(workItemId, "ROLE_ACTIVATION_REFUSED", roles.Findings);
        }

        return new CommandResult(0, "work start", new OrchestratorPayload("ELIGIBLE", workItemId, roles.RequiredRoles, ["build bounded context", "implement only within declared authority", "run declared tests", "collect structured role reviews", "delegate final readiness to governance gate"], []));
    }

    public static CommandResult Context(DirectoryInfo repository, string workItemId, DirectoryInfo output)
    {
        var start = Start(repository, workItemId);
        if (start.ExitCode != 0)
        {
            return start with { Command = "work context" };
        }

        var build = ContextApplication.Build(repository, workItemId, output);
        return build.ExitCode == 0
            ? new CommandResult(0, "work context", new OrchestratorPayload("CONTEXT_READY", workItemId, ((OrchestratorPayload)start.Payload).RequiredRoles, ["context was delegated to tunner-context"], []))
            : new CommandResult(build.ExitCode, "work context", build.Payload);
    }

    public static CommandResult Run(DirectoryInfo repository, string workItemId)
    {
        var start = Start(repository, workItemId);
        if (start.ExitCode != 0)
        {
            return start with { Command = "work run" };
        }

        var payload = (OrchestratorPayload)start.Payload;
        return new CommandResult(0, "work run", payload with { Outcome = "PLAN_READY", Steps = ["orchestrator performs no ungoverned tool calls", .. payload.Steps] });
    }

    public static CommandResult Validate(DirectoryInfo repository, string workItemId)
    {
        var start = Start(repository, workItemId);
        if (start.ExitCode != 0)
        {
            return start with { Command = "work validate" };
        }

        var gate = GovernanceApplication.CheckGate(repository, workItemId);
        var gatePayload = (GatePayload)gate.Payload;
        return new CommandResult(gate.ExitCode, "work validate", new OrchestratorPayload(gate.ExitCode == 0 ? "VALIDATED" : "GATE_BLOCKED", workItemId, ((OrchestratorPayload)start.Payload).RequiredRoles, gatePayload.Satisfied, gatePayload.Failures));
    }

    public static CommandResult Handoff(DirectoryInfo repository, string workItemId)
    {
        var validation = Validate(repository, workItemId);
        if (validation.ExitCode != 0)
        {
            return validation with { Command = "work handoff" };
        }

        var payload = (OrchestratorPayload)validation.Payload;
        return new CommandResult(0, "work handoff", payload with { Outcome = "HANDOFF_READY", Steps = ["refresh repository-native handoff and project state", "run governance next", "request human action only for an explicit human gate"] });
    }

    private static CommandResult Blocked(string workItemId, string outcome, IReadOnlyList<string> findings)
        => new(1, "work", new OrchestratorPayload(outcome, workItemId, [], [], findings));
}

public sealed record OrchestratorPayload(string Outcome, string WorkItemId, IReadOnlyList<string> RequiredRoles, IReadOnlyList<string> Steps, IReadOnlyList<string> Findings);