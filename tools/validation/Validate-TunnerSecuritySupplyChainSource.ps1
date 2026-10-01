[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$required = @(
    'tools/security/Invoke-TunnerSastPolicyScan.ps1',
    'tools/security/Invoke-TunnerContainerPolicyScan.ps1',
    'tools/security/New-TunnerSupplyChainEvidence.ps1',
    'tools/security/Invoke-TunnerSecurityBaseline.ps1',
    '.github/workflows/security-supply-chain.yml',
    'docs/development/LOCAL_SECURITY_SUPPLY_CHAIN.md'
)
foreach ($path in $required) { if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) { throw "Missing P0-013 artifact: $path" } }

$sast = Get-Content -LiteralPath (Join-Path $RepositoryRoot $required[0]) -Raw
$container = Get-Content -LiteralPath (Join-Path $RepositoryRoot $required[1]) -Raw
$generator = Get-Content -LiteralPath (Join-Path $RepositoryRoot $required[2]) -Raw
$baseline = Get-Content -LiteralPath (Join-Path $RepositoryRoot $required[3]) -Raw
$workflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot $required[4]) -Raw
foreach ($token in @('unsafe-binary-serialization', 'unsafe-json-polymorphism', 'insecure-hash-implementation', 'unsafe-xml-resolver')) { if ($sast -notmatch [regex]::Escape($token)) { throw "Missing SAST policy: $token" } }
foreach ($token in @('exact version tag', 'Privileged containers are prohibited', 'Host networking is prohibited', 'Published port must bind loopback explicitly')) { if ($container -notmatch [regex]::Escape($token)) { throw "Missing Compose policy: $token" } }
foreach ($token in @('SPDX-2.3', '--vulnerable', '--include-transitive', 'working_tree_clean_before_generation', 'OutputDirectory must remain below', 'Get-PropertyValues')) { if ($generator -notmatch [regex]::Escape($token)) { throw "Missing supply-chain evidence control: $token" } }
foreach ($token in @('Invoke-TunnerSastPolicyScan.ps1', 'Invoke-TunnerSecretScan.ps1', 'Invoke-TunnerContainerPolicyScan.ps1', 'New-TunnerSupplyChainEvidence.ps1')) { if ($baseline -notmatch [regex]::Escape($token)) { throw "Missing baseline orchestration control: $token" } }
foreach ($pin in @('3d3c42e5aac5ba805825da76410c181273ba90b1', 'a98b56852c35b8e3190ac28c8c2271da59106c68', 'ed142fd0673e97e23eac54620cfb913e5ce36c25', '043fb46d1a93c77aae656e7c1c64a875d1fc6a0a')) { if ($workflow -notmatch $pin) { throw "Workflow must retain immutable action pin: $pin" } }
if ($workflow -notmatch '(?m)^\s*contents:\s*read\s*$') { throw 'Security workflow must retain read-only contents permission.' }
if ($workflow -match '(?m)^\s*contents:\s*write\s*$') { throw 'Security workflow must not request contents write.' }
if ($workflow -notmatch 'Invoke-TunnerSecurityBaseline.ps1' -or $workflow -notmatch 'scan-type:\s*image') { throw 'Security workflow must run the local baseline and image scan.' }
Write-Host 'TUN-P0-013 static security and supply-chain validation passed.'