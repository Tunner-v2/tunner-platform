[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$requiredFiles = @(
  'Tunner.Governance.sln',
  'src\Tunner.Governance\Tunner.Governance.csproj',
  'src\Tunner.Governance\Program.cs',
  'src\Tunner.Governance\GovernanceApplication.cs',
  'src\Tunner.Governance\AuthorityApplication.cs',
  'src\Tunner.Governance\ContextApplication.cs',
  'src\Tunner.Governance\README.md',
  'tests\Tunner.Governance.FunctionalTests\Tunner.Governance.FunctionalTests.csproj',
  'tests\Tunner.Governance.FunctionalTests\Program.cs'
)
foreach ($relativePath in $requiredFiles) {
  $fullPath = Join-Path $repositoryRoot $relativePath
  if (-not (Test-Path -LiteralPath $fullPath)) { throw "Missing P0-004 artifact: $relativePath" }
}

[xml]$mainProject = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\Tunner.Governance.csproj')
if ($mainProject.Project.PropertyGroup.OutputType -ne 'Exe') { throw 'Governance project must be an executable.' }
$packageReferences = @($mainProject.Project.ItemGroup.PackageReference | ForEach-Object { $_.Include })
foreach ($package in @('System.CommandLine', 'YamlDotNet', 'JsonSchema.Net')) {
  if ($packageReferences -notcontains $package) { throw "Missing approved package reference: $package" }
}

$source = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\GovernanceApplication.cs')
foreach ($fragment in @('YamlStream', 'JsonSchema.FromText', 'public static CommandResult Validate', 'public static CommandResult Status', 'public static CommandResult Next', 'public static CommandResult CheckGate', 'public static CommandResult CheckTransition', 'AddAuthorityDiagnostics', 'GOV_AUTHORITY_INVALID', 'INSUFFICIENT_CONTEXT', 'IntegrationPolicy', 'HumanIntegrationActions', 'GOV_PR_INTEGRATION_POLICY', 'GOV_DEPENDENCY_READINESS', 'LOCAL_VALIDATED', 'MERGED_TO_MAIN', 'DependentUnmergedChainLimit', 'ConcurrentUnmergedWorkLimit')) {
  if (-not $source.Contains($fragment)) { throw "Governance engine is missing required behavior marker: $fragment" }
}
foreach ($forbidden in @('File.WriteAllText', 'File.Delete', 'Directory.Delete', 'Process.Start', 'Environment.GetEnvironmentVariable')) {
  if ($source.Contains($forbidden)) { throw "Read-only MVP source contains forbidden operation: $forbidden" }
}

$authoritySource = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\AuthorityApplication.cs')
foreach ($fragment in @('public static CommandResult Verify', 'BOOT-P0-001-integrity.json', 'docs/authority/', 'SHA256.HashData', 'AuthorityPayload')) {
  if (-not $authoritySource.Contains($fragment)) { throw "Authority verifier is missing required behavior marker: $fragment" }
}
foreach ($forbidden in @('File.WriteAllText', 'File.Delete', 'Directory.Delete', 'Process.Start', 'Environment.GetEnvironmentVariable')) {
  if ($authoritySource.Contains($forbidden)) { throw "Authority verifier contains forbidden operation: $forbidden" }
}

$contextSource = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\ContextApplication.cs')
foreach ($fragment in @('AuthorityApplication.Verify', 'AUTHORITY_INVALID')) {
  if (-not $contextSource.Contains($fragment)) { throw "Context gate is missing required authority behavior marker: $fragment" }
}

$program = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\Program.cs')
foreach ($command in @('"validate"', '"status"', '"next"', '"gate"', '"transition"', '"authority"', '"verify"')) {
  if (-not $program.Contains($command)) { throw "CLI command is missing: $command" }
}

$functionalTest = Get-Content -Raw (Join-Path $repositoryRoot 'tests\Tunner.Governance.FunctionalTests\Program.cs')
foreach ($scenario in @('GovernanceApplication.Validate', 'GovernanceApplication.Status', 'GovernanceApplication.Next', 'GovernanceApplication.CheckGate', 'GovernanceApplication.CheckTransition', 'AuthorityApplication.Verify', 'Authority verify must reject changed authority bytes', 'Context build must block when authority verification fails', 'HumanIntegrationActions', 'TUN-LOCAL', 'TUN-MERGED', 'TUN-CHAIN', 'malformed.yaml')) {
  if (-not $functionalTest.Contains($scenario)) { throw "Functional fixture scenario is missing: $scenario" }
}

$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
$sdkStatus = if ($null -eq $sdk) { 'DOTNET_SDK_NOT_DETECTED' } else { 'DOTNET_SDK_AVAILABLE:' + (& dotnet --version) }

[pscustomobject]@{
  result = 'PASS'
  scope = 'P0-004 static source contract'
  executable_environment = $sdkStatus
  checked_artifacts = $requiredFiles
} | ConvertTo-Json -Depth 5