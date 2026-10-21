#requires -Version 7.0
[CmdletBinding()]
param([string]$Image = 'tunner-security/postgres:18.6-r1', [string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$fixture = 'tunner-postgres-security-' + [Guid]::NewGuid().ToString('N')
$volume = $fixture + '-data'
$started = $false
$volumeCreated = $false
$checks = [Collections.Generic.List[string]]::new()
function Invoke-FixtureDocker {
    param([string[]]$Arguments)
    $value = @(& docker @Arguments)
    if ($LASTEXITCODE -ne 0) { throw ('Docker fixture operation failed: ' + $Arguments[0]) }
    return ($value -join "\n").Trim()
}
function Wait-Postgres {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        & docker exec $fixture pg_isready -q -U tunner -d fixture 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The disposable database did not become ready.'
}
function Invoke-FixtureSql {
    param([string]$Sql)
    Invoke-FixtureDocker -Arguments @('exec',$fixture,'psql','--no-psqlrc','--tuples-only','--no-align','--set','ON_ERROR_STOP=1','-U','tunner','-d','fixture','-c',$Sql)
}
try {
    $version = Invoke-FixtureDocker -Arguments @('run','--rm','--entrypoint','postgres',$Image,'--version')
    if ($version -notmatch '^postgres \(PostgreSQL\) 18\.6 ') { throw 'Unexpected PostgreSQL version.' }
    $checks.Add('PostgreSQL remains version 18.6')
    $identity = Invoke-FixtureDocker -Arguments @('run','--rm','--entrypoint','gosu',$Image,'postgres','id','-un')
    if ($identity -cne 'postgres') { throw 'gosu failed the upstream privilege transition.' }
    $checks.Add('gosu changes identity to postgres')
    Invoke-FixtureDocker -Arguments @('volume','create','--label','io.tunner.fixture=security-validation',$volume) | Out-Null
    $volumeCreated = $true
    Invoke-FixtureDocker -Arguments @('run','--detach','--name',$fixture,'--network','none','--mount',('type=volume,source='+$volume+',target=/var/lib/postgresql'),'-e','POSTGRES_DB=fixture','-e','POSTGRES_USER=tunner','-e','POSTGRES_HOST_AUTH_METHOD=trust',$Image) | Out-Null
    $started = $true
    Wait-Postgres
    $checks.Add('vendor entrypoint initializes an isolated database')
    if ((Invoke-FixtureSql 'SHOW data_directory') -cne '/var/lib/postgresql/18/docker') { throw 'Vendor PostgreSQL 18 data layout changed.' }
    $checks.Add('PostgreSQL 18 data layout preserved')
    if ((Invoke-FixtureSql "SELECT datcollate FROM pg_database WHERE datname=current_database()") -cne 'en_US.utf8') { throw 'Baseline database collation changed.' }
    $checks.Add('baseline en_US.utf8 collation preserved')
    Invoke-FixtureSql 'CREATE EXTENSION pgcrypto; CREATE TABLE security_fixture(id integer PRIMARY KEY, body jsonb NOT NULL); INSERT INTO security_fixture VALUES(1,jsonb_build_object(''valid'',true));' | Out-Null
    if ((Invoke-FixtureSql "SELECT (body->>'valid')::boolean AND length(encode(digest('fixture','sha256'),'hex'))=64 FROM security_fixture WHERE id=1") -cne 't') { throw 'JSON or pgcrypto compatibility failed.' }
    $checks.Add('JSON and pgcrypto round trip')
    Invoke-FixtureSql 'BEGIN; INSERT INTO security_fixture VALUES(2,''{}''); ROLLBACK;' | Out-Null
    if ((Invoke-FixtureSql 'SELECT count(*) FROM security_fixture') -cne '1') { throw 'Transaction rollback failed.' }
    $checks.Add('transaction rollback preserved')
    Invoke-FixtureDocker -Arguments @('restart',$fixture) | Out-Null
    Wait-Postgres
    if ((Invoke-FixtureSql 'SELECT count(*) FROM security_fixture') -cne '1') { throw 'Fixture data did not persist across restart.' }
    $checks.Add('fixture storage persists across restart')
    Write-Output ('PASS: '+$checks.Count+' isolated PostgreSQL security-image checks')
} finally {
    if ($started) { Invoke-FixtureDocker -Arguments @('rm','--force',$fixture) | Out-Null }
    if ($volumeCreated) { Invoke-FixtureDocker -Arguments @('volume','rm',$volume) | Out-Null }
}
