[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$required = @('tools/dev/tunner-dev.ps1', 'tools/dev/tunner-test.ps1', 'tools/validation/Test-TunnerDevNativeOutput.ps1', 'docs/development/LOCAL_DEVELOPER_AUTOMATION.md')
foreach ($path in $required) { if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) { throw "Missing P0-014 artifact: $path" } }
$tool = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'tools/dev/tunner-dev.ps1') -Raw
$test = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'tools/validation/Test-TunnerDevNativeOutput.ps1') -Raw
foreach ($command in @('"doctor"', '"setup"', '"start"', '"stop"', '"reset"', '"health"', '"test"', '"logs"')) { if ($tool -notmatch [regex]::Escape($command)) { throw "Missing tunner-dev command: $command" } }
foreach ($token in @('ConfirmReset', '"down", "--volumes", "--remove-orphans"', 'Invoke-UnitTests', 'tunner-test.ps1', 'Other Docker projects were not targeted')) { if ($tool -notmatch [regex]::Escape($token)) { throw "Missing P0-014 safety control: $token" } }
foreach ($token in @('Reset must reject execution', 'down.*--volumes.*--remove-orphans', 'Test command must dispatch only')) { if ($test -notmatch [regex]::Escape($token)) { throw "Missing P0-014 isolated regression assertion: $token" } }
Write-Host 'TUN-P0-014 static developer-automation validation passed.'