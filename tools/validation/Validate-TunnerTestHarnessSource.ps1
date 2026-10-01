[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)
$ErrorActionPreference = "Stop"
foreach ($path in @("src/Tunner.Testing/Tunner.Testing.csproj", "tests/Tunner.TestHarness.Tests/Tunner.TestHarness.Tests.csproj", "tests/contracts/README.md", "tools/dev/tunner-test.ps1", "docs/development/LOCAL_TEST_HARNESS.md")) { if (-not (Test-Path (Join-Path $RepositoryRoot $path))) { throw "Missing P0-010 artifact: $path" } }
$packages = Get-Content -Raw (Join-Path $RepositoryRoot "Directory.Packages.props")
foreach ($package in @("xunit", "Testcontainers", "Microsoft.Playwright")) { $pattern = 'PackageVersion Include="' + [regex]::Escape($package) + '"'; if ($packages -notmatch $pattern) { throw "Missing central package version: $package" } }
$profiles = Get-Content -Raw (Join-Path $RepositoryRoot "src/Tunner.Testing/TestHarnessProfiles.cs")
if ($profiles -notmatch "TUNNER_RUN_CONTAINER_TESTS" -or $profiles -notmatch "TUNNER_RUN_BROWSER_TESTS") { throw "P0-010 must require explicit integration and browser opt-in." }
Write-Host "TUN-P0-010 static test-harness validation passed."