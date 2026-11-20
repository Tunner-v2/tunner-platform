#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot, [string]$Image)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$RepositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$defaults = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'infra/docker/.env.example'))
if ([string]::IsNullOrWhiteSpace($Image)) { $Image = [regex]::Match($defaults, '(?m)^MAILPIT_IMAGE=(\S+)').Groups[1].Value }
$maintained = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'infra/security-images/catalog.json') -Raw | ConvertFrom-Json).images | Where-Object component -eq 'mailpit'
if ($image -cne $maintained.image -and $image -notmatch '^axllent/mailpit:v[0-9.]+@sha256:[a-f0-9]{64}$') { throw 'Mailpit fixture requires a reviewed exact vendor digest or maintained catalog candidate.' }
$fixture = 'tunner-mailpit-security-' + [Guid]::NewGuid().ToString('N')
$started = $false
try {
    & docker run --detach --name $fixture --publish '127.0.0.1::8025' --publish '127.0.0.1::1025' $image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start the isolated Mailpit candidate.' }
    $started = $true
    $webBinding = (& docker port $fixture '8025/tcp').Trim()
    if ($LASTEXITCODE -ne 0 -or $webBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Candidate HTTP port is not loopback isolated.' }
    $baseUri = 'http://' + $webBinding
    $smtpBinding = (& docker port $fixture '1025/tcp').Trim()
    if ($LASTEXITCODE -ne 0 -or $smtpBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Candidate SMTP port is not loopback isolated.' }
    $smtpPort = [int]$Matches[1]
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        try { $response = Invoke-WebRequest ($baseUri + '/livez') -TimeoutSec 2; $healthy = $response.StatusCode -eq 200 } catch { $healthy = $false }
        if (-not $healthy) { Start-Sleep -Milliseconds 300 }
    } until ($healthy -or [DateTime]::UtcNow -ge $deadline)
    if (-not $healthy) { throw 'Candidate failed health readiness.' }
    $ui = Invoke-WebRequest $baseUri -TimeoutSec 10
    if ($ui.StatusCode -ne 200 -or $ui.Content -notmatch 'Mailpit') { throw 'Mailpit embedded UI is missing.' }
    $marker = 'tunner-security-fixture-' + [Guid]::NewGuid().ToString('N')
    $message = [Net.Mail.MailMessage]::new('sender@fixture.invalid','recipient@fixture.invalid',$marker,$marker)
    $client = [Net.Mail.SmtpClient]::new('127.0.0.1',$smtpPort)
    $client.Timeout = 5000
    try { $client.Send($message) } finally { $client.Dispose(); $message.Dispose() }
    $inbox = Invoke-RestMethod ($baseUri + '/api/v1/messages') -TimeoutSec 5
    $found = @($inbox.messages | Where-Object { $_.Subject -ceq $marker })
    if ($found.Count -ne 1) { throw 'SMTP message did not appear exactly once in the candidate inbox.' }
    $details = Invoke-RestMethod ($baseUri + '/api/v1/message/' + $found[0].ID) -TimeoutSec 5
    if (-not $details.Text.Contains($marker)) { throw 'Candidate message body does not match the synthetic SMTP payload.' }
    Write-Output 'PASS: 5 isolated Mailpit checks (health,embedded UI,SMTP,inbox uniqueness,body); no existing service or user inbox accessed.'
} finally {
    if ($started) {
        & docker rm --force $fixture | Out-Null
        if ($LASTEXITCODE -ne 0) { throw ('Cannot remove the owned disposable fixture: ' + $fixture) }
    }
}
