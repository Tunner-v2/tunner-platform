#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot, [switch]$Ensure)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$image = 'tunner-security/lgtm:0.38.1-r1'
$paths = @('tools/security/Build-TunnerLgtmSecurityImage.ps1') + @(& git -C $RepositoryRoot ls-files -- infra/security-images/lgtm-signing)
if ($LASTEXITCODE -ne 0 -or $paths.Count -lt 10) { throw 'Cannot resolve committed recipe inputs.' }
$dirty = @(& git -C $RepositoryRoot status --porcelain -- $paths)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Commit the work-item-owned recipe before building a source-bound candidate.' }
$commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
$recipe = @($paths | Sort-Object | ForEach-Object {
    $bytes = [Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText((Join-Path $RepositoryRoot $_)).Replace(([string][char]13+[char]10),[string][char]10))
    [pscustomobject]@{path=$_;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
})
$composite = ($recipe | ForEach-Object { $_.path+[char]0+$_.sha256+[char]10 }) -join ''
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($composite))).ToLowerInvariant()
if ($Ensure) {
    $present = @(& docker image inspect $image --format '{{index .Config.Labels "io.tunner.recipe.sha256"}}' 2>$null)
    if ($LASTEXITCODE -eq 0 -and ($present -join '').Trim() -ceq $fingerprint) { Write-Output ('PRESENT: '+$image+' matches the recipe; scan/compatibility/release gates remain separate.'); return }
}
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($RepositoryRoot))
if ($drive.AvailableFreeSpace -lt 18GB) { throw 'At least18 GiB must be free before the complete LGTM rebuild. No cleanup is performed automatically.' }
$output = Join-Path $RepositoryRoot 'artifacts/security-images'
[IO.Directory]::CreateDirectory($output) | Out-Null
$oldEpoch = [Environment]::GetEnvironmentVariable('UI_AUDIT_EPOCH','Process')
$oldRecipe = [Environment]::GetEnvironmentVariable('RECIPE_SHA256','Process')
try {
    $env:UI_AUDIT_EPOCH = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $env:RECIPE_SHA256 = $fingerprint
    Push-Location $RepositoryRoot
    try {
        & docker buildx bake -f infra/security-images/lgtm-signing/docker-bake.hcl --progress plain --metadata-file (Join-Path $output 'lgtm-buildkit.json')
        if ($LASTEXITCODE -ne 0) { throw 'Complete dev/test LGTM rebuild failed; no runtime default is changed.' }
    } finally { Pop-Location }
    $id = (& docker image inspect $image --format '{{.Id}}').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the immutable built image.' }
    [ordered]@{schema_version=1;scope_id='P1-RV001-001';component='lgtm';image=$image;image_id=$id;platform='linux/amd64';source_commit=$commit;recipe_sha256=$fingerprint;recipe_paths=$recipe;ui_audit_epoch=$env:UI_AUDIT_EPOCH;recipe_has_uncommitted_changes=$false;status='BUILT_NOT_ACCEPTED';boundary='Private dev/test candidate. Complete scan, compatibility, signature/tamper, provenance/SBOM/license gates remain required. No runtime adoption, publication, main merge or production approval.'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'lgtm-build.json') -Encoding utf8NoBOM
    Write-Output ('BUILT: '+$image+' '+$id+'; validation remains required.')
} finally {
    [Environment]::SetEnvironmentVariable('UI_AUDIT_EPOCH',$oldEpoch,'Process')
    [Environment]::SetEnvironmentVariable('RECIPE_SHA256',$oldRecipe,'Process')
}
