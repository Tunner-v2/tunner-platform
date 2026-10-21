#requires -Version 7.0
[CmdletBinding()]
param([string]$Image = 'tunner-security/openbao:2.7.1-r1', [string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$fixture = 'tunner-openbao-security-' + [Guid]::NewGuid().ToString('N')
$volume = $fixture + '-data'
$directory = Join-Path $RepositoryRoot ('artifacts/security-images/fixtures/'+$fixture)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$config = Join-Path $directory 'fixture.hcl'
@'
ui = true
disable_mlock = true
storage "raft" {
  path = "/openbao/file"
}
listener "tcp" {
  address = "0.0.0.0:8200"
  tls_disable = true
}
api_addr = "http://127.0.0.1:8200"
cluster_addr = "http://127.0.0.1:8201"
'@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM
$started = $false
$volumeCreated = $false
$checks = [Collections.Generic.List[string]]::new()
function Invoke-FixtureDocker {
    param([string[]]$Arguments)
    $value = @(& docker @Arguments)
    if ($LASTEXITCODE -ne 0) { throw ('Docker fixture operation failed: '+$Arguments[0]) }
    return ($value -join [Environment]::NewLine).Trim()
}
function Invoke-FixtureRequest {
    param([string]$Method='GET',[string]$Path,[object]$Body,[hashtable]$Headers=@{},[int]$Expected=200)
    $request = @{Method=$Method;Uri=($script:baseUri+$Path);Headers=$Headers;SkipHttpErrorCheck=$true;TimeoutSec=5}
    if ($null -ne $Body) { $request.Body=$Body | ConvertTo-Json -Depth 8 -Compress; $request.ContentType='application/json' }
    $response = Invoke-WebRequest @request
    if ($response.StatusCode -ne $Expected) { throw ('Unexpected fixture response for '+$Path+': '+$response.StatusCode+'; response body withheld.') }
    if (-not [string]::IsNullOrWhiteSpace($response.Content)) { return $response.Content | ConvertFrom-Json }
}
function Wait-FixtureHealth {
    param([int]$Expected)
    $deadline=[DateTime]::UtcNow.AddSeconds(45)
    do {
        try { return (Invoke-FixtureRequest -Path '/v1/sys/health' -Expected $Expected) } catch { Start-Sleep -Milliseconds 300 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Disposable OpenBao did not reach the expected health state.'
}
try {
    $version = Invoke-FixtureDocker -Arguments @('run','--rm','--entrypoint','bao',$Image,'version')
    if ($version -notmatch 'OpenBao v2\.7\.1') { throw 'Unexpected OpenBao version.' }
    $checks.Add('OpenBao 2.7.1 release metadata preserved')
    Invoke-FixtureDocker -Arguments @('volume','create','--label','io.tunner.fixture=security-validation',$volume) | Out-Null
    $volumeCreated=$true
    Invoke-FixtureDocker -Arguments @('run','--detach','--name',$fixture,'--user','0','--publish','127.0.0.1::8200','--mount',('type=volume,source='+$volume+',target=/openbao/file'),'--mount',('type=bind,source='+$config+',target=/openbao/config/fixture.hcl,readonly'),$Image,'server','-config=/openbao/config/fixture.hcl') | Out-Null
    $started=$true
    $binding=Invoke-FixtureDocker -Arguments @('port',$fixture,'8200/tcp')
    if ($binding -notmatch '^127\.0\.0\.1:\d+$') { throw 'Fixture API must use a random loopback-only port.' }
    $script:baseUri='http://'+$binding
    Wait-FixtureHealth 501 | Out-Null
    # Tokens and unseal material below are created only for this disposable fixture;
    # they are never written to disk, stdout, process arguments, or operator services.
    $initial=Invoke-FixtureRequest -Method POST -Path '/v1/sys/init' -Body @{secret_shares=1;secret_threshold=1}
    $root=@{'X-Vault-Token'=$initial.root_token}
    Invoke-FixtureRequest -Method POST -Path '/v1/sys/unseal' -Body @{key=$initial.keys_base64[0]} | Out-Null
    Wait-FixtureHealth 200 | Out-Null
    $checks.Add('Raft initialization and unseal')
    $ui=Invoke-WebRequest ($script:baseUri+'/ui/') -TimeoutSec 5
    if ($ui.StatusCode -ne 200 -or $ui.Content -notmatch 'OpenBao') { throw 'Released embedded UI is missing.' }
    $checks.Add('released embedded UI remains available')
    Invoke-FixtureRequest -Method POST -Path '/v1/sys/mounts/security-fixture' -Headers $root -Body @{type='kv';options=@{version='2'}} -Expected 204 | Out-Null
    Invoke-FixtureRequest -Method PUT -Path '/v1/sys/policies/acl/security-fixture' -Headers $root -Body @{policy='path "security-fixture/data/allowed" { capabilities = ["create", "read", "update"] }'} -Expected 204 | Out-Null
    $issued=Invoke-FixtureRequest -Method POST -Path '/v1/auth/token/create' -Headers $root -Body @{policies=@('security-fixture');ttl='5m';explicit_max_ttl='5m';no_default_policy=$true}
    $scoped=@{'X-Vault-Token'=$issued.auth.client_token}
    $marker=[Guid]::NewGuid().ToString('N')
    Invoke-FixtureRequest -Method POST -Path '/v1/security-fixture/data/allowed' -Headers $scoped -Body @{options=@{cas=0};data=@{value=$marker}} | Out-Null
    $actual=Invoke-FixtureRequest -Path '/v1/security-fixture/data/allowed' -Headers $scoped
    if ($actual.data.data.value -cne $marker) { throw 'KV v2 round trip failed.' }
    $checks.Add('scoped expiring token and KV v2 round trip')
    Invoke-FixtureRequest -Path '/v1/security-fixture/data/sibling' -Headers $scoped -Expected 403 | Out-Null
    $checks.Add('ACL denies unrelated paths')
    Invoke-FixtureRequest -Method POST -Path '/v1/security-fixture/data/allowed' -Headers $scoped -Body @{options=@{cas=0};data=@{value='must-not-replace'}} -Expected 400 | Out-Null
    $actual=Invoke-FixtureRequest -Path '/v1/security-fixture/data/allowed' -Headers $scoped
    if ($actual.data.data.value -cne $marker) { throw 'Create-only CAS write was not preserved.' }
    $checks.Add('KV create-only CAS rejects replacement')
    Invoke-FixtureDocker -Arguments @('restart',$fixture) | Out-Null
    Wait-FixtureHealth 503 | Out-Null
    Invoke-FixtureRequest -Method POST -Path '/v1/sys/unseal' -Body @{key=$initial.keys_base64[0]} | Out-Null
    Wait-FixtureHealth 200 | Out-Null
    $actual=Invoke-FixtureRequest -Path '/v1/security-fixture/data/allowed' -Headers $scoped
    if ($actual.data.data.value -cne $marker) { throw 'Raft storage or scoped token did not survive restart.' }
    $checks.Add('Raft data and token persist through restart and re-unseal')
    Invoke-FixtureRequest -Method POST -Path '/v1/auth/token/revoke-self' -Headers $scoped -Body @{} -Expected 204 | Out-Null
    Invoke-FixtureRequest -Path '/v1/security-fixture/data/allowed' -Headers $scoped -Expected 403 | Out-Null
    $checks.Add('revoked scoped token is denied')
    Write-Output ('PASS: '+$checks.Count+' isolated OpenBao security-image checks')
} finally {
    if ($started) { Invoke-FixtureDocker -Arguments @('rm','--force',$fixture) | Out-Null }
    if ($volumeCreated) { Invoke-FixtureDocker -Arguments @('volume','rm',$volume) | Out-Null }
    Remove-Item -LiteralPath $config -Force
    Remove-Item -LiteralPath $directory
}
