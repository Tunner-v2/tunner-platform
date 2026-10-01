[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1'
Import-Module $module -Force
$root = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot

$required = @(
    '.tunner-root',
    'src/Tunner.Governance/RepositoryRootResolver.cs',
    'tools/lib/Tunner.RepositoryRoot.psm1',
    'tools/dev/tunner-dev.ps1',
    'tools/dev/tunner-test.ps1',
    'tools/dev/tunner-migrations.ps1',
    'tools/dev/openbao-bootstrap.ps1',
    'tools/validation/Test-TunnerRepositoryRoot.ps1',
    'docs/development/LOCAL_REPOSITORY_PORTABILITY.md',
    'governance/work-items/TUN-P0-035.yaml'
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $path))) { throw "Missing P0-035 artifact: $path" }
}

$resolver = Get-Content -LiteralPath (Join-Path $root 'src/Tunner.Governance/RepositoryRootResolver.cs') -Raw
foreach ($token in @('TUNNER_REPO_ROOT', 'TryGitRoot', '.tunner-root', 'ToLogicalPath', 'Path.GetRelativePath')) {
    if ($resolver -notmatch [regex]::Escape($token)) { throw "Missing governed C# resolver control: $token" }
}

foreach ($toolPath in @('tools/dev/tunner-dev.ps1', 'tools/dev/tunner-test.ps1', 'tools/dev/tunner-migrations.ps1', 'tools/dev/openbao-bootstrap.ps1')) {
    $tool = Get-Content -LiteralPath (Join-Path $root $toolPath) -Raw
    foreach ($token in @('Tunner.RepositoryRoot.psm1', 'Resolve-TunnerRepositoryRoot')) {
        if ($tool -notmatch [regex]::Escape($token)) { throw "Missing portable resolver use in ${toolPath}: $token" }
    }
}

$absolutePathPattern = '(?i)(?:\b[A-Z]:[\\/]|/(?:home|users|mnt|runner|builds)/[A-Za-z0-9._-]+(?:/|$)|/github/workspace(?:/|$))'
$binaryExtensions = @('.dll', '.exe', '.pdb', '.png', '.jpg', '.jpeg', '.gif', '.ico', '.pdf', '.zip')
$violations = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $tracked = & git -C $root ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files for the machine-path audit.' }
} else {
    $tracked = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.FullName.IndexOf('\bin\', [StringComparison]::OrdinalIgnoreCase) -lt 0 -and $_.FullName.IndexOf('\obj\', [StringComparison]::OrdinalIgnoreCase) -lt 0 -and $_.FullName.IndexOf('\artifacts\', [StringComparison]::OrdinalIgnoreCase) -lt 0 } | ForEach-Object { $_.FullName.Substring($root.Length).TrimStart([char[]]'\/').Replace('\', '/') }
}
foreach ($relativePath in $tracked) {
    if ($binaryExtensions -contains [IO.Path]::GetExtension($relativePath).ToLowerInvariant()) { continue }
    $fullPath = Join-Path $root ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    if ($bytes -contains 0) { continue }
    $content = [IO.File]::ReadAllText($fullPath)
    $match = [regex]::Match($content, $absolutePathPattern)
    if ($match.Success) { $violations.Add("$relativePath contains a machine-specific absolute path marker '$($match.Value)'.") }
}
if ($violations.Count -gt 0) { throw ($violations -join [Environment]::NewLine) }

Write-Host 'TUN-P0-035 static location-independent path validation passed.'
