using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

/// <summary>
/// Calculates mandatory specialist roles from the repository policy and validates
/// the structured, scope-local review records that satisfy the role gate.
/// </summary>
public static class RoleActivationApplication
{
    public static CommandResult Calculate(DirectoryInfo repository, string workItemId)
    {
        var diagnostics = new List<Diagnostic>();
        var records = GovernanceApplication.LoadRecords(repository, diagnostics);
        var workItems = GovernanceApplication.WorkItems(records);
        if (!workItems.TryGetValue(workItemId, out var workItem))
        {
            return new CommandResult(2, "roles calculate", new RoleCalculationPayload(workItemId, [], [], [], [], ["work item does not exist"]));
        }

        var calculation = Calculate(repository, workItem);
        var findings = calculation.Findings
            .Concat(calculation.MissingDeclaredRoles.Select(role => $"mandatory role is not activated: {role}"))
            .Concat(diagnostics.Where(item => item.Severity == "ERROR").Select(item => item.Code))
            .ToArray();
        return new CommandResult(findings.Length == 0 ? 0 : 1, "roles calculate", new RoleCalculationPayload(workItemId, calculation.RequiredRoles, workItem.StringList("activated_roles"), calculation.ImpactClassifications, calculation.MissingDeclaredRoles, findings));
    }

    internal static RoleReviewCheck CheckReviews(DirectoryInfo repository, GovernanceRecord workItem, IReadOnlyList<GovernanceRecord>? knownRecords)
    {
        var policy = RoleActivationPolicy.Load(repository);
        if (policy.Findings.Count > 0)
        {
            return new RoleReviewCheck(policy.Findings, []);
        }

        if (!policy.IsEnforced(workItem))
        {
            return new RoleReviewCheck([], ["role matrix enforcement is not retroactive for this legacy work-item record"]);
        }

        var calculation = Calculate(policy, workItem);
        var failures = new List<string>(calculation.Findings);
        failures.AddRange(calculation.MissingDeclaredRoles.Select(role => $"mandatory role is not activated: {role}"));
        var satisfied = new List<string>();
        if (failures.Count > 0)
        {
            return new RoleReviewCheck(failures, satisfied);
        }

        var records = knownRecords;
        if (records is null)
        {
            var diagnostics = new List<Diagnostic>();
            records = GovernanceApplication.LoadRecords(repository, diagnostics);
            failures.AddRange(diagnostics.Where(item => item.Severity == "ERROR").Select(item => $"record parse issue: {item.Code}"));
        }

        var formalReviews = records
            .Where(item => item.Type.DefinitionName == "specialist-review" && StringComparer.Ordinal.Equals(item.Scalar("work_item_id"), workItem.Identifier))
            .GroupBy(item => item.Scalar("role") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var role in calculation.RequiredRoles)
        {
            if (!formalReviews.TryGetValue(role, out var roleReviews) || roleReviews.Length == 0)
            {
                failures.Add($"mandatory role review is missing: {role}");
                continue;
            }

            if (roleReviews.Length != 1)
            {
                failures.Add($"mandatory role review is ambiguous: {role}");
                continue;
            }

            var review = roleReviews[0];
            if (!StringComparer.Ordinal.Equals(review.Scalar("skill_id"), role) || !StringComparer.Ordinal.Equals(review.Scalar("result"), "PASS") || !StringComparer.Ordinal.Equals(review.Scalar("status"), "PASS"))
            {
                failures.Add($"mandatory role review did not pass: {role}");
                continue;
            }

            if (review.StringList("authority_refs").Count == 0 || review.StringList("context_artifacts").Count == 0 || review.StringList("evidence_refs").Count == 0)
            {
                failures.Add($"mandatory role review lacks traceability fields: {role}");
                continue;
            }

            if (review.StringList("blocking_findings").Count > 0 || StringComparer.Ordinal.Equals(review.Scalar("context_expansion_requested"), "true"))
            {
                failures.Add($"mandatory role review blocks this scope: {role}");
                continue;
            }

            satisfied.Add($"mandatory role review passed: {role}");
        }

        return new RoleReviewCheck(failures.Distinct(StringComparer.Ordinal).ToArray(), satisfied);
    }

    private static RoleCalculation Calculate(DirectoryInfo repository, GovernanceRecord workItem)
    {
        var policy = RoleActivationPolicy.Load(repository);
        return policy.Findings.Count > 0 ? new RoleCalculation([], [], [], policy.Findings) : Calculate(policy, workItem);
    }

    private static RoleCalculation Calculate(RoleActivationPolicy policy, GovernanceRecord workItem)
    {
        var impact = ImpactValues(workItem);
        var matched = policy.Rules.Where(rule => Applies(rule.When, workItem, impact)).ToArray();
        var required = matched.SelectMany(rule => rule.Required).Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToArray();
        var declared = workItem.StringList("activated_roles");
        var missing = required.Where(role => !declared.Contains(role, StringComparer.Ordinal)).ToArray();
        return new RoleCalculation(required, matched.Select(rule => rule.When).OrderBy(item => item, StringComparer.Ordinal).ToArray(), missing, []);
    }

    private static bool Applies(string when, GovernanceRecord workItem, IReadOnlyDictionary<string, bool> impact) => when switch
    {
        "every_implementation" => !StringComparer.Ordinal.Equals(workItem.Scalar("type"), "DOCUMENTATION"),
        "architecture_or_module_or_integration_change" => IsTrue(impact, "architecture") || IsTrue(impact, "data_schema") || IsTrue(impact, "migration"),
        "user_or_admin_ui_change" => IsTrue(impact, "ui") || IsTrue(impact, "content"),
        "business_requirement_or_cross_domain_acceptance_change" => StringComparer.Ordinal.Equals(workItem.Scalar("type"), "BUSINESS_REQUIREMENT"),
        "product_api_sdk_event_webhook_change" => IsTrue(impact, "api") || IsTrue(impact, "sdk"),
        "financial_impact" => IsTrue(impact, "financial"),
        "compliance_or_privacy_or_regulated_impact" => IsTrue(impact, "compliance") || IsTrue(impact, "privacy"),
        "authentication_authorization_secrets_crypto_sensitive_data_supply_chain" => IsTrue(impact, "security"),
        "ci_cd_container_environment_observability_deployment_iac" => IsTrue(impact, "devops") || IsTrue(impact, "infrastructure"),
        "governance_context_orchestration_evidence_rule_change" => StringComparer.Ordinal.Equals(workItem.Scalar("type"), "GOVERNANCE_TOOLING"),
        "material_external_library_provider_protocol_standard" => IsTrue(impact, "provider"),
        "admin_or_support_workflow" => IsTrue(impact, "admin_ops") || IsTrue(impact, "support"),
        "milestone_or_release_closure" => IsTrue(impact, "release"),
        "product_decision_or_explicit_product_acceptance" => StringComparer.Ordinal.Equals(workItem.Scalar("type"), "PRODUCT_DECISION"),
        _ => false
    };

    private static Dictionary<string, bool> ImpactValues(GovernanceRecord workItem)
    {
        if (!workItem.Fields.TryGetValue("impact", out var node) || node is not YamlMappingNode mapping)
        {
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        return GovernanceApplication.Fields(mapping)
            .Where(item => item.Value is YamlScalarNode)
            .ToDictionary(item => item.Key, item => bool.TryParse(((YamlScalarNode)item.Value).Value, out var value) && value, StringComparer.Ordinal);
    }

    private static bool IsTrue(IReadOnlyDictionary<string, bool> impact, string name) => impact.TryGetValue(name, out var value) && value;
}

internal sealed record RoleActivationRule(string When, IReadOnlyList<string> Required);
internal sealed record RoleCalculation(IReadOnlyList<string> RequiredRoles, IReadOnlyList<string> ImpactClassifications, IReadOnlyList<string> MissingDeclaredRoles, IReadOnlyList<string> Findings);
internal sealed record RoleReviewCheck(IReadOnlyList<string> Failures, IReadOnlyList<string> Satisfied);
public sealed record RoleCalculationPayload(string WorkItemId, IReadOnlyList<string> RequiredRoles, IReadOnlyList<string> DeclaredRoles, IReadOnlyList<string> ImpactClassifications, IReadOnlyList<string> MissingMandatoryRoles, IReadOnlyList<string> Findings);

internal sealed class RoleActivationPolicy
{
    private RoleActivationPolicy(IReadOnlyList<RoleActivationRule> rules, DateTimeOffset? requiredFrom, IReadOnlyList<string> findings)
    {
        Rules = rules;
        RequiredFrom = requiredFrom;
        Findings = findings;
    }

    public IReadOnlyList<RoleActivationRule> Rules { get; }
    public DateTimeOffset? RequiredFrom { get; }
    public IReadOnlyList<string> Findings { get; }

    public bool IsEnforced(GovernanceRecord workItem) => RequiredFrom is null || DateTimeOffset.TryParse(workItem.Scalar("created_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) && createdAt >= RequiredFrom;

    public static RoleActivationPolicy Load(DirectoryInfo repository)
    {
        var path = Path.Combine(repository.FullName, "governance", "policies", "role-activation-policy.yaml");
        try
        {
            var stream = new YamlStream();
            using var reader = File.OpenText(path);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return Invalid("role activation policy must contain one mapping document");
            }

            var fields = GovernanceApplication.Fields(root);
            if (!fields.TryGetValue("rules", out var rulesNode) || rulesNode is not YamlSequenceNode rulesSequence)
            {
                return Invalid("role activation policy must declare rules");
            }

            var rules = new List<RoleActivationRule>();
            foreach (var node in rulesSequence.Children.OfType<YamlMappingNode>())
            {
                var rule = GovernanceApplication.Fields(node);
                var when = GovernanceApplication.Scalar(rule, "when");
                var required = GovernanceApplication.StringList(rule, "require");
                if (string.IsNullOrWhiteSpace(when) || required.Count == 0)
                {
                    return Invalid("every role activation rule must contain when and a non-empty require list");
                }

                rules.Add(new RoleActivationRule(when, required));
            }

            DateTimeOffset? requiredFrom = null;
            if (fields.TryGetValue("enforcement", out var enforcementNode) && enforcementNode is YamlMappingNode enforcement)
            {
                var requiredFromValue = GovernanceApplication.Scalar(GovernanceApplication.Fields(enforcement), "required_from");
                if (!string.IsNullOrWhiteSpace(requiredFromValue))
                {
                    if (!DateTimeOffset.TryParse(requiredFromValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedRequiredFrom))
                    {
                        return Invalid("role activation enforcement.required_from must be an ISO-8601 timestamp");
                    }

                    requiredFrom = parsedRequiredFrom;
                }
            }

            return new RoleActivationPolicy(rules, requiredFrom, []);
        }
        catch (YamlException exception)
        {
            return Invalid($"role activation policy is invalid YAML: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Invalid($"role activation policy cannot be read: {exception.Message}");
        }
    }

    private static RoleActivationPolicy Invalid(string finding) => new([], null, [finding]);
}