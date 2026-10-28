#requires -Version 7.0
[CmdletBinding()]
param([string]$Image='tunner-security/lgtm:0.38.1-r1',[string]$BaselineImage='grafana/otel-lgtm:0.34.0',[string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot=Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$sourceCommit=(& git -C $RepositoryRoot rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Cannot bind committed fixture source'}
$ownedDirty=@(& git -C $RepositoryRoot status --porcelain -- tools/validation/Test-TunnerLgtmImage.ps1 tools/validation/fixtures/lgtm-profile)
if($LASTEXITCODE -ne 0 -or $ownedDirty.Count){throw 'Commit the fixture before execution'}
$prefix='tunner-lgtm-'+[Guid]::NewGuid().ToString('N')
$output=Join-Path $RepositoryRoot ('artifacts/security-images/'+$prefix)
[IO.Directory]::CreateDirectory($output) | Out-Null
$checks=[Collections.Generic.List[string]]::new()
$containers=[Collections.Generic.List[string]]::new()
$volumes=[Collections.Generic.List[string]]::new()
$network=$prefix+'-net'
$networkCreated=$false
$credential=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('tunner-fixture:public-test-fixture'))
$headers=@{Authorization='Basic '+$credential}
function Expect([bool]$condition,[string]$name) { if(-not $condition){throw $name};$checks.Add($name);Write-Host ('PASS: '+$name) }
function Wait-For([scriptblock]$test,[string]$name,[int]$seconds=120) {
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds($seconds)
    do { try { if(& $test){$checks.Add($name);Write-Host ('PASS: '+$name);return} } catch {} ; Start-Sleep -Seconds 1 } while([DateTimeOffset]::UtcNow -lt $deadline)
    throw ('Timeout: '+$name)
}
function New-Volume([string]$name) { & docker volume create --label ('io.tunner.fixture='+$prefix) $name | Out-Null; if($LASTEXITCODE -ne 0){throw 'Fixture volume creation failed'};$volumes.Add($name) }
function Copy-Volume([string]$from,[string]$to) {
    & docker run --rm --network none --mount ('type=volume,src='+$from+',dst=/source,readonly') --mount ('type=volume,src='+$to+',dst=/target') busybox:1.37.0@sha256:bdf57e528e45e4433820e045b29b4597825a1c9e38353532d90a01445013f82e cp -a /source/. /target/
    if($LASTEXITCODE -ne 0){throw 'Owned stopped-volume backup/restore failed'}
}
function Start-Lgtm([string]$phase,[string]$reference,[string]$volume) {
    $name=$prefix+'-'+$phase
    $args=@('run','-d','--name',$name,'--label',('io.tunner.fixture='+$prefix),'--network',$network,'-v',($volume+':/data'),'-e','GF_SECURITY_ADMIN_USER=tunner-fixture','-e','GF_SECURITY_ADMIN_PASSWORD=public-test-fixture','-e','ENABLE_LOGS_ALL=true')
    foreach($port in @(3000,3100,3200,4040,4317,4318,9090)){$args+=@('-p',('127.0.0.1::'+$port))}
    $args+=$reference
    $id=(& docker @args).Trim()
    if($LASTEXITCODE -ne 0){throw ('Cannot start '+$phase+' fixture')};$containers.Add($name)
    $ports=@{}
    foreach($port in @(3000,3100,3200,4040,4317,4318,9090)){$mapping=(& docker port $name ($port.ToString()+'/tcp')).Trim();if($LASTEXITCODE -ne 0 -or $mapping -notmatch '^127\.0\.0\.1:([0-9]+)$'){throw 'Fixture port is not loopback-only'};$ports[$port]=[int]$Matches[1]}
    $instance=[pscustomobject]@{name=$name;ports=$ports;id=$id;image=$reference}
    Wait-For { & docker exec $name test -f /tmp/ready 2>$null; $LASTEXITCODE -eq 0 } ($phase+' vendor readiness sentinel') 240
    return $instance
}
function Stop-Lgtm($instance) { & docker stop --time 15 $instance.name | Out-Null;if($LASTEXITCODE -ne 0){throw 'Fixture shutdown failed'} }
function Get-Grafana($instance,[string]$path) { Invoke-RestMethod -Uri ('http://127.0.0.1:'+$instance.ports[3000]+$path) -Headers $headers -TimeoutSec 10 }
function Assert-Dashboard($instance,[string]$uid,[string]$phase) { $saved=Get-Grafana $instance ('/api/dashboards/uid/'+$uid);Expect ($saved.dashboard.title -eq 'Tunner synthetic storage fixture') ($phase+' saved dashboard preserved') }
try {
    & docker network create --internal --label ('io.tunner.fixture='+$prefix) $network | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Internal fixture network creation failed'};$networkCreated=$true
    $data=$prefix+'-data';$backup=$prefix+'-backup';$restore=$prefix+'-restore'
    foreach($v in @($data,$backup,$restore)){New-Volume $v}
    $baseline=Start-Lgtm 'baseline' $BaselineImage $data
    $uid=[Guid]::NewGuid().ToString('N')
    $dashboard=@{dashboard=@{id=$null;uid=$uid;title='Tunner synthetic storage fixture';schemaVersion=39;panels=@()};overwrite=$false}|ConvertTo-Json -Depth 6
    $created=Invoke-RestMethod -Method Post -Uri ('http://127.0.0.1:'+$baseline.ports[3000]+'/api/dashboards/db') -Headers $headers -ContentType application/json -Body $dashboard -TimeoutSec 15
    Expect ($created.status -eq 'success') 'baseline synthetic dashboard created'
    Stop-Lgtm $baseline
    Copy-Volume $data $backup
    $candidate=Start-Lgtm 'candidate' $Image $data
    Assert-Dashboard $candidate $uid 'candidate upgrade'
    $health=Get-Grafana $candidate '/api/health'
    Expect ($health.database -eq 'ok' -and $health.version -eq '13.2.4') 'Grafana13.2.4 API and SQLite healthy'
    $inventory=Get-Content (Join-Path $RepositoryRoot 'infra/security-images/lgtm-signing/source-inventory.json') -Raw|ConvertFrom-Json
    foreach($plugin in $inventory.plugins){$settings=Get-Grafana $candidate ('/api/plugins/'+$plugin.id+'/settings');Expect ($settings.signature -eq 'valid') ('native runtime signature valid: '+$plugin.id)}
    $gbase='http://127.0.0.1:'+$candidate.ports[3000]
    $html=Invoke-WebRequest -Uri $gbase -Headers $headers -TimeoutSec 15
    Expect ($html.StatusCode -eq 200 -and $html.Content -match '<html') 'Grafana retained production UI served'
    $pbase='http://127.0.0.1:'+$candidate.ports[4040]
    $phtml=Invoke-WebRequest -Uri $pbase -TimeoutSec 15
    Expect ($phtml.StatusCode -eq 200 -and $phtml.Content -notmatch 'not available in dev mode') 'Pyroscope retained embedded production UI served'
    foreach($entry in @(@{port=9090;path='/-/ready';name='Prometheus'},@{port=3100;path='/ready';name='Loki'},@{port=3200;path='/ready';name='Tempo'},@{port=4040;path='/ready';name='Pyroscope'})){
        $u='http://127.0.0.1:'+$candidate.ports[$entry.port]+$entry.path
        Wait-For { (Invoke-WebRequest -Uri $u -TimeoutSec 5).StatusCode -eq 200 } ($entry.name+' ready')
    }
    $tcp=[Net.Sockets.TcpClient]::new()
    try{$tcp.ConnectAsync('127.0.0.1',$candidate.ports[4317]).Wait(5000)|Out-Null;Expect $tcp.Connected 'OTLP gRPC TCP listener (protocol exchange not claimed)'}finally{$tcp.Dispose()}
    $marker='fixture-'+[Guid]::NewGuid().ToString('N')
    $resource=@{attributes=@(@{key='service.name';value=@{stringValue='tunner-security-fixture'}})}
    $nano=([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()*1000000L).ToString()
    $traceId=[Guid]::NewGuid().ToString('N');$spanId=[Guid]::NewGuid().ToString('N').Substring(0,16)
    $trace=@{resourceSpans=@(@{resource=$resource;scopeSpans=@(@{scope=@{name='tunner-fixture'};spans=@(@{traceId=$traceId;spanId=$spanId;name=$marker;kind=1;startTimeUnixNano=([long]$nano-1000000000L).ToString();endTimeUnixNano=$nano})})})}
    $logs=@{resourceLogs=@(@{resource=$resource;scopeLogs=@(@{scope=@{name='tunner-fixture'};logRecords=@(@{timeUnixNano=$nano;severityNumber=9;severityText='INFO';body=@{stringValue=$marker}})})})}
    $metrics=@{resourceMetrics=@(@{resource=$resource;scopeMetrics=@(@{scope=@{name='tunner-fixture'};metrics=@(@{name='tunner_security_fixture';gauge=@{dataPoints=@(@{asDouble=1.0;timeUnixNano=$nano})}})})})}
    $otlp='http://127.0.0.1:'+$candidate.ports[4318]
    foreach($signal in @(@{path='/v1/traces';payload=$trace},@{path='/v1/logs';payload=$logs},@{path='/v1/metrics';payload=$metrics})){
        $response=Invoke-WebRequest -Method Post -Uri ($otlp+$signal.path) -ContentType application/json -Body ($signal.payload|ConvertTo-Json -Depth 15 -Compress) -TimeoutSec 15
        Expect ($response.StatusCode -eq 200) ('OTLP/HTTP accepted '+$signal.path)
    }
    $tempo='http://127.0.0.1:'+$candidate.ports[3200]+'/api/traces/'+$traceId
    Wait-For { (Invoke-WebRequest -Uri $tempo -Headers @{Accept='application/json'} -TimeoutSec 5).Content -match $marker } 'Tempo returns the exact ingested trace'
    $query=[Uri]::EscapeDataString('{service_name="tunner-security-fixture"}')
    $loki='http://127.0.0.1:'+$candidate.ports[3100]+'/loki/api/v1/query_range?query='+$query+'&limit=20'
    Wait-For { (Invoke-WebRequest -Uri $loki -TimeoutSec 5).Content -match $marker } 'Loki returns the exact ingested log'
    $prom='http://127.0.0.1:'+$candidate.ports[9090]+'/api/v1/query?query=tunner_security_fixture'
    Wait-For { $r=Invoke-RestMethod -Uri $prom -TimeoutSec 5;@($r.data.result).Count -gt 0 -and [double]$r.data.result[0].value[1] -eq 1 } 'Prometheus returns the exact ingested gauge'
    $profileSource=Join-Path $PSScriptRoot 'fixtures/lgtm-profile'
    & docker run --rm --network none --cap-drop ALL --read-only --tmpfs /tmp:rw,nosuid,size=1073741824 -e GOCACHE=/tmp/go-cache -e GOMAXPROCS=2 --mount ('type=bind,src='+$profileSource+',dst=/src,readonly') --mount ('type=bind,src='+$output+',dst=/out') golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 go run /src/main.go /out/synthetic.pprof
    if($LASTEXITCODE -ne 0){throw 'Synthetic isolated profile generation failed'}
    $profile=Invoke-WebRequest -Method Post -Uri ($pbase+'/ingest?name=tunner-security-fixture.goroutine') -Form @{profile=Get-Item (Join-Path $output 'synthetic.pprof')} -TimeoutSec 15
    Expect ($profile.StatusCode -eq 200) 'Pyroscope accepts synthetic pprof profile'
    Wait-For { $r=Invoke-RestMethod -Method Post -Uri ($pbase+'/querier.v1.QuerierService/ProfileTypes') -Headers @{'Connect-Protocol-Version'='1'} -ContentType application/json -Body '{}' -TimeoutSec 5;@($r.profileTypes).Count -gt 0 } 'Pyroscope queries ingested profile types'
    Stop-Lgtm $candidate
    $restarted=Start-Lgtm 'restart' $Image $data
    Assert-Dashboard $restarted $uid 'candidate restart'
    Stop-Lgtm $restarted
    Copy-Volume $backup $restore
    $rollback=Start-Lgtm 'rollback' $BaselineImage $restore
    Assert-Dashboard $rollback $uid 'baseline backup restore rollback'
    Stop-Lgtm $rollback
    [ordered]@{schema_version=1;scope_id='P1-RV001-001';source_commit=$sourceCommit;image=$Image;image_id=(& docker image inspect $Image --format '{{.Id}}').Trim();baseline=$BaselineImage;result='PASS_DEV_TEST_COMPATIBILITY';passed=$checks.Count;checks=$checks;fixture_scope=$prefix;operator_data_used=$false;operator_credentials_used=$false;kernel_bpf_instrumentation='NOT_EXECUTED_OPTIONAL_NOT_ENABLED';grpc='TCP_LISTENER_ONLY';production_authorized=$false}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $output 'compatibility.json') -Encoding utf8NoBOM
    Write-Output ('PASS: '+$checks.Count+' complete LGTM dev/test compatibility checks. Evidence: '+$output)
} catch {
    [ordered]@{schema_version=1;scope_id='P1-RV001-001';source_commit=$sourceCommit;image=$Image;result='FAIL_COMPATIBILITY';passed=$checks.Count;checks=$checks;reason=$_.Exception.Message;fixture_scope=$prefix;operator_data_used=$false;operator_credentials_used=$false}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $output 'compatibility.json') -Encoding utf8NoBOM
    throw
} finally {
    foreach($name in $containers){ & docker logs $name > (Join-Path $output ($name+'.log')) 2>&1; $owner=@(& docker inspect $name --format '{{index .Config.Labels "io.tunner.fixture"}}' 2>$null);if(($owner -join '').Trim() -ceq $prefix){ & docker rm -f $name | Out-Null } }
    foreach($v in $volumes){$owner=@(& docker volume inspect $v --format '{{index .Labels "io.tunner.fixture"}}' 2>$null);if(($owner -join '').Trim() -ceq $prefix){& docker volume rm $v | Out-Null}}
    if($networkCreated){$owner=@(& docker network inspect $network --format '{{index .Labels "io.tunner.fixture"}}' 2>$null);if(($owner -join '').Trim() -ceq $prefix){& docker network rm $network | Out-Null}}
}
