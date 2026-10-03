using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

internal sealed record GitStateDefinition(
    bool? RepositoryChanged,
    string? ValidatedCommitSha,
    string? TestedCommitSha,
    IReadOnlyList<string> WorkItemCommitShas,
    string? ClosureCommitSha,
    IReadOnlyList<string> RemainingUncommittedPaths,
    string? RemainingUncommittedClassification);

internal static class CommitReadinessApplication
{
    private static readonly string[] AllowedClassifications = ["NONE", "PREEXISTING_UNRELATED", "UNRELATED_ACTIVE_WORK", "WORK_ITEM_OWNED"];

    public static GitStateDefinition? Read(GovernanceRecord record)
    {
        if (!record.Fields.TryGetValue("git_state", out var node) || node is not YamlMappingNode mapping)
        {
            return null;
        }

        var fields = GovernanceApplication.Fields(mapping);
        return new GitStateDefinition(
            ParseBoolean(GovernanceApplication.Scalar(fields, "repository_changed")),
            NullIfEmpty(GovernanceApplication.Scalar(fields, "validated_commit_sha")),
            NullIfEmpty(GovernanceApplication.Scalar(fields, "tested_commit_sha")),
            GovernanceApplication.StringList(fields, "work_item_commit_shas"),
            NullIfEmpty(GovernanceApplication.Scalar(fields, "closure_commit_sha")),
            GovernanceApplication.StringList(fields, "remaining_uncommitted_paths"),
            NullIfEmpty(GovernanceApplication.Scalar(fields, "remaining_uncommitted_classification")));
    }

    public static IReadOnlyList<string> Check(DirectoryInfo repository, GovernanceRecord record)
    {
        var state = Read(record);
        if (state is null)
        {
            return [];
        }

        var failures = new List<string>();
        if (state.RepositoryChanged is null)
        {
            failures.Add("git_state.repository_changed must be true or false");
            return failures;
        }

        if (!state.RepositoryChanged.Value)
        {
            return failures;
        }

        if (state.ValidatedCommitSha is null) failures.Add("repository-changing work requires git_state.validated_commit_sha");
        if (state.TestedCommitSha is null) failures.Add("repository-changing work requires git_state.tested_commit_sha");
        if (state.ValidatedCommitSha is not null && state.TestedCommitSha is not null && !StringComparer.Ordinal.Equals(state.ValidatedCommitSha, state.TestedCommitSha)) failures.Add("tested_commit_sha must equal validated_commit_sha for behavior-affecting validation");
        if (state.ValidatedCommitSha is not null && !state.WorkItemCommitShas.Contains(state.ValidatedCommitSha, StringComparer.Ordinal)) failures.Add("work_item_commit_shas must include validated_commit_sha");
        if (state.RemainingUncommittedClassification is null || !AllowedClassifications.Contains(state.RemainingUncommittedClassification, StringComparer.Ordinal)) failures.Add("remaining_uncommitted_classification is unsupported");
        if (StringComparer.Ordinal.Equals(state.RemainingUncommittedClassification, "WORK_ITEM_OWNED")) failures.Add("work-item-owned uncommitted changes block local readiness and completion");
        if (StringComparer.Ordinal.Equals(state.RemainingUncommittedClassification, "NONE") && state.RemainingUncommittedPaths.Count > 0) failures.Add("classification NONE cannot list remaining uncommitted paths");
        if (state.ValidatedCommitSha is not null && !GitCommitExists(repository, state.ValidatedCommitSha)) failures.Add("validated_commit_sha does not resolve to a local Git commit");
        if (state.TestedCommitSha is not null && !GitCommitExists(repository, state.TestedCommitSha)) failures.Add("tested_commit_sha does not resolve to a local Git commit");
        return failures;
    }

    public static IReadOnlyList<string> Audit(DirectoryInfo repository, string? milestoneId)
    {
        var diagnostics = new List<Diagnostic>();
        var records = GovernanceApplication.LoadRecords(repository, diagnostics);
        var findings = diagnostics.Where(item => item.Severity == "ERROR").Select(item => $"record parse issue: {item.Code}").ToList();
        foreach (var record in records.Where(item => item.Type.DefinitionName == "work-item" && (milestoneId is null || StringComparer.Ordinal.Equals(item.Scalar("milestone_id"), milestoneId)) && new[] { "LOCAL_VALIDATED", "READY_FOR_MERGE", "DONE" }.Contains(item.Scalar("status"), StringComparer.Ordinal)).OrderBy(item => item.Identifier, StringComparer.Ordinal))
        {
            var state = Read(record);
            if (state is null) findings.Add($"{record.Identifier}: git_state is absent; historical compatibility is preserved but committed-state readiness is not evidenced");
            else findings.AddRange(Check(repository, record).Select(item => $"{record.Identifier}: {item}"));
        }
        return findings;
    }

    private static bool? ParseBoolean(string? value) => value?.ToLowerInvariant() switch { "true" => true, "false" => false, _ => null };
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) || StringComparer.Ordinal.Equals(value, "null") ? null : value;

    private static bool GitCommitExists(DirectoryInfo repository, string sha)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", $"-C \"{repository.FullName}\" rev-parse --verify {sha}^{{commit}}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}