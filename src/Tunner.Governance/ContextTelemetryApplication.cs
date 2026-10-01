using System.Text.Json;
using System.Text.RegularExpressions;
namespace Tunner.Governance;
public static class ContextTelemetryApplication
{
    static readonly Regex Secret = new("(?:gh[pousr]_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|(?:password|pwd)\\s*=\\s*[^\\s;]{4,})",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static CommandResult Record(DirectoryInfo repo,string workItemId,FileInfo? output,ContextTelemetryInput input)
    {
        if(output is null||Path.IsPathRooted(output.Name)||!Path.GetFullPath(output.FullName).StartsWith(Path.Combine(repo.FullName,"artifacts","telemetry")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return new(2,"telemetry record",new ContextTelemetryPayload("REJECTED",workItemId,String.Empty,["output must be a new direct file under artifacts/telemetry"]));
        if(File.Exists(output.FullName)||Secret.IsMatch(JsonSerializer.Serialize(input,Json))) return new(2,"telemetry record",new ContextTelemetryPayload("REJECTED",workItemId,output.FullName,[File.Exists(output.FullName)?"output must be write-once":"telemetry values must not match the secret-safety pattern"]));
        var authority=AuthorityApplication.Verify(repo);if(authority.ExitCode!=0)return new(1,"telemetry record",new ContextTelemetryPayload("AUTHORITY_INVALID",workItemId,output.FullName,((AuthorityPayload)authority.Payload).Findings));
        if(!File.Exists(Path.Combine(repo.FullName,"governance","work-items",$"{workItemId}.yaml")))return new(2,"telemetry record",new ContextTelemetryPayload("REJECTED",workItemId,output.FullName,["work item does not exist"]));
        var record=new ContextTelemetryRecord(1,"tunner-context-telemetry-0.1.0",DateTimeOffset.UtcNow,workItemId,input.ContextMode,input.IncludedArtifacts,input.ExcludedArtifacts,input.ApproximateInputTokens,input.CacheOutcome,input.StaleRegenerated,input.ExpansionReason,input.ActivatedRoles,input.GateOutcome);
        Directory.CreateDirectory(output.DirectoryName!);File.WriteAllText(output.FullName,JsonSerializer.Serialize(record,Json)+Environment.NewLine);return new(0,"telemetry record",new ContextTelemetryPayload("RECORDED",workItemId,output.FullName,[]));
    }
}
public sealed record ContextTelemetryInput(string ContextMode,int IncludedArtifacts,int ExcludedArtifacts,int? ApproximateInputTokens,string CacheOutcome,bool StaleRegenerated,string? ExpansionReason,IReadOnlyList<string> ActivatedRoles,string GateOutcome);
public sealed record ContextTelemetryRecord(int SchemaVersion,string GeneratorVersion,DateTimeOffset RecordedAt,string WorkItemId,string ContextMode,int IncludedArtifacts,int ExcludedArtifacts,int? ApproximateInputTokens,string CacheOutcome,bool StaleRegenerated,string? ExpansionReason,IReadOnlyList<string> ActivatedRoles,string GateOutcome);
public sealed record ContextTelemetryPayload(string Outcome,string WorkItemId,string Output,IReadOnlyList<string> Findings);