using System.Text.Json;
using Tunner.Governance;

var repository = RepositoryRootResolver.Resolve();
var artifacts = new DirectoryInfo(Path.Combine(repository.FullName, "artifacts", "audit-remediation-tests"));
var fixture = new DirectoryInfo(Path.Combine(artifacts.FullName, Guid.NewGuid().ToString("N")));
fixture.Create();
if (!fixture.FullName.StartsWith(artifacts.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Fixture cleanup escaped its artifact directory.");
try
{
    foreach (var directory in new[] { "docs/authority", "governance" })
        Copy(Path.Combine(repository.FullName, directory), Path.Combine(fixture.FullName, directory));
    File.Copy(Path.Combine(repository.FullName, "AGENTS.md"), Path.Combine(fixture.FullName, "AGENTS.md"));
    File.Copy(Path.Combine(repository.FullName, ".tunner-root"), Path.Combine(fixture.FullName, ".tunner-root"));
    Check(AuthorityApplication.Verify(fixture).ExitCode == 0, "supplemental and historical authority verify together");
    var source = Path.Combine(fixture.FullName, "docs/authority/current/development/01_Engineering_Technology_Foundation.md");
    var bytes = File.ReadAllBytes(source);
    File.AppendAllText(source, "mutation");
    Check(AuthorityApplication.Verify(fixture).ExitCode != 0, "single-byte overlay drift is rejected");
    File.WriteAllBytes(source, bytes);
    var manifestPath = Path.Combine(fixture.FullName, "docs/authority/current/supplemental-manifest.json");
    bytes = File.ReadAllBytes(manifestPath);
    File.AppendAllText(manifestPath, " ");
    Check(AuthorityApplication.Verify(fixture).ExitCode != 0, "supplemental manifest drift is rejected");
    File.WriteAllBytes(manifestPath, bytes);
    // Model the no-eligible-work recovery branch independently of this checkout's active corrections.
    foreach (var revision in new[] { "P0-RV004", "P0-030-RV002", "P0-032-RV002", "P0-033-RV002", "P1-022-RV002", "P1-024-RV002" })
        File.Delete(Path.Combine(fixture.FullName, "governance", "revisions", revision + ".yaml"));
    var originalProject = Path.Combine(fixture.FullName, "docs", "project", "HANDOFF.md");
    Directory.CreateDirectory(Path.GetDirectoryName(originalProject)!);
    File.WriteAllText(originalProject, "pre-existing unrelated recovery note");
    var registrationPath = Path.Combine(fixture.FullName, "docs/authority/current/supplemental-registration.json");
    var registrationBytes = File.ReadAllBytes(registrationPath);
    File.Delete(registrationPath);
    Check(AuthorityApplication.Verify(fixture).ExitCode != 0, "missing required supplemental registration is rejected");
    File.WriteAllBytes(registrationPath, registrationBytes);
    var output = new DirectoryInfo(Path.Combine(fixture.FullName, "artifacts", "context"));
    Check(ContextApplication.Build(fixture, "TUN-P1-023", output).ExitCode == 0, "ordinary context builds");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.FullName, "manifest.json")));
    var paths = manifest.RootElement.GetProperty("Included").EnumerateArray().Select(item => item.GetProperty("Path").GetString()).ToArray();
    Check(paths.Contains("governance/defects/DEF-TUN-P1-023-R2-001.yaml"), "linked ordinary-work defect is hash-recorded");
    Check(paths.Contains("docs/authority/current/development/AMD-0002_Non-Blocking_PR_Approval_and_Continuous_Local_Execution.md"), "active amendment selects verified current clarification");
    Check(File.ReadAllText(originalProject) == "pre-existing unrelated recovery note", "isolated pack preserves existing project files");
    var handoff = File.ReadAllText(Path.Combine(output.FullName, "project/HANDOFF.md"));
    Check(handoff.Contains("Defects: DEF-TUN-P1-023-R2-001", StringComparison.Ordinal) &&
        handoff.Contains("Evidence: governance/evidence/TUN-P1-023-GAP-REGISTER.json", StringComparison.Ordinal),
        "durable ordinary-work handoff includes actual defect and evidence fields");
    Check(handoff.Contains("acceptance/release authorization", StringComparison.Ordinal), "post-validation recovery names the remaining authority gate");
    Check(ContextApplication.Verify(fixture, output).ExitCode == 0, "generated context verifies before mutation");
    var defectPath = Path.Combine(fixture.FullName, "governance/defects/DEF-TUN-P1-023-R2-001.yaml");
    File.AppendAllText(defectPath, Environment.NewLine + "# mutation" + Environment.NewLine);
    Check(ContextApplication.Verify(fixture, output).ExitCode != 0, "linked defect mutation makes context stale");
    Console.WriteLine("PASS: 12 audit-remediation authority/context checks");
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
finally
{
    fixture.Delete(true);
}

return 0;

static void Check(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException("FAIL: " + name);
}

static void Copy(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source))
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var directory in Directory.EnumerateDirectories(source))
        Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
}
