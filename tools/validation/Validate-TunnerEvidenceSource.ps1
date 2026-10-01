[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

$ErrorActionPreference = 'Stop'
$required = @(
    'src/Tunner.Governance/Program.cs',
    'src/Tunner.Governance/EvidenceApplication.cs',
    'tests/Tunner.Governance.FunctionalTests/Program.cs',
    'docs/development/LOCAL_EVIDENCE_MANIFEST.md',
    'src/Tunner.Governance/README.md'
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) {
        throw "Missing P0-011 artifact: $path"
    }
}

$program = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/Program.cs')
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Governance/EvidenceApplication.cs')
$tests = Get-Content -Raw (Join-Path $RepositoryRoot 'tests/Tunner.Governance.FunctionalTests/Program.cs')
if ($program -notmatch 'CreateEvidenceCommand' -or $program -notmatch 'new Command\("generate"') { throw 'P0-011 must expose the evidence generate command.' }
foreach ($token in @('GeneratorVersion', 'ValidateOutput', 'TryResolveArtifact', 'TryReadGitIdentity', 'SecretPattern', 'FileMode.CreateNew', 'write-once', 'This manifest does not approve a release')) {
    if ($source -notmatch [regex]::Escape($token)) { throw "P0-011 must retain $token." }
}
foreach ($token in @('byte-identical manifests', 'reject output paths outside the repository', 'reject an existing output path', 'secret-shaped value')) {
    if ($tests -notmatch [regex]::Escape($token)) { throw "P0-011 functional coverage is missing $token." }
}
Write-Host 'TUN-P0-011 static evidence-manifest validation passed.'