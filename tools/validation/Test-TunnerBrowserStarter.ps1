[CmdletBinding()]
param([string]$RepositoryRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$testRoot=Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
. (Join-Path $testRoot 'tools/dev/tunner-browser.ps1') -Command status -RepositoryRoot $testRoot -NoBrowser -SkipCertificateTrust
function Assert-BrowserCheck([bool]$Passed,[string]$Description) { if(-not $Passed){throw ('FAIL: '+$Description)} }
$testState=Read-BrowserState
Assert-BrowserCheck (Get-BrowserStatus $testState) 'the real isolated host must be ready'
$testPort=Select-LocalPort 30100
$testListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$testPort)
try { $testListener.Start(); Assert-BrowserCheck ((Select-LocalPort $testPort) -ne $testPort) 'occupied ports are skipped without changing their owner' } finally { $testListener.Stop() }
$wrongProcess=[pscustomobject]@{ProcessId=[Diagnostics.Process]::GetCurrentProcess().Id;ProcessStartedTicks=0}
$stateBytesBefore=[IO.File]::ReadAllBytes((Join-Path $testRoot 'artifacts/browser-testing/runtime.json'))
Stop-BrowserHost $wrongProcess
Assert-BrowserCheck ((Get-Process -Id $wrongProcess.ProcessId -ErrorAction Stop).Id -eq $wrongProcess.ProcessId -and [Convert]::ToBase64String($stateBytesBefore) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $testRoot 'artifacts/browser-testing/runtime.json')))) 'PID reuse leaves the unrelated process and saved state unchanged'
$wrongPin=$testState | ConvertTo-Json | ConvertFrom-Json
$wrongPin.CertificateSha256=('0'*64)
Assert-BrowserCheck (-not (Get-BrowserStatus $wrongPin)) 'wrong TLS certificate pins are rejected'
$previousMode=$env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT='Production'
    $exitCode=Invoke-Tool -File dotnet -Arguments @((Join-Path $testRoot 'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'),'--local-browser-testing',(Join-Path $testRoot 'artifacts/browser-testing/runtime.json')) -AllowFailure
    Assert-BrowserCheck ($exitCode -eq 2) 'synthetic providers refuse Production execution'
} finally { $env:ASPNETCORE_ENVIRONMENT=$previousMode }
$testCompose=Get-Content (Join-Path $testRoot 'artifacts/browser-testing/compose.json') -Raw | ConvertFrom-Json
Assert-BrowserCheck (@($testCompose.services.PSObject.Properties).Count -eq 2 -and $testCompose.services.postgres.environment.POSTGRES_DB -eq 'tunner_browser_testing' -and $testCompose.services.postgres.volumes[0].source -eq 'browser-postgres-data') 'only isolated PostgreSQL and Mailpit resources are selected'
Assert-BrowserCheck (Get-BrowserStatus $testState) 'negative checks leave the real application running'
Write-Output 'PASS: 7 browser starter safety and readiness checks'
