[CmdletBinding()]
param([ValidateSet("unit", "integration", "browser", "local")][string]$Profile = "unit", [string]$RepositoryRoot)
$ErrorActionPreference = "Stop"
$module = Join-Path $PSScriptRoot "../lib/Tunner.RepositoryRoot.psm1"
Import-Module $module -Force
$repositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$project = Join-Path $repositoryRoot "tests/Tunner.TestHarness.Tests/Tunner.TestHarness.Tests.csproj"
if ($Profile -eq "integration" -and $env:TUNNER_RUN_CONTAINER_TESTS -ne "1") { throw "Set TUNNER_RUN_CONTAINER_TESTS=1 before explicit local Testcontainers execution." }
if ($Profile -eq "browser" -and $env:TUNNER_RUN_BROWSER_TESTS -ne "1") { throw "Install Playwright browsers and set TUNNER_RUN_BROWSER_TESTS=1 before explicit local browser execution." }
& dotnet test $project --no-restore
if ($LASTEXITCODE -ne 0) { throw "Test harness failed with exit code $LASTEXITCODE." }