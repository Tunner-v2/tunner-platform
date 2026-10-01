[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$ComposePath = 'infra/docker/compose.yaml'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$composePath = if ([IO.Path]::IsPathRooted($ComposePath)) { [IO.Path]::GetFullPath($ComposePath) } else { [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $ComposePath)) }
if (-not $composePath.StartsWith($RepositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'ComposePath must remain below the repository root.' }
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Missing Compose file: $ComposePath" }

$content = [IO.File]::ReadAllText($composePath)
$findings = [System.Collections.Generic.List[string]]::new()
$imageReferences = [regex]::Matches($content, '(?m)^\s*image:\s*(?<reference>\S+)\s*$') | ForEach-Object { $_.Groups['reference'].Value }
if (@($imageReferences).Count -lt 7) { $findings.Add('Compose must declare the seven approved local image defaults.') }
foreach ($imageReference in $imageReferences) {
    if ($imageReference -match '(?i)(?:^|:)latest(?:\}|$)') { $findings.Add("Mutable latest image tag: $imageReference") }
    if ($imageReference -notmatch ':-[^}]+:[^}]+\}') { $findings.Add("Image default must contain an exact version tag: $imageReference") }
}
if ($content -match '(?m)^\s*build\s*:') { $findings.Add('Compose image builds are outside the P0 local dependency baseline; use reviewed registry images only.') }
if ($content -match '(?im)^\s*privileged\s*:\s*true\s*$') { $findings.Add('Privileged containers are prohibited.') }
if ($content -match '(?im)^\s*network_mode\s*:\s*["'']?host') { $findings.Add('Host networking is prohibited.') }
foreach ($port in [regex]::Matches($content, '(?m)^\s*-\s+["''](?<binding>[^"'']+)["'']\s*$')) {
    $binding = $port.Groups['binding'].Value
    if ($binding -match '^\d+(?::\d+)?$') { $findings.Add("Published port must bind loopback explicitly: $binding") }
}

if ($findings.Count -gt 0) { throw "Tunner container policy finding: $($findings -join '; ')" }
Write-Host "Tunner container policy scan passed: $($imageReferences.Count) exact-version Compose image reference(s), loopback bindings, and no privileged/host-network/build directives."