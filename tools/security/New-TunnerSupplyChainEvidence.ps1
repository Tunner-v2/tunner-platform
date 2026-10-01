[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AsOf,
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$OutputDirectory = 'artifacts/supply-chain'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$parsedAsOf = [DateTime]::MinValue
if (-not [DateTime]::TryParseExact($AsOf, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedAsOf)) {
    throw 'AsOf must use ISO date format yyyy-MM-dd.'
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifactsRoot = Join-Path $RepositoryRoot 'artifacts'
$outputPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $OutputDirectory))
if (-not $outputPath.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must remain below the repository artifacts directory.'
}
[IO.Directory]::CreateDirectory($outputPath) | Out-Null

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Invoke-DotnetJson([string[]]$Arguments) {
    $output = & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    try { return ($output -join "`n" | ConvertFrom-Json) }
    catch { throw "dotnet $($Arguments -join ' ') did not produce parseable JSON: $($_.Exception.Message)" }
}
function Get-PropertyValues($Object, [string]$PropertyName) {
    if ($null -eq $Object) { return @() }
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) { return @() }
    return @($property.Value)
}

$inventory = Invoke-DotnetJson @('list', 'Tunner.Governance.sln', 'package', '--include-transitive', '--format', 'json', '--no-restore')
$vulnerabilityReport = Invoke-DotnetJson @('list', 'Tunner.Governance.sln', 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--no-restore')
$vulnerablePackages = [System.Collections.Generic.List[string]]::new()
foreach ($project in @(Get-PropertyValues $vulnerabilityReport 'projects')) {
    foreach ($framework in @(Get-PropertyValues $project 'frameworks')) {
        foreach ($groupName in @('topLevelPackages', 'transitivePackages')) {
            foreach ($package in @(Get-PropertyValues $framework $groupName)) {
                $vulnerabilities = @(Get-PropertyValues $package 'vulnerabilities')
                if ($vulnerabilities.Count -gt 0) {
                    $vulnerablePackages.Add(('{0}@{1}' -f (Get-PropertyValues $package 'id'), (Get-PropertyValues $package 'resolvedVersion')))
                }
            }
        }
    }
}
if ($vulnerablePackages.Count -gt 0) { throw "NuGet vulnerability scan reported package(s): $(($vulnerablePackages | Sort-Object -Unique) -join ', ')" }

$packages = @{}
foreach ($project in @(Get-PropertyValues $inventory 'projects')) {
    foreach ($framework in @(Get-PropertyValues $project 'frameworks')) {
        foreach ($groupName in @('topLevelPackages', 'transitivePackages')) {
            foreach ($package in @(Get-PropertyValues $framework $groupName)) {
                $packageId = [string](Get-PropertyValues $package 'id')
                $resolvedVersion = [string](Get-PropertyValues $package 'resolvedVersion')
                if ([string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($resolvedVersion)) { continue }
                $key = "$packageId|$resolvedVersion"
                $packages[$key] = [PSCustomObject]@{ id = $packageId; version = $resolvedVersion }
            }
        }
    }
}if ($packages.Count -eq 0) { throw 'Package inventory contained no resolved packages.' }

$gitCommit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-f]{40}$') { throw 'Unable to determine current Git commit.' }
& git -C $RepositoryRoot diff --quiet
$treeClean = $LASTEXITCODE -eq 0
if ($LASTEXITCODE -gt 1) { throw "git diff --quiet failed with exit code $LASTEXITCODE." }

$spdxPackages = @($packages.Values | Sort-Object id, version | ForEach-Object {
    $safeId = ($_.id -replace '[^A-Za-z0-9.-]', '-') + '-' + ($_.version -replace '[^A-Za-z0-9.-]', '-')
    [ordered]@{
        SPDXID = "SPDXRef-Package-$safeId"
        name = $_.id
        versionInfo = $_.version
        downloadLocation = 'NOASSERTION'
        filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
    }
})
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = 'Tunner Platform dependency inventory'
    documentNamespace = "https://github.com/Tunner-v2/tunner-platform/spdx/$gitCommit"
    creationInfo = [ordered]@{ created = "$AsOf`T00:00:00Z"; creators = @('Tool: Tunner Supply Chain Baseline 1.0.0') }
    packages = $spdxPackages
}
$sbomPath = Join-Path $outputPath 'tunner.spdx.json'
[IO.File]::WriteAllText($sbomPath, (($sbom | ConvertTo-Json -Depth 20) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

$checksumInputs = @('Directory.Packages.props', 'global.json', 'infra/docker/compose.yaml', 'Tunner.Governance.sln', 'docs/authority/current-authority.json') | ForEach-Object {
    $path = Join-Path $RepositoryRoot $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required checksum input is missing: $_" }
    [ordered]@{ path = $_; sha256 = Get-Sha256 $path }
}
$provenance = [ordered]@{
    schema_version = 1
    evidence_type = 'local-supply-chain-provenance-hook'
    generated_on = $AsOf
    git_commit = $gitCommit
    working_tree_clean_before_generation = $treeClean
    sbom = [ordered]@{ path = 'tunner.spdx.json'; sha256 = Get-Sha256 $sbomPath; format = 'SPDX-2.3' }
    checksum_inputs = @($checksumInputs | Sort-Object path)
    limitations = @('This is unsigned local or CI evidence only.', 'It is not a release attestation, production deployment record, or approval.', 'Release attestation requires its own governed release scope and immutable build artifact.')
}
$provenancePath = Join-Path $outputPath 'tunner.provenance.json'
[IO.File]::WriteAllText($provenancePath, (($provenance | ConvertTo-Json -Depth 20) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

Write-Host "Tunner SCA/SBOM/provenance generation passed: $($packages.Count) unique resolved package(s); no vulnerable package reported."
Write-Host "Generated disposable evidence: $sbomPath and $provenancePath"
