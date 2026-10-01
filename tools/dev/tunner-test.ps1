[CmdletBinding()]
param([ValidateSet("unit", "integration", "browser", "local")][string]$Profile = "unit")
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "../../tests/Tunner.TestHarness.Tests/Tunner.TestHarness.Tests.csproj"
if ($Profile -eq "integration" -and $env:TUNNER_RUN_CONTAINER_TESTS -ne "1") { throw "Set TUNNER_RUN_CONTAINER_TESTS=1 before explicit local Testcontainers execution." }
if ($Profile -eq "browser" -and $env:TUNNER_RUN_BROWSER_TESTS -ne "1") { throw "Install Playwright browsers and set TUNNER_RUN_BROWSER_TESTS=1 before explicit local browser execution." }
& dotnet test $project --no-restore
if ($LASTEXITCODE -ne 0) { throw "Test harness failed with exit code $LASTEXITCODE." }