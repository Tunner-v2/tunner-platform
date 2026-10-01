[CmdletBinding()]
param([string]$RepositoryRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$validator = Join-Path $repositoryRoot 'tools/validation/Validate-TunnerDocumentation.ps1'
if (-not (Test-Path -LiteralPath $validator)) { throw "Missing P0-015 validator: $validator" }

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('tunner-documentation-validation-' + [Guid]::NewGuid().ToString('N'))
$lineBreak = [Environment]::NewLine
try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'docs/authority/baselines/1.6.0') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'docs/authority/amendments') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'governance/work-items') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'docs/authority/baselines/1.6.0/00.md'), ("> **Tunner Development Authority Package**" + $lineBreak + "> **Status:** ACTIVE" + $lineBreak + $lineBreak + "# Authority" + $lineBreak))
    [IO.File]::WriteAllText((Join-Path $fixture 'docs/guide.md'), ("# Guide" + $lineBreak + $lineBreak + "[Authority](authority/baselines/1.6.0/00.md)" + $lineBreak))
    [IO.File]::WriteAllText((Join-Path $fixture 'governance/work-items/TUN-DOC-001.yaml'), ("work_item_id: TUN-DOC-001" + $lineBreak))

    & $validator -RepositoryRoot $fixture -AsOf '2026-10-01' -SkipSchemaValidation -SkipSourceFreshness | Out-Null
    $nativeExit = Get-Variable -Name LASTEXITCODE -ValueOnly -ErrorAction SilentlyContinue
    if ($null -ne $nativeExit -and $nativeExit -ne 0) { throw 'Positive documentation fixture failed.' }

    function Assert-Failure([string]$Name, [string]$ExpectedToken, [scriptblock]$Action) {
        try { & $Action; throw "$Name unexpectedly passed." }
        catch {
            if ($_.Exception.Message -notmatch [regex]::Escape($ExpectedToken)) { throw "${Name} did not emit ${ExpectedToken}: $($_.Exception.Message)" }
        }
    }

    [IO.File]::AppendAllText((Join-Path $fixture 'docs/guide.md'), ($lineBreak + '[Missing](missing.md)' + $lineBreak))
    Assert-Failure 'missing local link' 'DOC_LINK_MISSING' { & $validator -RepositoryRoot $fixture -AsOf '2026-10-01' -SkipSchemaValidation -SkipSourceFreshness | Out-Null }
    [IO.File]::WriteAllText((Join-Path $fixture 'docs/guide.md'), ("# Guide" + $lineBreak + $lineBreak + "[Authority](authority/baselines/1.6.0/00.md)" + $lineBreak))

    [IO.File]::WriteAllText((Join-Path $fixture 'governance/work-items/TUN-DOC-002.yaml'), ("work_item_id: TUN-DOC-001" + $lineBreak))
    Assert-Failure 'duplicate identifier' 'DOC_ID_DUPLICATE' { & $validator -RepositoryRoot $fixture -AsOf '2026-10-01' -SkipSchemaValidation -SkipSourceFreshness | Out-Null }
    Remove-Item -LiteralPath (Join-Path $fixture 'governance/work-items/TUN-DOC-002.yaml') -Force

    [IO.File]::WriteAllText((Join-Path $fixture 'docs/authority/baselines/1.6.0/00.md'), ("# Header missing" + $lineBreak))
    Assert-Failure 'authority header' 'DOC_AUTHORITY_HEADER_MISSING' { & $validator -RepositoryRoot $fixture -AsOf '2026-10-01' -SkipSchemaValidation -SkipSourceFreshness | Out-Null }

    Write-Host 'TUN-P0-015 isolated documentation validation checks passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}