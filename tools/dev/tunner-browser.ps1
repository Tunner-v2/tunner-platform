[CmdletBinding()]
param(
    [ValidateSet('start','stop','restart','status','logs')][string]$Command = 'start',
    [string]$RepositoryRoot,
    [switch]$NoBrowser,
    [switch]$SkipCertificateTrust
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Tunner.RepositoryRoot.psm1') -Force
$repositoryRoot = Resolve-TunnerRepositoryRoot -RepositoryRoot $RepositoryRoot -StartDirectory $PSScriptRoot
$dataRoot = Join-Path $repositoryRoot 'artifacts/browser-testing'
$statePath = Join-Path $dataRoot 'runtime.json'
$composePath = Join-Path $dataRoot 'compose.json'
$hostProject = Join-Path $repositoryRoot 'tools/browser-testing/Tunner.BrowserTesting.csproj'
$hostAssembly = Join-Path $repositoryRoot 'tools/browser-testing/bin/Debug/net10.0/Tunner.BrowserTesting.dll'
$hash = [Security.Cryptography.SHA256]::Create()
try { $rootHash = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($repositoryRoot.ToLowerInvariant()))).Replace('-','').Substring(0,10).ToLowerInvariant() } finally { $hash.Dispose() }
$projectName = 'tunner-browser-' + $rootHash

# Per-request certificate pinning for readiness works in Windows PowerShell 5.1 and PowerShell 7.
# It never changes global certificate validation or accepts an arbitrary certificate.
if (-not ('TunnerBrowserLocalHttps' -as [type])) {
    # Keep the .NET Framework-compatible certificate-pinned bridge on Windows PowerShell 5.1.
    # Modern PowerShell compilers diagnose this API as obsolete; suppress only that compatibility diagnostic.
    $browserCompileOptions = @{}
    if ($PSVersionTable.PSVersion.Major -ge 7) { $browserCompileOptions.CompilerOptions = @('/nowarn:SYSLIB0014') }
    Add-Type @browserCompileOptions -WarningAction SilentlyContinue -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
public static class TunnerBrowserLocalHttps {
    public static string Get(string url, string pin) {
        var uri = new Uri(url);
        if (!uri.IsLoopback || uri.Scheme != "https" || uri.Host != "localhost" || pin.Length != 64)
            throw new InvalidOperationException("Only the pinned local test HTTPS endpoint is supported.");
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.Timeout = 4000;
        request.ReadWriteTimeout = 4000;
        request.AllowAutoRedirect = false;
        request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => {
            if (certificate == null) return false;
            using (var hash = SHA256.Create())
                return string.Equals(BitConverter.ToString(hash.ComputeHash(certificate.GetRawCertData())).Replace("-", ""), pin, StringComparison.OrdinalIgnoreCase);
        };
        using (var response = request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
    }
}
'@
}
function Invoke-Tool {
    param([string]$File, [string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot, [switch]$Capture, [switch]$AllowFailure)
    $resolved = Get-Command $File -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $quoted = ($Arguments | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $resolved.Source
    $info.Arguments = $quoted
    if ($resolved.Source.EndsWith('.cmd',[StringComparison]::OrdinalIgnoreCase)) {
        $info.FileName = $env:ComSpec
        $info.Arguments = '/d /s /c ""' + $resolved.Source + '" ' + $quoted + '"'
    }
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $outTask.GetAwaiter().GetResult()
        $errors = $errTask.GetAwaiter().GetResult()
        $code = $process.ExitCode
    } finally { $process.Dispose() }
    if ($AllowFailure) { return $code }
    if ($code -ne 0) {
        if ($output) { Write-Host $output }
        if ($errors) { Write-Host $errors }
        throw ($File + ' ' + $Arguments[0] + ' failed (exit ' + $code + ').')
    }
    if ($Capture) { return $output }
    if ($output) { Write-Host $output }
    if ($errors) { Write-Host $errors }
}
function Invoke-BrowserCompose {
    param([string[]]$Arguments, [switch]$Capture)
    Invoke-Tool -File docker -Arguments (@('compose','--project-name',$projectName,'--file',$composePath) + $Arguments) -Capture:$Capture
}
function Read-BrowserState {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { return $null }
    $value = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($value.Mode -ne 'ISOLATED_LOCAL_BROWSER_TESTING' -or $value.ProjectName -ne $projectName) { throw 'The saved test state belongs to another checkout or mode. No process or service was changed.' }
    foreach ($port in @($value.ApplicationPort,$value.PostgresPort,$value.SmtpPort,$value.MailpitPort)) {
        if ($port -lt 1024 -or $port -gt 65535) { throw 'Invalid saved local port.' }
    }
    return $value
}
function Write-BrowserState {
    param($Value)
    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding UTF8
}
function Get-BrowserStatus {
    param($State)
    if ($null -eq $State) { return $false }
    try {
        $status = [TunnerBrowserLocalHttps]::Get(('https://localhost:' + $State.ApplicationPort + '/browser-testing/status'),$State.CertificateSha256) | ConvertFrom-Json
        return $status.ready -and $status.mode -eq $State.Mode -and $status.instanceId -eq $State.InstanceId
    } catch { return $false }
}
function Show-BrowserAddresses {
    param($State)
    Write-Host ('Application: https://localhost:' + $State.ApplicationPort + '/registration')
    Write-Host ('Verification inbox: http://127.0.0.1:' + $State.MailpitPort)
    Write-Host ('PostgreSQL: 127.0.0.1:' + $State.PostgresPort + ' -> container 5432; database tunner_browser_testing')
    Write-Host 'ISOLATED LOCAL TEST DATA ONLY. Registration, verification and Personal Information are available. Sign-in and later P2 features are still under development.'
}
function Open-BrowserAddresses {
    param($State)
    if (-not $NoBrowser) {
        Start-Process ('https://localhost:' + $State.ApplicationPort + '/registration')
        Start-Process ('http://127.0.0.1:' + $State.MailpitPort)
    }
}
function Stop-BrowserHost {
    param($State)
    if ($null -eq $State -or $State.ProcessId -eq 0) { return }
    $process = Get-Process -Id $State.ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $details = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $State.ProcessId)
        if ($process.StartTime.ToUniversalTime().Ticks -ne $State.ProcessStartedTicks -or
            $null -eq $details -or $details.CommandLine.IndexOf($hostAssembly,[StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Write-Warning 'The saved PID now belongs to another process. It was left running; this stale host reference is ignored.'
            return
        }
        Stop-Process -Id $State.ProcessId -ErrorAction Stop
        $process.WaitForExit(10000) | Out-Null
    }
    $State.ProcessId = 0
    $State.ProcessStartedTicks = 0
    Write-BrowserState $State
}
function Select-LocalPort {
    param([int]$Preferred)
    for ($candidate = $Preferred; $candidate -lt $Preferred + 50; $candidate++) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$candidate)
        try { $listener.Start(); return $candidate } catch [Net.Sockets.SocketException] { } finally { $listener.Stop() }
    }
    throw ('No free local port near ' + $Preferred + '. Existing port owners were not changed.')
}
function Assert-BrowserPrerequisites {
    foreach ($tool in @('docker','dotnet','node','pnpm.cmd')) { Get-Command $tool -CommandType Application -ErrorAction Stop | Out-Null }
    $sdk = Invoke-Tool -File dotnet -Arguments @('--version') -Capture
    if ($sdk.Trim() -notmatch '^10\.') { throw '.NET SDK 10 is required; follow the repository global.json.' }
    $nodeVersion = Invoke-Tool -File node -Arguments @('--version') -Capture
    if ([version]$nodeVersion.Trim().TrimStart('v') -lt [version]'24.15.0' -or [version]$nodeVersion.Trim().TrimStart('v') -ge [version]'25.0.0') { throw 'Node.js >=24.15 and <25 is required by web/package.json.' }
    $pnpmVersion = Invoke-Tool -File pnpm.cmd -Arguments @('--version') -WorkingDirectory (Join-Path $repositoryRoot 'web') -Capture
    if ($pnpmVersion.Trim() -ne '11.19.0') { throw 'pnpm 11.19.0 is required by web/package.json.' }
    if ((Invoke-Tool -File docker -Arguments @('info','--format','{{.ServerVersion}}') -AllowFailure) -ne 0) {
        $dockerCommand = Get-Command docker -CommandType Application | Select-Object -First 1
        $desktop = Join-Path (Split-Path (Split-Path (Split-Path $dockerCommand.Source))) 'Docker Desktop.exe'
        if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) { throw 'Start Docker Desktop, then run the starter again.' }
        Start-Process -FilePath $desktop -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddMinutes(2)
        while ((Invoke-Tool -File docker -Arguments @('info','--format','{{.ServerVersion}}') -AllowFailure) -ne 0) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Docker Desktop did not become ready within two minutes.' }
            Start-Sleep -Seconds 2
        }
    }
    Invoke-Tool -File docker -Arguments @('compose','version')
    if ((Invoke-Tool -File dotnet -Arguments @('dev-certs','https','--check') -AllowFailure) -ne 0) { Invoke-Tool -File dotnet -Arguments @('dev-certs','https') }
    if (-not $SkipCertificateTrust -and (Invoke-Tool -File dotnet -Arguments @('dev-certs','https','--check','--trust') -AllowFailure) -ne 0) {
        Write-Host 'Preparing trusted localhost HTTPS. Windows may ask you to confirm the ASP.NET development certificate once.'
        Invoke-Tool -File dotnet -Arguments @('dev-certs','https','--trust')
        if ((Invoke-Tool -File dotnet -Arguments @('dev-certs','https','--check','--trust') -AllowFailure) -ne 0) { throw 'Local HTTPS certificate trust was not completed. Run the starter again after approving the Windows prompt.' }
    }
}
try {
    $state = Read-BrowserState
    if ($Command -eq 'status') {
        if (Get-BrowserStatus $state) { Show-BrowserAddresses $state; return }
        Write-Host 'The browser application is not running. Start it with start-browser.cmd.'
        return
    }
    if ($Command -eq 'logs') {
        $log = Join-Path $dataRoot 'host-error.log'
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 }
        else { Write-Host 'No host startup error is recorded.' }
        return
    }
    if ($Command -in @('stop','restart')) {
        Stop-BrowserHost $state
        if (Test-Path -LiteralPath $composePath -PathType Leaf) { Invoke-BrowserCompose -Arguments @('stop','postgres','mailpit') }
        Write-Host 'Only this checkout browser-test host and its PostgreSQL/Mailpit services were stopped. Database volumes and keys were preserved.'
        if ($Command -eq 'stop') { return }
    }
    Assert-BrowserPrerequisites
    if (Get-BrowserStatus $state) { Write-Host 'Already running. Use the restart command to rebuild after code changes.'; Show-BrowserAddresses $state; Open-BrowserAddresses $state; return }
    Stop-BrowserHost $state
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    $certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and @($_.Extensions | Where-Object { $_.Oid.Value -eq '1.3.6.1.4.1.311.84.1.1' }).Count -gt 0 } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($null -eq $certificate) { throw 'No valid ASP.NET localhost development certificate is available.' }
    $certificateHash = [Security.Cryptography.SHA256]::Create()
    try { $certificateSha256 = [BitConverter]::ToString($certificateHash.ComputeHash($certificate.RawData)).Replace('-','') } finally { $certificateHash.Dispose() }
    if ($null -eq $state) {
        $state = [pscustomobject]@{ Mode='ISOLATED_LOCAL_BROWSER_TESTING'; ProjectName=$projectName; InstanceId=[guid]::NewGuid().ToString(); ApplicationPort=(Select-LocalPort 24443); PostgresPort=(Select-LocalPort 25432); SmtpPort=(Select-LocalPort 21025); MailpitPort=(Select-LocalPort 28025); CertificateThumbprint=$certificate.Thumbprint; CertificateSha256=$certificateSha256; ProcessId=0; ProcessStartedTicks=0 }
    }
    $state.InstanceId = [guid]::NewGuid().ToString()
    $state.CertificateThumbprint = $certificate.Thumbprint
    $state.CertificateSha256 = $certificateSha256
    # Derive services from the approved pinned foundation; never reuse its volume names or operator .env.local.
    $baseCompose = Invoke-Tool -File docker -Arguments @('compose','--env-file',(Join-Path $repositoryRoot 'infra/docker/.env.example'),'--file',(Join-Path $repositoryRoot 'infra/docker/compose.yaml'),'config','--format','json') -Capture | ConvertFrom-Json
    $postgres = $baseCompose.services.postgres
    $postgres.environment.POSTGRES_DB = 'tunner_browser_testing'
    $postgres.ports = @('127.0.0.1:' + $state.PostgresPort + ':5432')
    $postgres.volumes = @(@{type='volume';source='browser-postgres-data';target='/var/lib/postgresql'})
    $mailpit = $baseCompose.services.mailpit
    $mailpit.ports = @(("127.0.0.1:{0}:1025" -f $state.SmtpPort),("127.0.0.1:{0}:8025" -f $state.MailpitPort))
    @{services=@{postgres=$postgres;mailpit=$mailpit};volumes=@{'browser-postgres-data'=@{}}} | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $composePath -Encoding UTF8
    Write-BrowserState $state
    Invoke-BrowserCompose -Arguments @('config','--quiet')
    Write-Host 'Preparing the pinned frontend and local browser host...'
    $web = Join-Path $repositoryRoot 'web'
    Invoke-Tool -File pnpm.cmd -Arguments @('install','--frozen-lockfile') -WorkingDirectory $web
    Invoke-Tool -File pnpm.cmd -Arguments @('build') -WorkingDirectory $web
    Invoke-Tool -File dotnet -Arguments @('restore',$hostProject)
    Invoke-Tool -File dotnet -Arguments @('build',$hostProject,'--no-restore','-warnaserror')
    Write-Host 'Starting isolated PostgreSQL and Mailpit; waiting for readiness...'
    Invoke-BrowserCompose -Arguments @('up','--detach','--wait','--wait-timeout','120','postgres','mailpit')
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
        $hostProcess = Start-Process -FilePath $dotnet -ArgumentList @(('"' + $hostAssembly + '"'),'--local-browser-testing',('"' + $statePath + '"')) -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru
    } finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }
    $state.ProcessId = $hostProcess.Id
    $state.ProcessStartedTicks = $hostProcess.StartTime.ToUniversalTime().Ticks
    Write-BrowserState $state
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while (-not (Get-BrowserStatus $state)) {
        $hostProcess.Refresh()
        if ($hostProcess.HasExited) { throw ('The local host exited. Check ' + (Join-Path $dataRoot 'host-error.log')) }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Local migrations/application readiness timed out. Use the status and logs commands.' }
        Start-Sleep -Seconds 1
    }
    $options = [TunnerBrowserLocalHttps]::Get(('https://localhost:' + $state.ApplicationPort + '/registration/options'),$state.CertificateSha256) | ConvertFrom-Json
    if ($options.policyVersion -ne 'browser-test-registration-v2') { throw 'The registration providers did not pass the test-mode readiness check.' }
    Write-Host 'Ready: database migrations, HTTPS, real registration API, packaged UI and local verification delivery.'
    Show-BrowserAddresses $state
    Open-BrowserAddresses $state
} catch {
    Write-Error -ErrorAction Continue ('Browser starter failed: ' + $_.Exception.Message)
    exit 1
}
