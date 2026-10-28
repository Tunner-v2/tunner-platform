#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^tunner-security/lgtm:[a-zA-Z0-9_.-]+$')][string]$Image,
    [ValidateSet('dev','test')][string]$Environment = 'dev',
    [string]$RepositoryRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$scanner = 'aquasec/trivy:0.70.0@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e'
$go = 'golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110'
$runId = [Guid]::NewGuid().ToString('N')
$output = Join-Path $RepositoryRoot ('artifacts/security-images/lgtm-scan-'+$runId)
[IO.Directory]::CreateDirectory($output) | Out-Null
$approved = Join-Path $RepositoryRoot 'governance/evidence/security-20261028/tempo-vex'
$source = Join-Path $RepositoryRoot 'infra/security-images/lgtm-signing'
$imageId = (& docker image inspect $Image --format '{{.Id}}').Trim()
if ($LASTEXITCODE -ne 0 -or $imageId -notmatch '^sha256:[a-f0-9]{64}$') { throw 'Cannot bind immutable local image.' }
$scanArgs = @('image','--scanners','vuln','--severity','HIGH,CRITICAL','--ignore-unfixed','--exit-code','1','--format','json','--list-all-pkgs')
& docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v tunner-trivy-ci-cache:/root/.cache/trivy --mount ('type=bind,src='+$output+',dst=/audit') $scanner @scanArgs --output /audit/unfiltered.json $imageId
$rawExit = $LASTEXITCODE
if ($rawExit -notin @(0,1)) { throw 'Unfiltered scanner failed to execute; VEX is not attempted.' }
$raw = Get-Content (Join-Path $output 'unfiltered.json') -Raw | ConvertFrom-Json
# Coverage never shrinks when signed/updated executables replace vendor binaries.
$expectedTargets = @('otel-lgtm/grafana/bin/grafana','otel-lgtm/loki/loki','otel-lgtm/obi/obi','otel-lgtm/otelcol-contrib/otelcol-contrib','otel-lgtm/prometheus/prometheus','otel-lgtm/prometheus/promtool','otel-lgtm/pyroscope/pyroscope','otel-lgtm/tempo/tempo','otel-lgtm/tempo/tempo-cli','otel-lgtm/tempo/tempo-query')
$inventory = Get-Content (Join-Path $source 'source-inventory.json') -Raw | ConvertFrom-Json
if (@($inventory.plugins).Count -ne 13) { throw 'The reviewed complete plugin inventory must contain13 entries.' }
foreach ($plugin in $inventory.plugins) { $expectedTargets += 'otel-lgtm/grafana/data/plugins-bundled/'+$plugin.directory+'/'+$plugin.binary }
$targets = @($raw.Results | Where-Object Type -eq 'gobinary' | ForEach-Object Target)
$coverageDifference = @(Compare-Object -ReferenceObject $expectedTargets -DifferenceObject $targets)
if ($coverageDifference.Count -ne 0) { throw 'Executable scan coverage differs from complete 23-binary inventory.' }
if (-not @($raw.Results | Where-Object Class -eq 'os-pkgs').Count) { throw 'Operating-system scan coverage is missing.' }
if (-not @($raw.Results | Where-Object Type -eq 'node-pkg').Count) { throw 'Vendor Node package scan coverage is missing.' }
$matching = @($raw.Results | ForEach-Object { if ($_.PSObject.Properties.Name -contains 'Vulnerabilities') { $_.Vulnerabilities } } | Where-Object { $_.PkgName -eq 'github.com/grafana/tempo' -and $_.InstalledVersion -eq 'v1.5.1-0.20261021152436-9db7df79d2af' -and $_.VulnerabilityID -in @('CVE-2026-21728','CVE-2026-28377') })
# Extract immutable evidence without starting any service.
$container = $null
try {
    $container = (& docker create $imageId /inspection-only).Trim()
    if ($LASTEXITCODE -ne 0 -or $container -notmatch '^[a-f0-9]{64}$') { throw 'Cannot isolate exact evidence extraction.' }
    & docker cp ($container+':/usr/share/tunner/obi-java-agent.sbom.json') (Join-Path $output 'obi-java-agent.sbom.json')
    if ($LASTEXITCODE -ne 0) { throw 'The pinned embedded-Java runtime SBOM is missing.' }
    if ($matching.Count -gt 0) {
        & docker cp ($container+':/otel-lgtm/grafana/data/plugins-bundled/tempo/gpx_grafana-tempo-datasource_linux_amd64') (Join-Path $output 'tempo-plugin')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot extract the reviewed Tempo executable.' }
    }
} finally {
    if ($container -match '^[a-f0-9]{64}$') { & docker rm $container | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Cannot remove owned inspection container.' } }
}
$javaSbom = Join-Path $output 'obi-java-agent.sbom.json'
if ((Get-FileHash -LiteralPath $javaSbom -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inventory.obi_embedded_java.sbom_sha256) { throw 'Embedded Java SBOM differs from exact reviewed upstream source.' }
& docker run --rm -v tunner-trivy-ci-cache:/root/.cache/trivy --mount ('type=bind,src='+$output+',dst=/audit') $scanner sbom --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --format json --list-all-pkgs --output /audit/embedded-java-scan.json /audit/obi-java-agent.sbom.json
if ($LASTEXITCODE -ne 0) { throw 'Embedded Java findings remain blocking.' }
$javaScan = Get-Content (Join-Path $output 'embedded-java-scan.json') -Raw | ConvertFrom-Json
$javaPackages = @($javaScan.Results | Where-Object Type -eq 'jar' | ForEach-Object Packages)
foreach ($dependency in $inventory.obi_embedded_java.runtime_dependencies) {
    if (@($javaPackages | Where-Object { $_.Name -ceq $dependency.name -and $_.Version -ceq $dependency.version }).Count -ne 1) { throw 'Embedded Java package scan coverage is incomplete.' }
}
$correctedExit = $rawExit
$vexUsed = $false
if ($matching.Count -gt 0) {
    $guardArgs = @('run','--rm','--network','none','--cap-drop','ALL','--read-only','--tmpfs','/tmp:rw,nosuid,size=1073741824','-e','GOCACHE=/tmp/go-cache','-e','GOMAXPROCS=2','--mount',('type=bind,src='+$source+',dst=/src,readonly'),'--mount',('type=bind,src='+$approved+',dst=/approved,readonly'),'--mount',('type=bind,src='+$output+',dst=/audit,readonly'),'-w','/src',$go,'go','run','./cmd/vex-check','--approved-dir','/approved','--binary','/audit/tempo-plugin','--raw-scan','/audit/unfiltered.json','--image-id',$imageId)
    & docker @guardArgs | Set-Content (Join-Path $output 'guard.json') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Approval/source/hash/context/tests/review/expiry guard rejected VEX; raw failure remains blocking.' }
    & docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v tunner-trivy-ci-cache:/root/.cache/trivy --mount ('type=bind,src='+$output+',dst=/audit') --mount ('type=bind,src='+$approved+',dst=/approved,readonly') $scanner @scanArgs --show-suppressed --vex /approved/tempo-vex.json --output /audit/corrected.json $imageId
    $correctedExit = $LASTEXITCODE
    if ($correctedExit -notin @(0,1)) { throw 'Corrected scanner execution failed.' }
    $vexUsed = $true
}
$receipt = [ordered]@{schema_version=1;scope_id='P1-RV001-001';environment=$Environment;source_commit=(& git -C $RepositoryRoot rev-parse HEAD).Trim();image=$Image;image_id=$imageId;binary_targets=$targets.Count;embedded_java_dependencies=@($inventory.obi_embedded_java.runtime_dependencies).Count;embedded_java_scan='embedded-java-scan.json';raw_exit_code=$rawExit;vex_used=$vexUsed;corrected_exit_code=$correctedExit;raw_report='unfiltered.json';corrected_report=$(if($vexUsed){'corrected.json'}else{$null});production_authorized=$false;status=$(if($correctedExit -eq 0){'PASS_SCAN_ONLY'}else{'FAIL_SCAN'})}
$receipt | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'receipt.json') -Encoding utf8NoBOM
if ($correctedExit -ne 0) { throw ('Applicable HIGH/CRITICAL findings remain blocking. Evidence: '+$output) }
Write-Output ('PASS: full LGTM image scan; compatibility/release acceptance remains separate. Evidence: '+$output)
