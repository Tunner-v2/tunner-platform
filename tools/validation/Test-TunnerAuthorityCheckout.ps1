[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RepositoryRoot, [string]$Commit = 'HEAD')
$ErrorActionPreference='Stop'
$resolved=(& git -C $RepositoryRoot rev-parse --verify "$Commit^{commit}" | Out-String).Trim()
if($LASTEXITCODE -ne 0 -or $resolved -notmatch '^[a-f0-9]{40}$'){throw 'Commit must resolve to a local committed SHA.'}
$integrity=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'governance/evidence/BOOT-P0-001-integrity.json') -Raw | ConvertFrom-Json
$checks=@($integrity.checks)+@($integrity.amendment_checks)
$verified=0
foreach($check in $checks | Where-Object {$_.local_path -like 'docs/authority/*'}){
  $path=[string]$check.local_path
  if($path.Contains('"') -or $path.Contains('..') -or [IO.Path]::IsPathRooted($path)){throw "Invalid authority path: $path"}
  $start=[Diagnostics.ProcessStartInfo]::new()
  $start.FileName='git'
  $start.WorkingDirectory=$RepositoryRoot
  $start.Arguments='cat-file --filters "'+$resolved+':'+$path+'"'
  $start.UseShellExecute=$false
  $start.CreateNoWindow=$true
  $start.RedirectStandardOutput=$true
  $start.RedirectStandardError=$true
  $process=[Diagnostics.Process]::Start($start)
  $bytes=[IO.MemoryStream]::new()
  try {
    $process.StandardOutput.BaseStream.CopyTo($bytes)
    $errorText=$process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw ('Committed authority read failed for '+$path+': '+$errorText)}
    $hasher=[Security.Cryptography.SHA256]::Create()
    try {$hash=([BitConverter]::ToString($hasher.ComputeHash($bytes.ToArray()))).Replace('-','').ToLowerInvariant()}
    finally {$hasher.Dispose()}
    if($hash -ne $check.expected_sha256){throw "Byte-exact committed checkout mismatch: $path"}
    $verified++
  } finally {$bytes.Dispose();$process.Dispose()}
}
$zip='docs/authority/baselines/1.6.0/Tunner_Development_Documentation_Baseline_1.6.0_LOCKED.zip'
$blob=(& git -C $RepositoryRoot rev-parse ($resolved+':'+$zip) | Out-String).Trim()
$working=(& git -C $RepositoryRoot hash-object -- $zip | Out-String).Trim()
$raw=(& git -C $RepositoryRoot hash-object --no-filters -- $zip | Out-String).Trim()
if($blob -ne $working -or $working -ne $raw){throw 'Authority ZIP binary filter/raw/blob portability mismatch.'}
[ordered]@{status='PASS';validated_commit_sha=$resolved;byte_exact_checkout_checks=$verified;zip_blob_hash=$blob;zip_working_tree_hash=$working;zip_raw_hash=$raw;zip_sha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $zip) -Algorithm SHA256).Hash.ToLowerInvariant();zip_attributes=((& git -C $RepositoryRoot check-attr -a -- $zip)|Out-String).Trim();boundary='Reads committed blobs through declared checkout filters without cloning, moving, replacing the baseline, or changing verifier semantics.'}|ConvertTo-Json -Depth 6
