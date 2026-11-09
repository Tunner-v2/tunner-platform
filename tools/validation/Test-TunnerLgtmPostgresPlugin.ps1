#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot=Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$fixture='tunner-lgtm-pg-'+[Guid]::NewGuid().ToString('N')
$database=$fixture+'-database'
$runner=$fixture+'-tests'
$networkCreated=$false
$databaseStarted=$false
$inspection=$null
$out=Join-Path $RepositoryRoot ('artifacts/lgtm-postgres-plugin-integration/'+$fixture)
$vendorRoot=Join-Path $out 'vendor'
[IO.Directory]::CreateDirectory($vendorRoot) | Out-Null
function Invoke-CheckedDocker([string[]]$Arguments){
 $result=@(& docker @Arguments);if($LASTEXITCODE -ne 0){throw ('Isolated plugin fixture Docker operation failed: '+$Arguments[0])};return ($result -join [Environment]::NewLine).Trim()
}
try {
 $inventory=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'infra/security-images/lgtm-signing/source-inventory.json') -Raw | ConvertFrom-Json
 $spec=$inventory.plugins | Where-Object id -ceq 'grafana-postgresql-datasource'
 if($null -eq $spec -or $spec.directory -cne 'grafana-postgresql-datasource'){throw 'The reviewed PostgreSQL plugin inventory is missing.'}
 $inspection=Invoke-CheckedDocker @('create',$inventory.upstream_image)
 Invoke-CheckedDocker @('cp',($inspection+':/otel-lgtm/grafana/data/plugins-bundled/'+$spec.directory),$vendorRoot) | Out-Null
 Invoke-CheckedDocker @('rm',$inspection) | Out-Null;$inspection=$null
 Invoke-CheckedDocker @('network','create','--label',('io.tunner.fixture='+$fixture),$fixture) | Out-Null;$networkCreated=$true
 # These are the public upstream test-fixture credentials, never an operator
 # credential. No database port is published; only the isolated test network connects.
 $postgres=(Get-Content -LiteralPath (Join-Path $RepositoryRoot 'infra/security-images/catalog.json') -Raw | ConvertFrom-Json).images | Where-Object component -ceq 'postgres'
 Invoke-CheckedDocker @('run','--detach','--name',$database,'--network',$fixture,'--label',('io.tunner.fixture='+$fixture),'--tmpfs','/var/lib/postgresql','--env','POSTGRES_USER=grafanatest','--env','POSTGRES_PASSWORD=grafanatest','--env','POSTGRES_DB=grafanadstest',$postgres.image) | Out-Null;$databaseStarted=$true
 $ready=$false;$deadline=[DateTime]::UtcNow.AddSeconds(60)
 do {& docker exec $database pg_isready -q -U grafanatest -d grafanadstest 2>$null | Out-Null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Milliseconds 300}while([DateTime]::UtcNow -lt $deadline)
 if(-not $ready){throw 'The isolated plugin database is not ready.'}
 $source=Join-Path $RepositoryRoot 'infra/security-images/lgtm-signing'
 $go='golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110'
 & docker run --rm --platform linux/amd64 --name $runner --network $fixture --label ('io.tunner.fixture='+$fixture) --env CGO_ENABLED=0 --env GOTOOLCHAIN=local --env GRAFANA_TEST_DB=postgres --env ('POSTGRES_HOST='+$database) --env POSTGRES_PORT=5432 --mount 'type=volume,source=tunner-lgtm-go-mod-20261028,target=/go/pkg/mod' --mount 'type=volume,source=tunner-lgtm-runtime-go-build,target=/root/.cache/go-build' --mount ('type=bind,source='+$source+',target=/tools,readonly') --mount ('type=bind,source='+$vendorRoot+',target=/vendor-plugins,readonly') --mount ('type=bind,source='+$out+',target=/out') --workdir /tools $go go run ./cmd/rebuild-plugins -inventory /tools/source-inventory.json -vendor-root /vendor-plugins -output /out -plugin grafana-postgresql-datasource *> (Join-Path $out 'integration.log')
 if($LASTEXITCODE -ne 0){throw ('Plugin database integration failed; see '+[IO.Path]::GetRelativePath($RepositoryRoot,(Join-Path $out 'integration.log')))}
 $receipt=Join-Path $out 'provenance/grafana-postgresql-datasource/upstream-tests.json'
 $events=@(Get-Content -LiteralPath $receipt | Where-Object {$_} | ForEach-Object {$_ | ConvertFrom-Json})
 foreach($name in @('TestIntegrationPostgres','TestIntegrationPostgresSnapshots')){if(-not @($events | Where-Object {$_.PSObject.Properties.Name -contains 'Test' -and $_.Test -ceq $name -and $_.Action -ceq 'pass'}).Count){throw ('Required upstream database integration did not pass: '+$name)}}
 Write-Output ('PASS: both upstream PostgreSQL integration suites ran on the maintained isolated database; receipt '+[IO.Path]::GetRelativePath($RepositoryRoot,$receipt).Replace('\','/'))
}
finally {
 & docker rm --force $runner 2>$null | Out-Null
 if($databaseStarted){& docker rm --force $database 2>$null | Out-Null}
 if($networkCreated){& docker network rm $fixture 2>$null | Out-Null}
 if($inspection){& docker rm $inspection 2>$null | Out-Null}
}
