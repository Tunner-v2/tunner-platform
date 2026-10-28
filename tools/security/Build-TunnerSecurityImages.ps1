#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('postgres','openbao','mailpit','all')][string]$Component = 'all', [string]$RepositoryRoot, [switch]$Ensure)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$catalogPath = Join-Path $RepositoryRoot 'infra/security-images/catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$artifacts = Join-Path $RepositoryRoot 'artifacts/security-images'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$sourceCommit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
foreach ($entry in $catalog.images) {
    if ($Component -ne 'all' -and $entry.component -ne $Component) { continue }
    $recipePaths = @('infra/security-images/catalog.json','infra/security-images/.dockerignore','tools/security/Build-TunnerSecurityImages.ps1',$entry.dockerfile)
    if ($entry.PSObject.Properties.Name -contains 'recipe_inputs') { $recipePaths += @($entry.recipe_inputs) }
    $recipe = @($recipePaths | ForEach-Object {
        $text = [IO.File]::ReadAllText((Join-Path $RepositoryRoot $_)).Replace(([string][char]13 + [char]10),[string][char]10)
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
        [pscustomobject]@{path=$_;sha256=$hash}
    })
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($recipe | ConvertTo-Json -Compress)))).ToLowerInvariant()
    if ($Ensure) {
        $present = @(& docker image inspect $entry.image --format '{{index .Config.Labels "io.tunner.recipe.sha256"}}' 2>$null)
        if ($LASTEXITCODE -eq 0 -and (($present -join '').Trim() -ceq $fingerprint)) {
            Write-Output ('PRESENT: '+$entry.image+' matches the reviewed recipe; CI vulnerability/compatibility gates remain separate.')
            continue
        }
    }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($RepositoryRoot))
    if ($drive.AvailableFreeSpace -lt ($entry.minimum_free_gib * 1GB)) { throw ('At least '+$entry.minimum_free_gib+' GiB must be free on the workspace drive before building '+$entry.component+'. No cleanup is performed automatically.') }
    $metadata = Join-Path $artifacts ($entry.component+'-buildkit.json')
    $auditEpoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    & docker build --platform $catalog.platform --progress plain --file (Join-Path $RepositoryRoot $entry.dockerfile) --tag $entry.image --label ('io.tunner.recipe.sha256='+$fingerprint) --build-arg ('SOURCE_DATE_EPOCH='+$catalog.source_date_epoch) --build-arg ('UI_AUDIT_EPOCH='+$auditEpoch) --metadata-file $metadata (Join-Path $RepositoryRoot 'infra/security-images')
    if ($LASTEXITCODE -ne 0) { throw ('Private '+$entry.component+' rebuild failed; no default image was changed.') }
    $imageId = (& docker image inspect $entry.image --format '{{.Id}}').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the built image identity.' }
    $ownedDirty = @(& git -C $RepositoryRoot status --porcelain -- $recipePaths)
    $receipt = [ordered]@{schema_version=1;component=$entry.component;image=$entry.image;image_id=$imageId;platform=$catalog.platform;source_commit=$sourceCommit;recipe_sha256=$fingerprint;recipe_paths=$recipe;ui_audit_epoch=$auditEpoch;recipe_has_uncommitted_changes=($ownedDirty.Count -ne 0);status='BUILT_NOT_ACCEPTED';boundary='Private local candidate. Actual full-image vulnerability and compatibility checks are required; no publication, adoption, release or deployment approval.'}
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts ($entry.component+'-build.json')) -Encoding utf8NoBOM
    Write-Output ('BUILT: '+$entry.image+' '+$imageId+'; validation remains required.')
}
