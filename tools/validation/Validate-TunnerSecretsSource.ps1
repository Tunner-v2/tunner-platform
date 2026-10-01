[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }

$requiredFiles = @(
    "infra/openbao/policies/tunner-local-bootstrap.hcl",
    "infra/openbao/policies/tunner-local-runtime.hcl",
    "tools/dev/openbao-bootstrap.ps1",
    "tools/security/Invoke-TunnerSecretScan.ps1",
    "docs/development/LOCAL_OPENBAO.md"
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relativePath))) {
        throw "Missing required P0-007 artifact: $relativePath"
    }
}

$bootstrapPolicy = Get-Content -LiteralPath (Join-Path $RepositoryRoot "infra/openbao/policies/tunner-local-bootstrap.hcl") -Raw
$runtimePolicy = Get-Content -LiteralPath (Join-Path $RepositoryRoot "infra/openbao/policies/tunner-local-runtime.hcl") -Raw
$bootstrapTool = Get-Content -LiteralPath (Join-Path $RepositoryRoot "tools/dev/openbao-bootstrap.ps1") -Raw

if ($bootstrapPolicy -notmatch 'sys/mounts/tunner' -or $bootstrapPolicy -notmatch 'sys/policies/acl/tunner-local-runtime') {
    throw "Bootstrap policy does not have the expected narrow provisioning scope."
}
if ($runtimePolicy -notmatch 'tunner/data/local/\*' -or $runtimePolicy -notmatch 'capabilities\s*=\s*\["read"\]') {
    throw "Runtime policy must provide read-only local KV-v2 access."
}
if ($runtimePolicy -match '(?im)\b(create|update|delete|sudo)\b') {
    throw "Runtime policy exceeds read/list capability."
}
if ($bootstrapTool -notmatch 'Read-Host .*AsSecureString' -or $bootstrapTool -notmatch 'Remove-Variable rootToken') {
    throw "Bootstrap tool must obtain the root token interactively and clear transient variables."
}
if ($bootstrapTool -notmatch 'bao secrets enable -path=tunner kv-v2') {
    throw "Bootstrap tool must provision the local KV-v2 mount before applying policies."
}

& (Join-Path $RepositoryRoot "tools/security/Invoke-TunnerSecretScan.ps1") -RepositoryRoot $RepositoryRoot

$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ("tunner-secret-scan-" + [Guid]::NewGuid().ToString("N"))
$fixturePath = Join-Path $fixtureDirectory "negative-fixture.txt"
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
try {
    $fixtureContent = "api" + "_key=ghp_" + "0123456789abcdefghij`n"
    [IO.File]::WriteAllText($fixturePath, $fixtureContent)
    $wasRejected = $false
    try {
        & (Join-Path $RepositoryRoot "tools/security/Invoke-TunnerSecretScan.ps1") -RepositoryRoot $RepositoryRoot -Path $fixturePath
    }
    catch {
        $wasRejected = $_.Exception.Message -match "Potential committed secret"
    }
    if (-not $wasRejected) { throw "Secret scanner did not reject the negative fixture." }
}
finally {
    if (Test-Path -LiteralPath $fixtureDirectory) { Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force }
}

Write-Host "TUN-P0-007 source validation passed."