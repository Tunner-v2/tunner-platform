[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$requiredFiles = @(
  'src\Tunner.Governance\ContextApplication.cs',
  'src\Tunner.Governance\Program.cs',
  'src\Tunner.Governance\README.md',
  'tests\Tunner.Governance.FunctionalTests\Program.cs'
)
foreach ($relativePath in $requiredFiles) {
  $fullPath = Join-Path $repositoryRoot $relativePath
  if (-not (Test-Path -LiteralPath $fullPath)) { throw "Missing P0-005 artifact: $relativePath" }
}

$contextSource = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\ContextApplication.cs')
foreach ($fragment in @('public static CommandResult Build', 'public static CommandResult Verify', 'GeneratorVersion', 'HashFile', 'GitHistory', 'IsWithin', 'ContextManifest', '"STALE"', 'ProcessStartInfo', 'ArgumentList.Add')) {
  if (-not $contextSource.Contains($fragment)) { throw "Context engine is missing required behavior marker: $fragment" }
}
foreach ($forbidden in @('Environment.GetEnvironmentVariable', 'UseShellExecute = true', 'cmd.exe', 'powershell.exe')) {
  if ($contextSource.Contains($forbidden)) { throw "Context engine contains forbidden execution behavior: $forbidden" }
}

$program = Get-Content -Raw (Join-Path $repositoryRoot 'src\Tunner.Governance\Program.cs')
foreach ($command in @('"context"', '"build"', '"verify"', '"--work-item"', '"--output"')) {
  if (-not $program.Contains($command)) { throw "Context CLI surface is missing: $command" }
}

$functionalTest = Get-Content -Raw (Join-Path $repositoryRoot 'tests\Tunner.Governance.FunctionalTests\Program.cs')
foreach ($scenario in @('ContextApplication.Build', 'ContextApplication.Verify', 'contextStale')) {
  if (-not $functionalTest.Contains($scenario)) { throw "Context fixture scenario is missing: $scenario" }
}

$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
$sdkStatus = if ($null -eq $sdk) { 'DOTNET_SDK_NOT_DETECTED' } else { 'DOTNET_SDK_AVAILABLE:' + (& dotnet --version) }

[pscustomobject]@{
  result = 'PASS'
  scope = 'P0-005 static context contract'
  executable_environment = $sdkStatus
  checked_artifacts = $requiredFiles
  security_boundary = 'context output is repository-contained; Git history uses fixed executable arguments without a shell'
} | ConvertTo-Json -Depth 5