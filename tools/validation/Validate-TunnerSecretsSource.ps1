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
    "tools/security/secret-scan-allowances.json",
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

$scannerPath = Join-Path $RepositoryRoot 'tools/security/Invoke-TunnerSecretScan.ps1'
$configurationText = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'tools/security/secret-scan-allowances.json'))
$configuration = $configurationText | ConvertFrom-Json -AsHashtable
# Read the production rule without executing a second implementation of it.
$scannerAst = [System.Management.Automation.Language.Parser]::ParseFile($scannerPath, [ref]$null, [ref]$null)
$genericRule = $scannerAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.HashtableAst] }, $true) |
    ForEach-Object { try { $_.SafeGetValue() } catch { } } |
    Where-Object { $_.Contains('Name') -and $_.Name -eq 'generic-secret-assignment' } |
    Select-Object -First 1
if ($null -eq $genericRule -or $configuration.allowances.Count -ne 6) { throw 'Secret-scan qualification inputs changed; review the case set.' }
$reviewedMatches = @(
    foreach ($allowance in $configuration.allowances) {
        $content = [IO.File]::ReadAllText((Join-Path $RepositoryRoot $allowance.path))
        $matching = @([regex]::Matches($content, $genericRule.Pattern) | Where-Object {
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($_.Value))).ToLowerInvariant() -ceq $allowance.match_sha256
        })
        if ($matching.Count -ne 1) { throw 'Reviewed secret-scan fingerprint is missing or ambiguous.' }
        [PSCustomObject]@{ Path = $allowance.path; Value = $matching[0].Value }
    }
)

$fixtureName = 'tunner-secret-scan-' + [Guid]::NewGuid().ToString('N')
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) $fixtureName
$fixtureConfigurationPath = Join-Path $fixtureDirectory 'tools/security/secret-scan-allowances.json'
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
$script:secretScanCaseCount = 0
function Write-ScanFixture([string]$RelativePath, [string]$Content) {
    $target = Join-Path $fixtureDirectory $RelativePath
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    [IO.File]::WriteAllText($target, $Content)
}
function Assert-ScanAccepted([string[]]$Paths) {
    try { & $scannerPath -RepositoryRoot $fixtureDirectory -Path $Paths *> $null }
    catch { throw 'Secret-scan acceptance case failed; fixture values withheld.' }
    $script:secretScanCaseCount++
}
function Assert-ScanRejected([string[]]$Paths, [string]$ExpectedRule = '', [switch]$InvalidConfiguration) {
    $rejected = $false
    try { & $scannerPath -RepositoryRoot $fixtureDirectory -Path $Paths *> $null }
    catch {
        $message = $_.Exception.Message
        $rejected = if ($InvalidConfiguration) { $message -eq 'Invalid secret-scan allowance configuration.' }
            else { $message.StartsWith('Potential committed secret detected.', [StringComparison]::Ordinal) -and $message.Contains('[' + $ExpectedRule + ']') }
    }
    if (-not $rejected) { throw 'Secret-scan rejection case failed; fixture values withheld.' }
    $script:secretScanCaseCount++
}
function Restore-ScanConfiguration {
    Write-ScanFixture 'tools/security/secret-scan-allowances.json' $configurationText
}
try {
    Restore-ScanConfiguration
    $genericPositive = 'api' + '_key=' + ('Z' * 32)
    foreach ($reviewed in $reviewedMatches) {
        Write-ScanFixture $reviewed.Path $reviewed.Value
        Assert-ScanAccepted @($reviewed.Path)
        Write-ScanFixture $reviewed.Path ($reviewed.Value + 'Z')
        Assert-ScanRejected @($reviewed.Path) 'generic-secret-assignment'
        Write-ScanFixture $reviewed.Path ($reviewed.Value + "`n" + $genericPositive)
        Assert-ScanRejected @($reviewed.Path) 'generic-secret-assignment'
        Write-ScanFixture 'other-location.txt' $reviewed.Value
        Assert-ScanRejected @('other-location.txt') 'generic-secret-assignment'
        Write-ScanFixture $reviewed.Path $reviewed.Value
    }
    $reviewedPaths = @($reviewedMatches | ForEach-Object Path)
    Assert-ScanAccepted $reviewedPaths

    # Construct nonfunctional positive vectors in memory; never log their bytes.
    $positives = @(
        @{ Rule = 'private-key'; Value = ('-----BEGIN ' + 'PRIVATE KEY-----') },
        @{ Rule = 'github-token'; Value = ('gh' + 'p_' + ('A' * 32)) },
        @{ Rule = 'slack-token'; Value = ('xo' + 'xb-' + ('A' * 32)) },
        @{ Rule = 'generic-secret-assignment'; Value = $genericPositive }
    )
    foreach ($positive in $positives) {
        Write-ScanFixture $reviewedMatches[0].Path ($reviewedMatches[0].Value + "`n" + $positive.Value)
        Assert-ScanRejected @($reviewedMatches[0].Path) $positive.Rule
        Write-ScanFixture 'other-location.txt' $positive.Value
        Assert-ScanRejected @('other-location.txt') $positive.Rule
    }
    # Retain the original combined generic/GitHub regression case.
    Write-ScanFixture 'other-location.txt' ('api' + '_key=gh' + 'p_' + ('A' * 32))
    Assert-ScanRejected @('other-location.txt') 'github-token'
    foreach ($reviewed in $reviewedMatches) { Write-ScanFixture $reviewed.Path $reviewed.Value }

    $invalidMutations = @(
        { param($value) $value.schema_version = 2 },
        { param($value) $value.schema_version = $true },
        { param($value) $value.unexpected = $true },
        { param($value) $value.allowances = @() },
        { param($value) $value.allowances[0].path = '../outside.txt' },
        { param($value) $value.allowances[0].path = 'C:/outside.txt' },
        { param($value) $value.allowances[0].path = 'tests/*' },
        { param($value) $value.allowances[0].match_sha256 = 'invalid' },
        { param($value) $value.allowances[0].rule = 'github-token' },
        { param($value) $value.allowances[0].rule = @('generic-secret-assignment') },
        { param($value) $value.allowances[0].reason = '' },
        { param($value) $value.allowances[0].Remove('match_sha256') | Out-Null },
        { param($value) $value.allowances += @($value.allowances[0]) }
    )
    foreach ($mutation in $invalidMutations) {
        $modified = $configurationText | ConvertFrom-Json -AsHashtable
        & $mutation $modified
        Write-ScanFixture 'tools/security/secret-scan-allowances.json' ($modified | ConvertTo-Json -Depth 8)
        Assert-ScanRejected $reviewedPaths -InvalidConfiguration
    }
    Write-ScanFixture 'tools/security/secret-scan-allowances.json' '{'
    Assert-ScanRejected $reviewedPaths -InvalidConfiguration
    Remove-Item -LiteralPath $fixtureConfigurationPath
    Assert-ScanRejected $reviewedPaths -InvalidConfiguration

    $modified = $configurationText | ConvertFrom-Json -AsHashtable
    $modified.allowances[0].match_sha256 = '0' * 64
    Write-ScanFixture 'tools/security/secret-scan-allowances.json' ($modified | ConvertTo-Json -Depth 8)
    Assert-ScanRejected $reviewedPaths 'generic-secret-assignment'
    $modified = $configurationText | ConvertFrom-Json -AsHashtable
    $modified.allowances[0].path = 'mismatched-location.txt'
    Write-ScanFixture 'tools/security/secret-scan-allowances.json' ($modified | ConvertTo-Json -Depth 8)
    Assert-ScanRejected $reviewedPaths 'generic-secret-assignment'
    Restore-ScanConfiguration
    Assert-ScanAccepted $reviewedPaths
}
finally {
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureDirectory)
    if (-not $resolvedFixture.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -cne $fixtureName) { throw 'Refusing cleanup outside the owned secret-scan fixture directory.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}

Write-Host "TUN-P0-007 source validation passed; $script:secretScanCaseCount focused secret-scan cases passed."
