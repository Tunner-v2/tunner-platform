using System.Globalization;
using System.Text.Json;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

public static class GovernanceApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static readonly IReadOnlyList<RecordTypeDefinition> RecordTypes =
    [
        new("milestones", "milestone", "milestone.schema.json", "milestone", "milestone_id"),
        new("sprints", "sprint", "sprint.schema.json", "sprint", "sprint_id"),
        new("work-items", "work-item", "work-item.schema.json", "workItem", "work_item_id"),
        new("todos", "todo", "todo.schema.json", "todo", "id"),
        new("defects", "defect", "defect.schema.json", "defect", "defect_id"),
        new("reopens", "reopen", "reopen.schema.json", "reopen", "reopen_id"),
        new("decisions", "product-decision", "product-decision.schema.json", "productDecision", "decision_id"),
        new("gates", "gate", "gate.schema.json", "gate", "gate_id"),
        new("releases", "release", "release.schema.json", "release", "release_id"),
        new("context", "context-selection", "context-selection.schema.json", "contextSelection", "selection_id")
    ];

    public static CommandResult Validate(DirectoryInfo repository)
    {
        var diagnostics = new List<Diagnostic>();
        var schemas = SchemaCatalog.Load(repository, diagnostics);
        var records = LoadRecords(repository, diagnostics);

        foreach (var record in records)
        {
            if (!schemas.TryGetValue(record.Type.DefinitionName, out var schema))
            {
                diagnostics.Add(Diagnostic.Error(record.Path, record.Root.Start, "GOV_SCHEMA_MISSING", $"No schema is available for '{record.Type.DefinitionName}'."));
                continue;
            }

            foreach (var required in schema.RequiredFields)
            {
                if (!record.Fields.ContainsKey(required))
                {
                    diagnostics.Add(Diagnostic.Error(record.Path, record.Root.Start, "GOV_REQUIRED_FIELD", $"'{required}' is required by {record.Type.SchemaFileName}."));
                }
            }

            foreach (var actual in record.Fields.Keys.Where(name => !schema.PermittedFields.Contains(name, StringComparer.Ordinal)))
            {
                diagnostics.Add(Diagnostic.Error(record.Path, record.Root.Start, "GOV_UNKNOWN_FIELD", $"'{actual}' is not declared by {record.Type.SchemaFileName}."));
            }

            if (!int.TryParse(record.Scalar("schema_version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version < 1)
            {
                diagnostics.Add(Diagnostic.Error(record.Path, record.Root.Start, "GOV_SCHEMA_VERSION", "schema_version must be an integer of at least 1."));
            }
        }

        var isValid = diagnostics.All(item => item.Severity != "ERROR");
        return new CommandResult(isValid ? 0 : 1, "validate", new ValidationPayload(isValid, records.Count, diagnostics));
    }

    public static CommandResult Status(DirectoryInfo repository)
    {
        var diagnostics = new List<Diagnostic>();
        var records = LoadRecords(repository, diagnostics);
        var validation = Validate(repository);
        var milestones = records.Where(item => item.Type.DefinitionName == "milestone")
            .Select(item => new MilestoneStatus(item.Identifier, item.Scalar("status") ?? "UNKNOWN"))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var workItems = records.Where(item => item.Type.DefinitionName == "work-item")
            .Select(item => new WorkItemStatus(item.Identifier, item.Scalar("status") ?? "UNKNOWN", item.StringList("prerequisites"), item.StringList("blockers"), item.StringList("required_evidence")))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        return new CommandResult(validation.ExitCode, "status", new StatusPayload(milestones, workItems, validation.Payload));
    }

    public static CommandResult Next(DirectoryInfo repository)
    {
        var diagnostics = new List<Diagnostic>();
        var records = LoadRecords(repository, diagnostics);
        var workItems = WorkItems(records);
        var allowValidatedP0Prerequisites = HasApprovedP0LocalIntegration(records);
        var next = new List<NextItem>();

        foreach (var record in workItems.Values.OrderBy(item => item.Identifier, StringComparer.Ordinal))
        {
            var state = record.Scalar("status") ?? "UNKNOWN";
            if (TerminalStates.Contains(state, StringComparer.Ordinal))
            {
                continue;
            }

            var reasons = ReadinessReasons(record, workItems, allowValidatedP0Prerequisites);
            var active = StringComparer.Ordinal.Equals(state, "IN_PROGRESS");
            next.Add(new NextItem(record.Identifier, state, active || reasons.Count == 0, active ? "currently in progress" : reasons.Count == 0 ? "all recorded prerequisites and blockers permit work" : string.Join("; ", reasons)));
        }

        return new CommandResult(diagnostics.Count == 0 ? 0 : 1, "next", new NextPayload(next, diagnostics));
    }

    public static CommandResult CheckGate(DirectoryInfo repository, string workItemId)
    {
        var diagnostics = new List<Diagnostic>();
        var records = LoadRecords(repository, diagnostics);
        var workItems = WorkItems(records);
        if (!workItems.TryGetValue(workItemId, out var workItem))
        {
            return new CommandResult(2, "gate check", new GatePayload(workItemId, "NOT_FOUND", ["work item does not exist"], []));
        }

        var failures = ReadinessReasons(workItem, workItems, HasApprovedP0LocalIntegration(records));
        var satisfied = new List<string>();
        foreach (var evidencePath in workItem.StringList("required_evidence"))
        {
            var fullPath = Path.Combine(repository.FullName, evidencePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                satisfied.Add($"evidence exists: {evidencePath}");
            }
            else
            {
                failures.Add($"required evidence missing: {evidencePath}");
            }
        }

        var reviewEvidence = $"governance/evidence/{workItem.Identifier}-ROLE-REVIEWS.json";
        if (File.Exists(Path.Combine(repository.FullName, reviewEvidence.Replace('/', Path.DirectorySeparatorChar))))
        {
            satisfied.Add("role review evidence exists");
        }
        else
        {
            failures.Add($"role review evidence missing: {reviewEvidence}");
        }

        foreach (var diagnostic in diagnostics.Where(item => item.Severity == "ERROR"))
        {
            failures.Add($"record parse issue: {diagnostic.Code}");
        }

        var outcome = failures.Count == 0 ? "READY" : "NOT_READY";
        return new CommandResult(failures.Count == 0 ? 0 : 1, "gate check", new GatePayload(workItemId, outcome, failures, satisfied));
    }

    public static CommandResult CheckTransition(DirectoryInfo repository, string workItemId, string destination)
    {
        var diagnostics = new List<Diagnostic>();
        var records = LoadRecords(repository, diagnostics);
        var workItems = WorkItems(records);
        if (!workItems.TryGetValue(workItemId, out var workItem))
        {
            return new CommandResult(2, "transition check", new TransitionPayload(workItemId, "UNKNOWN", destination, "NOT_FOUND", "work item does not exist"));
        }

        var policy = LifecyclePolicy.Load(repository, diagnostics);
        var from = workItem.Scalar("status") ?? "UNKNOWN";
        if (diagnostics.Count > 0)
        {
            return new CommandResult(1, "transition check", new TransitionPayload(workItemId, from, destination, "INSUFFICIENT_CONTEXT", string.Join("; ", diagnostics.Select(item => item.Message))));
        }

        if (policy.SideStates.Contains(from, StringComparer.Ordinal) || policy.SideStates.Contains(destination, StringComparer.Ordinal))
        {
            return new CommandResult(1, "transition check", new TransitionPayload(workItemId, from, destination, "INSUFFICIENT_CONTEXT", "The policy lists side states but does not define their transition graph."));
        }

        var fromIndex = Array.IndexOf(policy.NormalStates.ToArray(), from);
        var destinationIndex = Array.IndexOf(policy.NormalStates.ToArray(), destination);
        var allowed = fromIndex >= 0 && destinationIndex == fromIndex + 1;
        return new CommandResult(allowed ? 0 : 1, "transition check", new TransitionPayload(workItemId, from, destination, allowed ? "ALLOWED" : "REJECTED", allowed ? "The destination is the next declared normal lifecycle state." : "Only the next declared normal lifecycle state is allowed by this MVP."));
    }

    public static string Serialize(CommandResult result) => JsonSerializer.Serialize(result, JsonOptions);

    private static Dictionary<string, GovernanceRecord> WorkItems(IReadOnlyList<GovernanceRecord> records)
        => records.Where(item => item.Type.DefinitionName == "work-item" && item.Identifier.Length > 0).ToDictionary(item => item.Identifier, StringComparer.Ordinal);

    private static List<string> ReadinessReasons(GovernanceRecord workItem, Dictionary<string, GovernanceRecord> workItems, bool allowValidatedP0Prerequisites)
    {
        var failures = new List<string>();
        foreach (var prerequisite in workItem.StringList("prerequisites"))
        {
            if (!workItems.TryGetValue(prerequisite, out var prerequisiteRecord))
            {
                failures.Add($"prerequisite '{prerequisite}' has no work-item record");
                continue;
            }

            var state = prerequisiteRecord.Scalar("status") ?? "UNKNOWN";
            var p0LocalException = allowValidatedP0Prerequisites && StringComparer.Ordinal.Equals(workItem.Scalar("milestone_id"), "P0") && StringComparer.Ordinal.Equals(prerequisiteRecord.Scalar("milestone_id"), "P0") && StringComparer.Ordinal.Equals(state, "VALIDATION");
            if (!StringComparer.Ordinal.Equals(state, "DONE") && !p0LocalException)
            {
                failures.Add($"prerequisite '{prerequisite}' is {state}");
            }
        }

        failures.AddRange(workItem.StringList("blockers").Select(blocker => $"blocker: {blocker}"));
        return failures;
    }

    private static bool HasApprovedP0LocalIntegration(IReadOnlyList<GovernanceRecord> records)
        => records.Any(record => record.Type.DefinitionName == "product-decision" && StringComparer.Ordinal.Equals(record.Identifier, "DEC-0001") && StringComparer.Ordinal.Equals(record.Scalar("status"), "APPROVED"));

    private static List<GovernanceRecord> LoadRecords(DirectoryInfo repository, List<Diagnostic> diagnostics)
    {
        var records = new List<GovernanceRecord>();
        var governanceRoot = Path.Combine(repository.FullName, "governance");
        if (!Directory.Exists(governanceRoot))
        {
            diagnostics.Add(Diagnostic.Error(governanceRoot, null, "GOV_ROOT_MISSING", "Repository does not contain a governance directory."));
            return records;
        }

        foreach (var type in RecordTypes)
        {
            var directory = Path.Combine(governanceRoot, type.DirectoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories).OrderBy(item => item, StringComparer.Ordinal))
            {
                var mapping = ParseMapping(path, diagnostics);
                if (mapping is null)
                {
                    continue;
                }

                var fields = Fields(mapping);
                var identifier = Scalar(fields, type.IdentifierField) ?? string.Empty;
                if (identifier.Length == 0)
                {
                    diagnostics.Add(Diagnostic.Error(path, mapping.Start, "GOV_IDENTIFIER_MISSING", $"'{type.IdentifierField}' must be present and non-empty."));
                }

                records.Add(new GovernanceRecord(path, type, mapping, fields, identifier));
            }
        }

        return records;
    }

    private static YamlMappingNode? ParseMapping(string path, List<Diagnostic> diagnostics)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = File.OpenText(path);
            stream.Load(reader);
            if (stream.Documents.Count != 1)
            {
                diagnostics.Add(Diagnostic.Error(path, null, "GOV_DOCUMENT_COUNT", "A governance record must contain exactly one YAML document."));
                return null;
            }

            if (stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                diagnostics.Add(Diagnostic.Error(path, stream.Documents[0].RootNode.Start, "GOV_ROOT_MAPPING", "A governance record root must be a YAML mapping."));
                return null;
            }

            return mapping;
        }
        catch (YamlException exception)
        {
            diagnostics.Add(Diagnostic.Error(path, exception.Start, "GOV_YAML_PARSE", exception.Message));
            return null;
        }
        catch (IOException exception)
        {
            diagnostics.Add(Diagnostic.Error(path, null, "GOV_FILE_READ", exception.Message));
            return null;
        }
    }

    internal static IReadOnlyDictionary<string, YamlNode> Fields(YamlMappingNode mapping)
    {
        var fields = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is YamlScalarNode { Value: { } key })
            {
                fields[key] = pair.Value;
            }
        }

        return fields;
    }

    internal static string? Scalar(IReadOnlyDictionary<string, YamlNode> fields, string name)
        => fields.TryGetValue(name, out var node) && node is YamlScalarNode scalar ? scalar.Value : null;

    internal static IReadOnlyList<string> StringList(IReadOnlyDictionary<string, YamlNode> fields, string name)
    {
        if (!fields.TryGetValue(name, out var node) || node is not YamlSequenceNode sequence)
        {
            return [];
        }

        return sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray();
    }

    private static readonly string[] TerminalStates = ["DONE", "CANCELED", "SUPERSEDED", "DUPLICATE", "NOT_APPLICABLE"];
}

public sealed record CommandResult(int ExitCode, string Command, object Payload);
public sealed record Diagnostic(string File, long? Line, long? Column, string Severity, string Code, string Message)
{
    public static Diagnostic Error(string file, Mark? mark, string code, string message)
    {
        long? line = null;
        long? column = null;
        if (mark is { } location)
        {
            line = location.Line + 1;
            column = location.Column + 1;
        }

        return new(file, line, column, "ERROR", code, message);
    }
}

public sealed record ValidationPayload(bool IsValid, int RecordCount, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record MilestoneStatus(string Id, string Status);
public sealed record WorkItemStatus(string Id, string Status, IReadOnlyList<string> Prerequisites, IReadOnlyList<string> Blockers, IReadOnlyList<string> RequiredEvidence);
public sealed record StatusPayload(IReadOnlyList<MilestoneStatus> Milestones, IReadOnlyList<WorkItemStatus> WorkItems, object Validation);
public sealed record NextItem(string WorkItemId, string Status, bool Actionable, string Reason);
public sealed record NextPayload(IReadOnlyList<NextItem> Items, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record GatePayload(string WorkItemId, string Outcome, IReadOnlyList<string> Failures, IReadOnlyList<string> Satisfied);
public sealed record TransitionPayload(string WorkItemId, string From, string Destination, string Outcome, string Reason);

internal sealed record RecordTypeDefinition(string DirectoryName, string DefinitionName, string SchemaFileName, string SchemaDefinitionName, string IdentifierField);
internal sealed record GovernanceRecord(string Path, RecordTypeDefinition Type, YamlMappingNode Root, IReadOnlyDictionary<string, YamlNode> Fields, string Identifier)
{
    public string? Scalar(string name) => GovernanceApplication.Scalar(Fields, name);
    public IReadOnlyList<string> StringList(string name) => GovernanceApplication.StringList(Fields, name);
}

internal sealed record SchemaDefinition(IReadOnlyList<string> RequiredFields, IReadOnlyList<string> PermittedFields);

internal static class SchemaCatalog
{
    public static IReadOnlyDictionary<string, SchemaDefinition> Load(DirectoryInfo repository, ICollection<Diagnostic> diagnostics)
    {
        var root = Path.Combine(repository.FullName, "governance", "schemas", "v1");
        var corePath = Path.Combine(root, "governance-record.schema.json");
        if (!File.Exists(corePath))
        {
            diagnostics.Add(Diagnostic.Error(corePath, null, "GOV_SCHEMA_LIBRARY", "Shared governance schema library is missing."));
            return new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
        }

        try
        {
            var buildOptions = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
            _ = JsonSchema.FromText(File.ReadAllText(corePath), buildOptions);
            using var core = JsonDocument.Parse(File.ReadAllText(corePath));
            var definitions = core.RootElement.GetProperty("$defs");
            var result = new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
            foreach (var type in GovernanceApplication.RecordTypes)
            {
                var path = Path.Combine(root, type.SchemaFileName);
                if (!File.Exists(path))
                {
                    diagnostics.Add(Diagnostic.Error(path, null, "GOV_SCHEMA_ENTRY", "Entry schema is missing."));
                    continue;
                }

                _ = JsonSchema.FromText(File.ReadAllText(path), buildOptions);
                if (!definitions.TryGetProperty(type.SchemaDefinitionName, out var definition))
                {
                    diagnostics.Add(Diagnostic.Error(path, null, "GOV_SCHEMA_DEFINITION", $"Definition '{type.SchemaDefinitionName}' is missing."));
                    continue;
                }

                var required = definition.GetProperty("required").EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray();
                var permitted = definition.GetProperty("properties").EnumerateObject().Select(item => item.Name).ToArray();
                result[type.DefinitionName] = new SchemaDefinition(required, permitted);
            }

            return result;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Diagnostic.Error(corePath, null, "GOV_SCHEMA_PARSE", exception.Message));
            return new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add(Diagnostic.Error(corePath, null, "GOV_SCHEMA_BUILD", exception.Message));
            return new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
        }
    }
}

internal sealed record LifecycleDefinition(IReadOnlyList<string> NormalStates, IReadOnlyList<string> SideStates);

internal static class LifecyclePolicy
{
    public static LifecycleDefinition Load(DirectoryInfo repository, ICollection<Diagnostic> diagnostics)
    {
        var path = Path.Combine(repository.FullName, "governance", "policies", "lifecycle-policy.yaml");
        try
        {
            var stream = new YamlStream();
            using var reader = File.OpenText(path);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                diagnostics.Add(Diagnostic.Error(path, null, "GOV_LIFECYCLE_SHAPE", "Lifecycle policy must contain one mapping document."));
                return new LifecycleDefinition([], []);
            }

            var fields = GovernanceApplication.Fields(root);
            if (!fields.TryGetValue("work_item", out var workItemNode) || workItemNode is not YamlMappingNode workItem)
            {
                diagnostics.Add(Diagnostic.Error(path, root.Start, "GOV_LIFECYCLE_SHAPE", "Lifecycle policy must define work_item."));
                return new LifecycleDefinition([], []);
            }

            var workItemFields = GovernanceApplication.Fields(workItem);
            return new LifecycleDefinition(GovernanceApplication.StringList(workItemFields, "normal"), GovernanceApplication.StringList(workItemFields, "side"));
        }
        catch (YamlException exception)
        {
            diagnostics.Add(Diagnostic.Error(path, exception.Start, "GOV_LIFECYCLE_PARSE", exception.Message));
            return new LifecycleDefinition([], []);
        }
        catch (IOException exception)
        {
            diagnostics.Add(Diagnostic.Error(path, null, "GOV_LIFECYCLE_READ", exception.Message));
            return new LifecycleDefinition([], []);
        }
    }
}