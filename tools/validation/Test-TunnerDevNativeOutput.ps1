[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }
$runner = Join-Path $RepositoryRoot "tools/dev/tunner-dev.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "Missing runner: $runner" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("tunner-dev-native-output-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$fakeDocker = Join-Path $tempRoot "docker.cmd"
$fakeDotnet = Join-Path $tempRoot "dotnet.cmd"
$dockerArguments = Join-Path $tempRoot "docker-arguments.txt"
$dotnetArguments = Join-Path $tempRoot "dotnet-arguments.txt"
[IO.File]::WriteAllText($fakeDocker, "@echo off`r`necho %* >> `"$dockerArguments`"`r`necho Container tunner-local-postgres-1 Running 1>&2`r`nexit /b 0`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText($fakeDotnet, "@echo off`r`necho %* >> `"$dotnetArguments`"`r`necho Tunner test harness invoked`r`nexit /b 0`r`n", [Text.Encoding]::ASCII)
$previousPath = $env:Path
try {
    $env:Path = "$tempRoot;$previousPath"
    $global:LASTEXITCODE = 0
    & $runner start
    if ($LASTEXITCODE -ne 0) { throw "Expected the current-shell fake Docker start path to succeed, but it exited $LASTEXITCODE." }

    $resetRejected = $false
    try { & $runner reset } catch { $resetRejected = $_.Exception.Message -match 'ConfirmReset' }
    if (-not $resetRejected) { throw 'Reset must reject execution until ConfirmReset is supplied.' }
    & $runner reset -ConfirmReset
    if ($LASTEXITCODE -ne 0) { throw "Expected the fake Docker reset path to succeed, but it exited $LASTEXITCODE." }
    if ((Get-Content -LiteralPath $dockerArguments -Raw) -notmatch 'down.*--volumes.*--remove-orphans') { throw 'Reset must invoke Compose down with named-volume removal and orphan removal.' }

    & $runner test
    if ($LASTEXITCODE -ne 0) { throw "Expected the fake dotnet test path to succeed, but it exited $LASTEXITCODE." }
    if ((Get-Content -LiteralPath $dotnetArguments -Raw) -notmatch 'test.*Tunner.TestHarness.Tests.csproj.*--no-restore') { throw 'Test command must dispatch only to the deterministic unit-test harness.' }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -ne $windowsPowerShell) {
        $windowsOutput = & $windowsPowerShell.Source -NoProfile -ExecutionPolicy Bypass -File $runner start 2>&1
        $windowsExitCode = $LASTEXITCODE
        $windowsOutput | ForEach-Object { Write-Host $_ }
        if ($windowsExitCode -ne 0) { throw "Expected the Windows PowerShell fake Docker start path to succeed, but it exited $windowsExitCode." }
    }
}
finally {
    $env:Path = $previousPath
    if (Test-Path -LiteralPath $tempRoot) { [IO.Directory]::Delete($tempRoot, $true) }
}

Write-Host "Tunner dev native stderr and developer-automation regression test passed."