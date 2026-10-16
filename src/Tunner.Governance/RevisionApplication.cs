using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

/// <summary>Post-closure work lineage in the existing repository governance control plane.</summary>
public static class RevisionApplication
{
    private static readonly string[] Terminal = ["CLOSED", "CANCELED", "SUPERSEDED"];
    private static readonly string[] IdentityFields = ["revision_id", "revision_scope", "original_scope_id", "parent_revision_id", "parent_milestone_revision_id", "created_at"];
    private static readonly Regex IdPattern = new(@"^[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-RV[0-9]{3,}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static CommandResult Create(DirectoryInfo repository, RevisionCreateRequest request)
    {
        var validation = (ValidationPayload)GovernanceApplication.Validate(repository).Payload;
        if (!validation.IsValid) return Result("revision create", "", "REJECTED", validation.Diagnostics.Select(x => x.Message));
        var records = GovernanceApplication.LoadRecords(repository, []);
        var original = ResolveOriginal(records, request.Scope);
        if (original is null) return Result("revision create", "", "REJECTED", ["original scope does not uniquely resolve"]);
        var requiredStatus = original.Type.DefinitionName == "milestone" ? "CLOSED" : "DONE";
        if (original.Scalar("status") != requiredStatus)
            return Result("revision create", "", "REJECTED", [$"original scope must be historically {requiredStatus}; active acceptance/release corrections retain REOPENED"]);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Reason))
            return Result("revision create", "", "REJECTED", ["title and reason are required"]);
        var authority = request.AuthorityRefs.Count == 0 ? original.StringList("authority_refs") : request.AuthorityRefs;
        var preflight = ReferenceFailures(repository, authority, request.DecisionRefs, [], [], records).ToList();
        preflight.AddRange(ImpactFailures(request.Impact, request.BlockedScope, records));
        if (request.ParentMilestoneRevisionId is { } parentId)
        {
            var parent = records.SingleOrDefault(x => x.Type.DefinitionName == "revision" && x.Identifier == parentId);
            var milestone = original.Type.DefinitionName == "milestone" ? original.Identifier : original.Scalar("milestone_id");
            if (parent is null || parent.Scalar("revision_scope") != "MILESTONE" || parent.Scalar("original_scope_id") != milestone || Terminal.Contains(parent.Scalar("status"), StringComparer.Ordinal))
                preflight.Add("parent milestone revision must be active lineage of the original milestone");
        }
        if (preflight.Count > 0) return Result("revision create", "", "REJECTED", preflight);
        var directory = Path.Combine(repository.FullName, "governance", "revisions");
        Directory.CreateDirectory(directory);
        try
        {
            using var allocationLock = new FileStream(Path.Combine(directory, ".allocation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            records = GovernanceApplication.LoadRecords(repository, []);
            var prior = Revisions(records).Where(x => x.Scalar("original_scope_id") == original.Identifier).OrderBy(Sequence).LastOrDefault();
            var stem = IdentityStem(original.Identifier);
            var sequence = NextSequence(repository, directory, stem + "-RV", false);
            var id = stem + "-RV" + sequence.ToString("D3", CultureInfo.InvariantCulture);
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schema_version"] = 1, ["revision_id"] = id,
                ["revision_scope"] = original.Type.DefinitionName == "milestone" ? "MILESTONE" : "WORK_ITEM",
                ["original_scope_id"] = original.Identifier, ["parent_revision_id"] = prior?.Identifier,
                ["parent_milestone_revision_id"] = request.ParentMilestoneRevisionId, ["title"] = request.Title,
                ["revision_type"] = request.Type, ["status"] = "PROPOSED", ["reason"] = request.Reason,
                ["authority_refs"] = authority, ["decision_refs"] = request.DecisionRefs,
                ["defect_refs"] = Array.Empty<string>(), ["change_refs"] = Array.Empty<string>(),
                ["impact_classification"] = request.Impact, ["blocked_scope"] = request.BlockedScope,
                ["acceptance_criteria"] = request.AcceptanceCriteria, ["prerequisites"] = Array.Empty<string>(),
                ["work_items"] = Array.Empty<string>(), ["required_gates"] = Array.Empty<string>(),
                ["required_roles"] = Array.Empty<string>(), ["evidence_refs"] = Array.Empty<string>(),
                ["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), ["closed_at"] = null
            };
            var candidate = FromFields(fields, Path.Combine(directory, id + ".yaml"));
            var failures = SchemaFailures(repository, candidate).Concat(RecordFailures(repository, candidate, records.Append(candidate).ToArray())).ToArray();
            if (failures.Length > 0) return Result("revision create", id, "REJECTED", failures);
            WriteNew(candidate.Path, JsonSerializer.Serialize(fields, Json));
            return Result("revision create", id, "CREATED", [], ["original record and evidence remain unchanged"]);
        }
        catch (IOException exception) { return Result("revision create", "", "REJECTED", [exception.Message]); }
    }

    public static CommandResult CreateChild(DirectoryInfo repository, string revisionId, string title)
    {
        var validation = (ValidationPayload)GovernanceApplication.Validate(repository).Payload;
        if (!validation.IsValid) return Result("revision child-create", revisionId, "REJECTED", validation.Diagnostics.Select(x => x.Message));
        var records = GovernanceApplication.LoadRecords(repository, []);
        var revision = Revisions(records).SingleOrDefault(x => x.Identifier == revisionId);
        if (revision is null || Terminal.Contains(revision.Scalar("status"), StringComparer.Ordinal) || string.IsNullOrWhiteSpace(title))
            return Result("revision child-create", revisionId, "REJECTED", ["an open revision and title are required"]);
        var directory = Path.Combine(repository.FullName, "governance", "work-items");
        try
        {
            using var allocationLock = new FileStream(Path.Combine(repository.FullName, "governance", "revisions", ".allocation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            records = GovernanceApplication.LoadRecords(repository, []);
            revision = Revisions(records).Single(x => x.Identifier == revisionId);
            var id = revisionId + "-" + NextSequence(repository, directory, revisionId + "-", true).ToString("D3", CultureInfo.InvariantCulture);
            var original = ResolveOriginal(records, revision.Scalar("original_scope_id") ?? string.Empty)!;
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schema_version"] = 1, ["work_item_id"] = id,
                ["milestone_id"] = original.Type.DefinitionName == "milestone" ? original.Identifier : original.Scalar("milestone_id"),
                ["revision_ref"] = revisionId, ["sprint_id"] = null, ["title"] = title, ["status"] = "DRAFT",
                ["type"] = original.Scalar("type") ?? "FEATURE", ["authority_refs"] = revision.StringList("authority_refs"),
                ["flow_refs"] = Array.Empty<string>(), ["contract_refs"] = Array.Empty<string>(),
                ["decision_refs"] = revision.StringList("decision_refs"), ["acceptance_criteria"] = Array.Empty<string>(),
                ["prerequisites"] = Array.Empty<string>(), ["blockers"] = Array.Empty<string>(),
                ["impact"] = new { architecture = false, ui = false, sdk = false, financial = false, compliance = false, security = false, devops = false, support = false },
                ["activated_roles"] = revision.StringList("required_roles"), ["required_tests"] = Array.Empty<string>(),
                ["required_evidence"] = Array.Empty<string>(), ["context_policy"] = "TASK",
                ["todos"] = Array.Empty<string>(), ["defects"] = Array.Empty<string>(),
                ["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["updated_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            WriteNew(Path.Combine(directory, id + ".yaml"), JsonSerializer.Serialize(fields, Json));
            var mapping = revision.Root;
            mapping.Children[new YamlScalarNode("work_items")] = new YamlSequenceNode(revision.StringList("work_items").Append(id).Select(x => new YamlScalarNode(x)));
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            new YamlStream(new YamlDocument(mapping)).Save(writer, false);
            File.WriteAllText(revision.Path, writer.ToString(), new UTF8Encoding(false));
            return Result("revision child-create", id, "CREATED", []);
        }
        catch (IOException exception) { return Result("revision child-create", revisionId, "REJECTED", [exception.Message]); }
    }

    public static CommandResult Show(DirectoryInfo repository, string id)
    {
        var validation = GovernanceApplication.Validate(repository);
        var validated = (ValidationPayload)validation.Payload;
        if (!validated.IsValid) return Result("revision show", id, "REJECTED", validated.Diagnostics.Select(x => x.Message));
        var records = GovernanceApplication.LoadRecords(repository, []);
        var revision = Revisions(records).SingleOrDefault(x => x.Identifier == id);
        if (revision is null) return Result("revision show", id, "NOT_FOUND", ["revision does not exist"]);
        var original = ResolveOriginal(records, revision.Scalar("original_scope_id") ?? string.Empty);
        return new(validation.ExitCode, "revision show", new
        {
            Revision = Status([revision]).Single(), Original = original is null ? null : new { original.Identifier, Status = original.Scalar("status"), HistoricalStatus = HistoricalStatus(repository, original), Evidence = original.StringList(original.Type.DefinitionName == "milestone" ? "evidence_refs" : "required_evidence") },
            Record = ToJson(revision.Root), NextAction = NextAction(revision), Validation = validation.Payload
        });
    }

    public static CommandResult CheckTransition(DirectoryInfo repository, string id, string destination)
    {
        var validation = (ValidationPayload)GovernanceApplication.Validate(repository).Payload;
        if (!validation.IsValid) return Result("revision transition check", id, "REJECTED", validation.Diagnostics.Select(x => x.Message));
        var records = GovernanceApplication.LoadRecords(repository, []);
        var revision = Revisions(records).SingleOrDefault(x => x.Identifier == id);
        if (revision is null) return Result("revision transition check", id, "NOT_FOUND", ["revision does not exist"]);
        var normal = PolicyValues(repository, "normal_states");
        var from = revision.Scalar("status") ?? string.Empty;
        var allowed = Array.IndexOf(normal, destination) == Array.IndexOf(normal, from) + 1 && Array.IndexOf(normal, from) >= 0;
        if (from == "VALIDATION" && destination == "CLOSED" && revision.Scalar("product_acceptance_required") == "false") allowed = true;
        var failures = new List<string>();
        if (!allowed) failures.Add("only the next declared normal Revision state is allowed; terminal history cannot be reopened");
        if (destination == "ACTIVE") failures.AddRange(Readiness(repository, revision, records));
        if (destination == "CLOSED") failures.AddRange(((RevisionOperationPayload)CloseCheck(repository, id).Payload).Findings);
        return Result("revision transition check", id, failures.Count == 0 ? "ALLOWED" : "REJECTED", failures);
    }

    public static CommandResult CloseCheck(DirectoryInfo repository, string id)
    {
        var validation = (ValidationPayload)GovernanceApplication.Validate(repository).Payload;
        if (!validation.IsValid) return Result("revision close-check", id, "NOT_READY", validation.Diagnostics.Select(x => x.Message));
        var records = GovernanceApplication.LoadRecords(repository, []);
        var revision = Revisions(records).SingleOrDefault(x => x.Identifier == id);
        if (revision is null) return Result("revision close-check", id, "NOT_FOUND", ["revision does not exist"]);
        var failures = validation.Diagnostics.Select(x => x.Message).ToList();
        failures.AddRange(Readiness(repository, revision, records));
        if (!new[] { "VALIDATION", "PRODUCT_ACCEPTANCE", "CLOSED" }.Contains(revision.Scalar("status"), StringComparer.Ordinal))
            failures.Add("Revision must reach validation/acceptance before closure");
        if (revision.Scalar("product_acceptance_required") != "false" && revision.Scalar("status") == "VALIDATION")
            failures.Add("Product Acceptance remains required unless explicitly declared otherwise");
        if (revision.StringList("evidence_refs").Count == 0) failures.Add("closure requires new Revision evidence");
        foreach (var evidence in revision.StringList("evidence_refs"))
            if (!SafeFile(repository, evidence)) failures.Add($"required Revision evidence missing: {evidence}");
        if (CommitReadinessApplication.Read(revision)?.RepositoryChanged != true) failures.Add("repository-native Revision closure requires repository-changing committed-state lineage");
        failures.AddRange(CommitReadinessApplication.Check(repository, revision));
        var committedState = CommitReadinessApplication.Read(revision);
        if (committedState?.RepositoryChanged == true && committedState.ValidatedCommitSha is { } sha && Parse(Git(repository, "show", sha + ":" + RepositoryRootResolver.ToLogicalPath(repository, revision.Path))) is null)
            failures.Add("validated SHA must contain this Revision record");
        foreach (var workId in revision.StringList("work_items"))
        {
            var childRevision = Revisions(records).SingleOrDefault(x => x.Identifier == workId && x.Scalar("parent_milestone_revision_id") == id);
            if (childRevision is not null)
            {
                if (childRevision.Scalar("status") != "CLOSED") failures.Add($"child Revision is not CLOSED: {workId}");
                continue;
            }
            var work = records.SingleOrDefault(x => x.Type.DefinitionName == "work-item" && x.Identifier == workId);
            if (work is null || work.Scalar("revision_ref") != id || work.Scalar("status") != "DONE") failures.Add($"Revision child is not complete and linked: {workId}");
            else failures.AddRange(((GatePayload)GovernanceApplication.CheckGate(repository, workId).Payload).Failures.Select(x => $"{workId}: {x}"));
        }
        foreach (var gateId in revision.StringList("required_gates"))
        {
            var gate = records.SingleOrDefault(x => x.Type.DefinitionName == "gate" && x.Identifier == gateId);
            if (gate is null || gate.Scalar("revision_ref") != id || gate.Scalar("status") != "PASS" || gate.StringList("blocking_findings").Count != 0)
                failures.Add($"Revision gate did not pass: {gateId}");
            else foreach (var evidence in gate.StringList("required_evidence"))
                if (!SafeFile(repository, evidence)) failures.Add($"Revision gate evidence missing: {evidence}");
        }
        foreach (var role in revision.StringList("required_roles"))
        {
            var reviews = records.Where(x => x.Type.DefinitionName == "specialist-review" && x.Scalar("work_item_id") == id && x.Scalar("role") == role).ToArray();
            if (reviews.Length != 1 || reviews[0].Scalar("result") != "PASS" || reviews[0].Scalar("status") != "PASS" || reviews[0].StringList("blocking_findings").Count > 0 || reviews[0].StringList("evidence_refs").Count == 0 || reviews[0].StringList("evidence_refs").Any(x => !SafeFile(repository, x)))
                failures.Add($"Revision role review missing, failed, or lacking evidence: {role}");
        }
        foreach (var defect in revision.StringList("defect_refs"))
        {
            var record = ResolveReference(records, defect, "defect");
            if (record is not null && !new[] { "CLOSED", "RESOLVED", "SUPERSEDED", "NOT_APPLICABLE" }.Contains(record.Scalar("status"), StringComparer.Ordinal))
                failures.Add($"linked defect remains open: {defect}");
        }
        return Result("revision close-check", id, failures.Count == 0 ? "PASS" : "NOT_READY", failures);
    }

    internal static void AddDiagnostics(DirectoryInfo repository, IReadOnlyList<GovernanceRecord> records, List<Diagnostic> diagnostics)
    {
        foreach (var revision in Revisions(records))
        {
            try
            {
                foreach (var failure in SchemaFailures(repository, revision).Concat(RecordFailures(repository, revision, records)))
                    diagnostics.Add(Diagnostic.Error(revision.Path, revision.Root.Start, "GOV_REVISION_INVALID", failure));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
            {
                diagnostics.Add(Diagnostic.Error(revision.Path, revision.Root.Start, "GOV_REVISION_INVALID", exception.Message));
            }
        }
        foreach (var linked in records.Where(x => x.Type.DefinitionName is "work-item" or "gate"))
        {
            var reference = NullValue(linked.Scalar("revision_ref"));
            if (reference is not null && !Revisions(records).Any(x => x.Identifier == reference))
                diagnostics.Add(Diagnostic.Error(linked.Path, linked.Root.Start, "GOV_REVISION_LINK", $"revision_ref does not resolve: {reference}"));
            if (Regex.IsMatch(linked.Identifier, @"-RV[0-9]+-", RegexOptions.CultureInvariant) && linked.Type.DefinitionName == "work-item" && (reference is null || !Regex.IsMatch(linked.Identifier, "^" + Regex.Escape(reference) + @"-[0-9]{3,}$", RegexOptions.CultureInvariant) || !BigInteger.TryParse(linked.Identifier[(linked.Identifier.LastIndexOf('-') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var childSequence) || childSequence <= 0))
                diagnostics.Add(Diagnostic.Error(linked.Path, linked.Root.Start, "GOV_REVISION_CHILD_ID", "Revision child identity must be <revision_id>-<positive sequence> and linked to its owner"));
        }
        foreach (var milestone in records.Where(x => x.Type.DefinitionName == "milestone"))
            foreach (var field in new[] { "active_revision_refs", "closed_revision_refs" })
                foreach (var id in milestone.StringList(field))
                    if (!Revisions(records).Any(x => x.Identifier == id && x.Scalar("original_scope_id") == milestone.Identifier && (field == "closed_revision_refs" ? x.Scalar("status") == "CLOSED" : !Terminal.Contains(x.Scalar("status"), StringComparer.Ordinal))))
                        diagnostics.Add(Diagnostic.Error(milestone.Path, milestone.Root.Start, "GOV_REVISION_LINK", $"invalid {field}: {id}"));
        foreach (var trackedPath in Git(repository, "ls-tree", "-r", "--name-only", "HEAD", "--", "governance/revisions").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(x => x.EndsWith(".yaml", StringComparison.Ordinal)))
            if (!File.Exists(Path.Combine(repository.FullName, trackedPath)))
                diagnostics.Add(Diagnostic.Error(trackedPath, null, "GOV_REVISION_HISTORY_DELETED", "committed Revision history cannot be deleted"));
    }

    internal static IReadOnlyList<RevisionStatus> Status(IReadOnlyList<GovernanceRecord> records)
        => Revisions(records).OrderBy(x => x.Scalar("original_scope_id"), StringComparer.Ordinal).ThenBy(Sequence).Select(x => new RevisionStatus(x.Identifier, x.Scalar("original_scope_id") ?? "", x.Scalar("revision_scope") ?? "", x.Scalar("status") ?? "", x.Scalar("revision_type") ?? "", x.Scalar("reason") ?? "", x.Scalar("impact_classification") ?? "", x.StringList("blocked_scope"), NullValue(x.Scalar("parent_revision_id")), NullValue(x.Scalar("parent_milestone_revision_id")), NextAction(x))).ToArray();

    internal static IReadOnlyList<HistoricalScopeStatus> HistoricalScopes(DirectoryInfo repository, IReadOnlyList<GovernanceRecord> records)
        => records.Where(x => x.Type.DefinitionName == "milestone" || x.Type.DefinitionName == "work-item" && Revisions(records).Any(r => r.Scalar("original_scope_id") == x.Identifier)).OrderBy(x => x.Identifier, StringComparer.Ordinal)
            .Select(x => new HistoricalScopeStatus(x.Identifier, HistoricalStatus(repository, x), x.Scalar("status") ?? "",
                Revisions(records).Where(r => r.Scalar("original_scope_id") == x.Identifier && !Terminal.Contains(r.Scalar("status"), StringComparer.Ordinal)).Select(r => r.Identifier).ToArray(),
                Revisions(records).Where(r => r.Scalar("original_scope_id") == x.Identifier && r.Scalar("status") == "CLOSED").Select(r => r.Identifier).ToArray())).ToArray();

    internal static IEnumerable<NextItem> Next(DirectoryInfo repository, IReadOnlyList<GovernanceRecord> records, bool valid)
    {
        foreach (var revision in Revisions(records).OrderBy(x => x.Scalar("original_scope_id"), StringComparer.Ordinal).ThenBy(Sequence).Where(x => !Terminal.Contains(x.Scalar("status"), StringComparer.Ordinal)))
        {
            var reasons = Readiness(repository, revision, records).ToList();
            reasons.AddRange(BlockingReasons(records, revision));
            if (revision.Scalar("status") == "PROPOSED") reasons.Add("Revision approval is required before activation");
            if (revision.Scalar("status") == "BLOCKED") reasons.Add("Revision is explicitly BLOCKED");
            yield return new(revision.Identifier, revision.Scalar("status") ?? "", valid && reasons.Count == 0, reasons.Count == 0 ? NextAction(revision) : string.Join("; ", reasons));
        }
    }

    internal static IReadOnlyList<string> BlockingReasons(IReadOnlyList<GovernanceRecord> records, GovernanceRecord work)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal) { work.Identifier, work.Scalar("milestone_id") ?? "", work.Scalar("original_scope_id") ?? "" };
        var queue = new Queue<string>(work.StringList("prerequisites").Concat(records.Where(x => x.Type.DefinitionName == "dependency" && x.Scalar("work_item_id") == work.Identifier).Select(x => x.Scalar("prerequisite_work_item_id")).OfType<string>()));
        while (queue.TryDequeue(out var id))
        {
            if (!affected.Add(id)) continue;
            var prerequisite = records.FirstOrDefault(x => x.Identifier == id);
            if (prerequisite is null) continue;
            if (prerequisite.Scalar("milestone_id") is { } milestone) affected.Add(milestone);
            foreach (var next in prerequisite.StringList("prerequisites").Concat(records.Where(x => x.Type.DefinitionName == "dependency" && x.Scalar("work_item_id") == id).Select(x => x.Scalar("prerequisite_work_item_id")).OfType<string>())) queue.Enqueue(next);
        }
        var owner = records.FirstOrDefault(x => x.Identifier == work.Scalar("revision_ref"));
        var exemptions = new[] { work.Identifier, work.Scalar("revision_ref"), owner?.Scalar("parent_milestone_revision_id"), work.Scalar("parent_milestone_revision_id") };
        return Revisions(records).Where(x => !Terminal.Contains(x.Scalar("status"), StringComparer.Ordinal) && x.Scalar("impact_classification") != "NON_BLOCKING" && !exemptions.Contains(x.Identifier, StringComparer.Ordinal) && x.StringList("blocked_scope").Any(affected.Contains))
            .Select(x => $"Revision {x.Identifier} blocks declared affected scope ({x.Scalar("impact_classification")})").ToArray();
    }

    internal static IEnumerable<GovernanceRecord> Revisions(IReadOnlyList<GovernanceRecord> records) => records.Where(x => x.Type.DefinitionName == "revision");
    internal static string NextAction(GovernanceRecord revision) => revision.Scalar("status") switch
    {
        "PROPOSED" => "review and approve the scoped Revision",
        "APPROVED" => "check Revision activation eligibility",
        "ACTIVE" => "select eligible Revision children, implement, and validate the committed scope",
        "VALIDATION" => "collect passing gates, role reviews, committed evidence, and required acceptance",
        "PRODUCT_ACCEPTANCE" => "run revision close-check after required acceptance",
        "CLOSED" => "preserve closure and run governance next",
        _ => "resolve the recorded scoped Revision condition"
    };
    internal static string HistoricalStatus(DirectoryInfo repository, GovernanceRecord original)
    {
        var previous = Parse(Git(repository, "show", "HEAD:" + RepositoryRootResolver.ToLogicalPath(repository, original.Path)));
        return previous is null ? original.Scalar("status") ?? "UNKNOWN" : GovernanceApplication.Scalar(GovernanceApplication.Fields(previous), "status") ?? "UNKNOWN";
    }

    private static List<string> Readiness(DirectoryInfo repository, GovernanceRecord revision, IReadOnlyList<GovernanceRecord> records)
    {
        var failures = RecordFailures(repository, revision, records).ToList();
        foreach (var id in revision.StringList("prerequisites"))
        {
            var prerequisite = records.SingleOrDefault(x => x.Identifier == id);
            if (prerequisite is null || !new[] { "CLOSED", "DONE", "CANCELED", "SUPERSEDED" }.Contains(prerequisite.Scalar("status"), StringComparer.Ordinal))
                failures.Add($"Revision declared prerequisite is incomplete: {id}");
        }
        if (revision.StringList("acceptance_criteria").Count == 0) failures.Add("Revision acceptance criteria are required before activation");
        return failures;
    }

    private static List<string> RecordFailures(DirectoryInfo repository, GovernanceRecord revision, IReadOnlyList<GovernanceRecord> records)
    {
        var failures = new List<string>();
        var original = ResolveOriginal(records, revision.Scalar("original_scope_id") ?? "");
        if (original is null) failures.Add("Revision original scope does not resolve uniquely");
        else
        {
            var scope = original.Type.DefinitionName == "milestone" ? "MILESTONE" : "WORK_ITEM";
            if (revision.Scalar("revision_scope") != scope) failures.Add("Revision scope type disagrees with original scope");
            var expectedStem = IdentityStem(original.Identifier) + "-RV";
            if (!IdPattern.IsMatch(revision.Identifier) || !revision.Identifier.StartsWith(expectedStem, StringComparison.Ordinal) || Sequence(revision) <= 0)
                failures.Add("Revision identity does not match original scope and positive monotonic sequence");
            if (original.Scalar("status") != (scope == "MILESTONE" ? "CLOSED" : "DONE"))
                failures.Add("Revision original scope must preserve its historical CLOSED/DONE status");
        }
        if (Path.GetFileNameWithoutExtension(revision.Path) != revision.Identifier) failures.Add("Revision filename must match immutable identity");
        if (!PolicyValues(repository, "normal_states").Concat(PolicyValues(repository, "side_states")).Contains(revision.Scalar("status"), StringComparer.Ordinal))
            failures.Add("Revision status is not declared by the active lifecycle policy");
        if (!PolicyValues(repository, "types").Contains(revision.Scalar("revision_type"), StringComparer.Ordinal)) failures.Add("Revision type is not approved by policy");
        failures.AddRange(ReferenceFailures(repository, revision.StringList("authority_refs"), revision.StringList("decision_refs"), revision.StringList("defect_refs"), revision.StringList("change_refs"), records));
        failures.AddRange(ImpactFailures(revision.Scalar("impact_classification") ?? "", revision.StringList("blocked_scope"), records));
        var prior = Revisions(records).Where(x => x.Identifier != revision.Identifier && x.Scalar("original_scope_id") == revision.Scalar("original_scope_id") && Sequence(x) < Sequence(revision)).OrderBy(Sequence).LastOrDefault();
        if (NullValue(revision.Scalar("parent_revision_id")) != prior?.Identifier) failures.Add("previous Revision must link the immediate earlier lineage");
        var parentId = NullValue(revision.Scalar("parent_milestone_revision_id"));
        if (parentId is not null)
        {
            var parent = Revisions(records).SingleOrDefault(x => x.Identifier == parentId);
            var milestone = original?.Type.DefinitionName == "milestone" ? original.Identifier : original?.Scalar("milestone_id");
            if (parent is null || parent.Scalar("revision_scope") != "MILESTONE" || parent.Scalar("original_scope_id") != milestone || parent.Identifier == revision.Identifier)
                failures.Add("parent milestone Revision must resolve to the original milestone lineage");
        }
        foreach (var childId in revision.StringList("work_items"))
            if (!records.Any(x => x.Identifier == childId && (x.Type.DefinitionName == "work-item" && x.Scalar("revision_ref") == revision.Identifier || x.Type.DefinitionName == "revision" && x.Scalar("parent_milestone_revision_id") == revision.Identifier)))
                failures.Add($"Revision child does not link back to its owner: {childId}");
        foreach (var time in new[] { "created_at", "closed_at" })
            if (NullValue(revision.Scalar(time)) is { } value && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                failures.Add($"Revision {time} must be an ISO timestamp");
        if (NullValue(revision.Scalar("created_at")) is null) failures.Add("Revision creation timestamp is required");
        if ((revision.Scalar("status") == "CLOSED") != (NullValue(revision.Scalar("closed_at")) is not null)) failures.Add("closed_at is required exactly when Revision is CLOSED");
        var old = Parse(Git(repository, "show", "HEAD:" + RepositoryRootResolver.ToLogicalPath(repository, revision.Path)));
        if (old is not null)
        {
            var fields = GovernanceApplication.Fields(old);
            foreach (var field in IdentityFields)
                if (NullValue(GovernanceApplication.Scalar(fields, field)) != NullValue(revision.Scalar(field))) failures.Add($"committed Revision identity field is immutable: {field}");
            if (GovernanceApplication.Scalar(fields, "status") == "CLOSED" && JsonSerializer.Serialize(ToJson(old)) != JsonSerializer.Serialize(ToJson(revision.Root)))
                failures.Add("closed Revision history is immutable; later work requires a new Revision");
        }
        return failures;
    }

    private static IEnumerable<string> SchemaFailures(DirectoryInfo repository, GovernanceRecord record)
    {
        try
        {
            using var library = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, "governance", "schemas", "v1", "governance-record.schema.json")));
            var schema = JsonSchema.FromText("{\"$defs\":" + library.RootElement.GetProperty("$defs").GetRawText() + ",\"$ref\":\"#/$defs/revision\"}");
            return schema.Evaluate(JsonSerializer.SerializeToElement(ToJson(record.Root))).IsValid ? [] : ["Revision fails executable JSON Schema field/type/enum validation"];
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        { return [$"Revision schema evaluation failed: {exception.Message}"]; }
    }

    private static List<string> ReferenceFailures(DirectoryInfo repository, IReadOnlyList<string> authority, IReadOnlyList<string> decisions, IReadOnlyList<string> defects, IReadOnlyList<string> changes, IReadOnlyList<GovernanceRecord> records)
    {
        var failures = new List<string>();
        var verified = new HashSet<string>(StringComparer.Ordinal);
        using var integrity = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, "governance", "evidence", "BOOT-P0-001-integrity.json")));
        foreach (var group in new[] { "checks", "amendment_checks" })
            if (integrity.RootElement.TryGetProperty(group, out var checks))
                foreach (var check in checks.EnumerateArray())
                    if (check.TryGetProperty("local_path", out var path)) verified.Add(path.GetString() ?? "");
        if (authority.Count == 0) failures.Add("Revision must reference approved existing authority");
        foreach (var path in authority)
            if (!path.StartsWith("docs/authority/", StringComparison.Ordinal) || !SafeFile(repository, path) || !verified.Contains(path))
                failures.Add($"Revision authority reference is not in the byte-verified mirror: {path}");
        foreach (var reference in decisions)
        {
            var decision = ResolveReference(records, reference, "product-decision");
            if (decision?.Scalar("status") != "APPROVED" && !ApprovedMirroredDecision(repository, reference, verified))
                failures.Add($"Revision decision must resolve to approved authority: {reference}");
        }
        foreach (var reference in defects)
            if (ResolveReference(records, reference, "defect") is null) failures.Add($"Revision defect reference does not resolve: {reference}");
        foreach (var reference in changes)
            if (!SafeFile(repository, reference)) failures.Add($"Revision change reference does not resolve to a repository record: {reference}");
        return failures;
    }

    private static bool ApprovedMirroredDecision(DirectoryInfo repository, string reference, HashSet<string> verified)
    {
        var matches = verified.Where(path => path.StartsWith("docs/authority/", StringComparison.Ordinal) &&
            (path == reference || Path.GetFileName(path).StartsWith(reference + "_", StringComparison.Ordinal)) &&
            Regex.IsMatch(Path.GetFileName(path), @"^(ADR|PDR)-[0-9]+_", RegexOptions.CultureInvariant)).ToArray();
        return matches.Length > 0 && matches.All(path => SafeFile(repository, path) &&
            Regex.IsMatch(File.ReadAllText(Path.Combine(repository.FullName, path)).Replace("*", "", StringComparison.Ordinal),
                @"(?im)^Status:\s*(ACCEPTED|APPROVED)\b", RegexOptions.CultureInvariant));
    }

    private static List<string> ImpactFailures(string impact, IReadOnlyList<string> blockedScope, IReadOnlyList<GovernanceRecord> records)
    {
        var failures = new List<string>();
        if (!new[] { "NON_BLOCKING", "SCOPED_BLOCKING", "RELEASE_BLOCKING", "SECURITY_BLOCKING" }.Contains(impact, StringComparer.Ordinal)) failures.Add("unsupported Revision impact");
        if (impact == "NON_BLOCKING" && blockedScope.Count != 0) failures.Add("NON_BLOCKING Revision cannot declare blocked scope");
        if (impact != "NON_BLOCKING" && blockedScope.Count == 0) failures.Add("blocking Revision must declare bounded affected scope");
        foreach (var id in blockedScope)
            if (!records.Any(x => x.Identifier == id && x.Type.DefinitionName is "milestone" or "work-item" or "revision" or "release")) failures.Add($"blocked scope does not resolve: {id}");
        return failures;
    }

    private static GovernanceRecord? ResolveOriginal(IReadOnlyList<GovernanceRecord> records, string id)
    {
        var matches = records.Where(x => x.Type.DefinitionName is "milestone" or "work-item" && (x.Identifier == id || IdentityStem(x.Identifier) == id) && !x.Identifier.Contains("-RV", StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static GovernanceRecord? ResolveReference(IReadOnlyList<GovernanceRecord> records, string id, string type)
        => records.SingleOrDefault(x => x.Type.DefinitionName == type && (x.Identifier == id || x.Path.Replace('\\', '/').EndsWith("/" + id, StringComparison.Ordinal)));
    private static string IdentityStem(string id) => id.StartsWith("TUN-", StringComparison.Ordinal) ? id[4..] : id;
    private static BigInteger Sequence(GovernanceRecord revision)
        => BigInteger.TryParse(revision.Identifier[(revision.Identifier.LastIndexOf("-RV", StringComparison.Ordinal) + 3)..], NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : BigInteger.Zero;
    private static BigInteger NextSequence(DirectoryInfo repository, string directory, string prefix, bool child)
    {
        var logical = RepositoryRootResolver.ToLogicalPath(repository, directory);
        var names = Directory.EnumerateFiles(directory, "*.yaml").Select(Path.GetFileNameWithoutExtension)
            .Concat(Git(repository, "log", "--all", "--format=", "--name-only", "--", logical).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFileNameWithoutExtension));
        var maximum = BigInteger.Zero;
        foreach (var name in names.OfType<string>().Where(x => x.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var suffix = name[prefix.Length..];
            if ((child || !suffix.Contains('-', StringComparison.Ordinal)) && BigInteger.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) maximum = BigInteger.Max(maximum, sequence);
        }
        return maximum + BigInteger.One;
    }
    private static string[] PolicyValues(DirectoryInfo repository, string field)
    {
        var mapping = Parse(File.ReadAllText(Path.Combine(repository.FullName, "governance", "policies", "lifecycle-policy.yaml")));
        if (mapping is null || !GovernanceApplication.Fields(mapping).TryGetValue("revision", out var node) || node is not YamlMappingNode revision) return [];
        return GovernanceApplication.StringList(GovernanceApplication.Fields(revision), field).ToArray();
    }
    private static GovernanceRecord FromFields(Dictionary<string, object?> fields, string path)
    {
        var mapping = Parse(JsonSerializer.Serialize(fields))!;
        var type = GovernanceApplication.RecordTypes.Single(x => x.DefinitionName == "revision");
        return new(path, type, mapping, GovernanceApplication.Fields(mapping), (string)fields["revision_id"]!);
    }
    private static YamlMappingNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { var stream = new YamlStream(); stream.Load(new StringReader(text)); return stream.Documents.Count == 1 ? stream.Documents[0].RootNode as YamlMappingNode : null; }
        catch (YamlException) { return null; }
    }
    internal static object? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode mapping => mapping.Children.ToDictionary(x => ((YamlScalarNode)x.Key).Value ?? "", x => ToJson(x.Value), StringComparer.Ordinal),
        YamlSequenceNode sequence => sequence.Children.Select(ToJson).ToArray(),
        YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && NullValue(scalar.Value) is null => null,
        YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && bool.TryParse(scalar.Value, out var flag) => flag,
        YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        YamlScalarNode scalar => scalar.Value,
        _ => null
    };
    private static string? NullValue(string? value) => string.IsNullOrWhiteSpace(value) || value is "null" or "~" ? null : value;
    private static bool SafeFile(DirectoryInfo repository, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal)) return false;
        try { var full = Path.GetFullPath(Path.Combine(repository.FullName, path)); return RepositoryRootResolver.ToLogicalPath(repository, full) == path && File.Exists(full); }
        catch (InvalidOperationException) { return false; }
    }
    private static void WriteNew(string path, string content)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file, new UTF8Encoding(false)); writer.WriteLine(content);
    }
    internal static string Git(DirectoryInfo repository, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return "";
            var text = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit();
            return process.ExitCode == 0 ? text.Trim() : "";
        }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
    private static CommandResult Result(string command, string id, string outcome, IEnumerable<string> findings, IReadOnlyList<string>? satisfied = null)
    {
        var failures = findings.Distinct(StringComparer.Ordinal).ToArray();
        return new(outcome is "CREATED" or "PASS" or "ALLOWED" ? 0 : 1, command, new RevisionOperationPayload(id, outcome, failures, satisfied ?? []));
    }
}
public sealed record RevisionCreateRequest(string Scope, string Type, string Title, string Reason, string Impact, IReadOnlyList<string> BlockedScope, IReadOnlyList<string> AuthorityRefs, IReadOnlyList<string> DecisionRefs, IReadOnlyList<string> AcceptanceCriteria, string? ParentMilestoneRevisionId = null);
public sealed record RevisionOperationPayload(string RevisionId, string Outcome, IReadOnlyList<string> Findings, IReadOnlyList<string> Satisfied);
public sealed record RevisionStatus(string Id, string OriginalScopeId, string Scope, string Status, string Type, string Reason, string Impact, IReadOnlyList<string> BlockedScope, string? PreviousRevision, string? ParentMilestoneRevision, string NextAction);
public sealed record HistoricalScopeStatus(string Id, string HistoricalStatus, string WorkingRecordStatus, IReadOnlyList<string> ActiveRevisionRefs, IReadOnlyList<string> ClosedRevisionRefs);
