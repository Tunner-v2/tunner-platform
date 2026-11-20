[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot=Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$defaults=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'infra/docker/.env.example'))
$workflow=[IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/security-supply-chain.yml'))
$catalog=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'infra/security-images/catalog.json') -Raw | ConvertFrom-Json
$compose=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'infra/docker/compose.yaml'))
$bindings=@{}
foreach($m in [regex]::Matches($defaults,'(?m)^([A-Z_]+_IMAGE)=(\S+)')){$bindings[$m.Groups[1].Value]=$m.Groups[2].Value}
foreach($m in [regex]::Matches($compose,'image:\s*\$\{([A-Z_]+_IMAGE):-([^}]+)\}')){if($bindings[$m.Groups[1].Value] -cne $m.Groups[2].Value){throw ('Compose fallback differs from the scanned default: '+$m.Groups[1].Value)}}
$declared=@([regex]::Matches($defaults,'(?m)^[A-Z_]+_IMAGE=(\S+)') | ForEach-Object {$_.Groups[1].Value})
$scanned=@([regex]::Matches($workflow,'(?m)^\s*- image: (\S+)') | ForEach-Object {$_.Groups[1].Value})
if ($declared.Count -lt 7 -or $scanned.Count -ne ($scanned | Select-Object -Unique).Count) {throw 'Missing runtime coverage or duplicate matrix image.'}
foreach($image in $declared){if($scanned -cnotcontains $image){throw ('Runtime image is not in the enforced scan matrix: '+$image)}}
foreach($image in $scanned){if($declared -cnotcontains $image){throw ('Scan matrix image is not a declared runtime dependency: '+$image)}}
foreach($entry in $catalog.images){
 if($declared -cnotcontains $entry.image){throw ('Maintained runtime candidate drift: '+$entry.component)}
 $binding='(?ms)- image: '+[regex]::Escape($entry.image)+'\r?\n\s+component: '+[regex]::Escape($entry.component)+'(?:\r?\n|$)'
 if($workflow -notmatch $binding){throw ('Maintained image build is missing before scan: '+$entry.component)}
}
foreach($token in @('severity: HIGH,CRITICAL','ignore-unfixed: true',"exit-code: '1'",'needs: [container-image-scan]','Assert-TunnerCiResult.ps1')){if(-not $workflow.Contains($token)){throw ('Required fail-closed scan policy changed: '+$token)}}
Write-Output ('PASS: all'+$declared.Count+' runtime dependency images have exact scan/build coverage; security thresholds remain unchanged.')
