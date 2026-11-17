$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=(Get-Location).Path
$ownedRoot=$PSScriptRoot
$run='d52947ff9e86482687469246a30a5e75'
$image='sha256:c6a5b95f7348a70e743a9b5e2a3fd78cf22767a039ae81b36e1e561f138cb09c'
$expectedEngine='b81c329c-42bf-4ce2-812c-2570fbcda327'
$name='tunner-content-interruption-'+$run
$assembly=Join-Path $repo 'artifacts/p2-019-content-20261116/browser-startup-diagnostic/Tunner.BrowserTesting.dll'
$configPath=Join-Path $ownedRoot 'runtime.json'
$containerId=$null
$containerCreateAttempted=$false
$volumes=@()
$hostProcess=$null
$hostStartTicks=0L
$ownedProcesses=[Collections.Generic.List[object]]::new()
$result=[ordered]@{run_id=$run;source_sha='b1d5becc9b77b6dd30b044e3cfd225fd65fbfa6d';result='FAIL';stage='ADMISSION';http_timeout_seconds=10;container=$null;main_before=$false;main_after=$false;cleanup=$false;keys_removed=$false;failure_type=$null}
function Docker([string[]]$Arguments,[switch]$AllowFailure) {
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command docker -CommandType Application|Select-Object -First 1).Source)
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(30000)){$process.Kill($true);throw 'OWNED_DOCKER_CLIENT_DEADLINE'}
        $output=$stdout.GetAwaiter().GetResult(); $null=$stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0 -and -not $AllowFailure){throw 'OWNED_DOCKER_COMMAND_FAILED'}
        return [pscustomobject]@{exit=$process.ExitCode;output=$output.Trim()}
    } finally {$process.Dispose()}
}
function FreePort([int[]]$Excluded) {
    do {$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()}while($Excluded -contains $port)
    return $port
}
function Sql([string]$Text,[switch]$AllowFailure) {
    $reply=Docker @('exec',$containerId,'psql','-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-U','tunner','-d','tunner_browser_testing','-c',$Text) -AllowFailure:$AllowFailure
    if($reply.exit -ne 0){return $null}; return $reply.output
}
function LaunchHost {
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application|Select-Object -First 1).Source)
    $info.WorkingDirectory=$repo;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.Environment['ASPNETCORE_ENVIRONMENT']='Development'
    $info.ArgumentList.Add($assembly);$info.ArgumentList.Add('--local-browser-testing');$info.ArgumentList.Add($configPath)
    $script:hostProcess=[Diagnostics.Process]::Start($info)
    $script:hostStartTicks=$hostProcess.StartTime.ToUniversalTime().Ticks
    $ownedProcesses.Add([ordered]@{pid=$hostProcess.Id;start_utc=$hostProcess.StartTime.ToUniversalTime();assembly=$assembly;config=$configPath})
}
function StopOwnedHost {
    if($null -eq $script:hostProcess){return}
    $script:hostProcess.Refresh()
    if(-not $script:hostProcess.HasExited){
        if($script:hostProcess.StartTime.ToUniversalTime().Ticks -ne $script:hostStartTicks){throw 'OWNED_PID_START_MISMATCH'}
        # This retained Process object was launched with the exact assembly/config ArgumentList above.
        $script:hostProcess.Kill($true)
        if(-not $script:hostProcess.WaitForExit(15000)){throw 'OWNED_HOST_NOT_CLOSED'}
    }
    $script:hostProcess.Dispose();$script:hostProcess=$null
}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
public static class InterruptionPinnedHttps {
 public static string Get(string url,string pin) {
  var uri=new Uri(url);
  if(!uri.IsLoopback||uri.Scheme!="https"||uri.Host!="localhost"||pin.Length!=64) throw new InvalidOperationException();
#pragma warning disable SYSLIB0014
  var request=(HttpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
  request.Timeout=10000; request.ReadWriteTimeout=10000; request.AllowAutoRedirect=false;
  request.ServerCertificateValidationCallback=(sender,certificate,chain,errors)=>{
   if(certificate==null)return false;
   using(var hash=SHA256.Create())return string.Equals(BitConverter.ToString(hash.ComputeHash(certificate.GetRawCertData())).Replace("-",""),pin,StringComparison.OrdinalIgnoreCase);
  };
  using(var response=request.GetResponse())
  using(var reader=new StreamReader(response.GetResponseStream()))return reader.ReadToEnd();
 }
}
'@
$main=Get-Content -LiteralPath (Join-Path $repo 'artifacts/browser-testing/runtime.json') -Raw|ConvertFrom-Json
function MainReady {
    try {
        $status=[InterruptionPinnedHttps]::Get(('https://localhost:'+$main.ApplicationPort+'/browser-testing/status'),$main.CertificateSha256)|ConvertFrom-Json
        $pg=Docker @('inspect','--format','{{.State.Running}}','f72775e18f1dc226de5877a5017b4a26681097b60f79d372804293f4f720f143')
        $mail=Docker @('ps','--filter',('label=com.docker.compose.project='+$main.ProjectName),'--filter','label=com.docker.compose.service=mailpit','--format','{{.ID}}')
        return $status.ready -and $status.instanceId -eq $main.InstanceId -and $pg.output -eq 'true' -and $mail.output.Length -ge 12
    } catch {return $false}
}
$snapshotSql=@'
SELECT json_build_object(
 'publication_count',(SELECT count(*) FROM presentation_content.publications),
 'version_count',(SELECT count(*) FROM presentation_content.versions),
 'proposal_count',(SELECT count(*) FROM presentation_content.proposals),
 'publication_digest',(SELECT encode(sha256(convert_to(coalesce(string_agg(to_jsonb(p)::text,'' ORDER BY p."Id"),''),'UTF8')),'hex') FROM presentation_content.publications p),
 'version_digest',(SELECT encode(sha256(convert_to(coalesce(string_agg(to_jsonb(v)::text,'' ORDER BY v."Id"),''),'UTF8')),'hex') FROM presentation_content.versions v),
 'proposal_digest',(SELECT encode(sha256(convert_to(coalesce(string_agg(to_jsonb(d)::text,'' ORDER BY d."Id"),''),'UTF8')),'hex') FROM presentation_content.proposals d),
 'max_from',(SELECT max("From") FROM presentation_content.publications),
 'pending_count',(SELECT count(*) FROM presentation_content.publications WHERE "From">clock_timestamp()),
 'references',(SELECT json_object_agg("Key",json_build_object('publication',replace("Id"::text,'-',''),'version',replace("VersionReference"::text,'-',''))) FROM presentation_content.publications));
'@
try {
    if((Docker @('info','--format','{{.ID}}')).output -ne $expectedEngine){throw 'ENGINE_MISMATCH'}
    if((Docker @('image','inspect','--format','{{.Id}}',$image)).output -ne $image){throw 'IMAGE_MISMATCH'}
    $result.main_before=MainReady
    if(-not $result.main_before){throw 'MAIN_NOT_READY_BEFORE'}
    $pgPort=FreePort @(24443,25432,21025,28025);$appPort=FreePort @($pgPort,24443,25432,21025,28025);$smtpPort=FreePort @($pgPort,$appPort,24443,25432,21025,28025)
    $configuration=[ordered]@{Mode='ISOLATED_LOCAL_BROWSER_TESTING';InstanceId=$run;ApplicationPort=$appPort;PostgresPort=$pgPort;SmtpPort=$smtpPort;CertificateThumbprint=$main.CertificateThumbprint;GovernedContent=$true}
    $configuration|ConvertTo-Json|Set-Content -LiteralPath $configPath -Encoding utf8
    $result.stage='OWNED_PG_CREATE'
    $containerCreateAttempted=$true
    $created=Docker @('create','--name',$name,'--label',('tunner.content.interruption.run='+$run),'--label','tunner.content.interruption.scope=owned-disposable','--publish',('127.0.0.1:'+$pgPort+':5432'),'--env','POSTGRES_USER=tunner','--env','POSTGRES_DB=tunner_browser_testing','--env','POSTGRES_HOST_AUTH_METHOD=trust',$image)
    $containerId=$created.output
    if($containerId -notmatch '^[a-f0-9]{64}$'){throw 'OWNED_CONTAINER_ID_INVALID'}
    $inspect=(Docker @('inspect',$containerId)).output|ConvertFrom-Json
    if($inspect[0].Image -ne $image -or $inspect[0].Config.Labels.'tunner.content.interruption.run' -ne $run){throw 'OWNED_CONTAINER_IDENTITY_INVALID'}
    $volumes=@($inspect[0].Mounts|Where-Object Type -eq volume|ForEach-Object Name)
    $ownership=[ordered]@{run_id=$run;container_id=$containerId;container_name=$name;image=$image;engine=$expectedEngine;pg_port=$pgPort;app_port=$appPort;volumes=$volumes;config=$configPath}
    $ownership|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ownedRoot 'ownership.json') -Encoding utf8
    Write-Output ('OWNED_RESOURCES '+($ownership|ConvertTo-Json -Depth 4 -Compress))
    $result.container=$containerId
    $null=Docker @('start',$containerId)
    $deadline=[DateTime]::UtcNow.AddSeconds(60)
    do {$ready=Docker @('exec',$containerId,'pg_isready','-U','tunner','-d','tunner_browser_testing') -AllowFailure;if($ready.exit -eq 0){break};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
    if($ready.exit -ne 0){throw 'OWNED_PG_READINESS_DEADLINE'}
    $result.stage='INITIAL_BOOTSTRAP';LaunchHost
    $deadline=[DateTime]::UtcNow.AddSeconds(90);$before=$null
    do {
        $hostProcess.Refresh();if($hostProcess.HasExited){throw 'INITIAL_HOST_EXITED'}
        $exists=Sql "SELECT to_regclass('presentation_content.publications') IS NOT NULL" -AllowFailure
        if($exists -eq 't') {
            $json=Sql $snapshotSql -AllowFailure
            if($json){$candidate=$json|ConvertFrom-Json;if($candidate.publication_count -eq 62 -and $candidate.pending_count -gt 0){$before=$candidate;break}}
        }
        Start-Sleep -Milliseconds 75
    }while([DateTime]::UtcNow -lt $deadline)
    if($null -eq $before){throw 'NO_62_PENDING_PUBLICATIONS_OBSERVED'}
    $result.stage='INTERRUPT_PENDING_BOOTSTRAP';$result.pending_before=$before.pending_count
    $result.max_from=$before.max_from
    StopOwnedHost
    LaunchHost
    $restart=$hostProcess.StartTime.ToUniversalTime()
    $result.restart_utc=$restart
    $pendingUntil=if($before.max_from -is [DateTime]){$before.max_from.ToUniversalTime()}else{[DateTimeOffset]::Parse([string]$before.max_from).UtcDateTime}
    if($restart -ge $pendingUntil){$result.result='INCONCLUSIVE';throw 'RESTART_WINDOW_MISSED'}
    $result.stage='RESTART_READINESS';$deadline=[DateTime]::UtcNow.AddSeconds(90);$ready=$false
    do {
        $hostProcess.Refresh();if($hostProcess.HasExited){throw 'RESTART_HOST_EXITED'}
        try {$status=[InterruptionPinnedHttps]::Get(('https://localhost:'+$appPort+'/browser-testing/status'),$main.CertificateSha256)|ConvertFrom-Json;$ready=$status.ready -and $status.instanceId -eq $run}catch{}
        if(-not $ready){Start-Sleep -Milliseconds 200}
    }while(-not $ready -and [DateTime]::UtcNow -lt $deadline)
    if(-not $ready){throw 'RESTART_HTTPS_DEADLINE'}
    $options=[InterruptionPinnedHttps]::Get(('https://localhost:'+$appPort+'/authentication/options'),$main.CertificateSha256)|ConvertFrom-Json
    $after=(Sql $snapshotSql)|ConvertFrom-Json
    $same=$true
    foreach($key in @('publication_count','version_count','proposal_count','publication_digest','version_digest','proposal_digest')) {
        if($before.$key -ne $after.$key){$same=$false}
        $result[$key]=$after.$key
    }
    $provenance=0
    foreach($entry in $options.content.PSObject.Properties) {
        $expected=$before.references.($entry.Name)
        if($null -ne $expected -and $entry.Value.publicationRef -eq $expected.publication -and $entry.Value.versionRef -eq $expected.version){$provenance++}
    }
    $restartLiteral=$restart.ToString('O')
    $observed=Sql ('SELECT count(*) FROM audit.events a JOIN presentation_content.publications p ON (a.envelope::jsonb #>> ''{targets,0,resource_id}'')=p."Key"||'':''||p."VariantReference" WHERE a.operation_id=''fixture-content-history'' AND a.occurred_at_utc>='''+$restartLiteral+'''::timestamptz AND a.occurred_at_utc<p."From"')
    $result.history_before_own_from_count=[int]$observed
    $result.same_publication_version_proposal_snapshot=$same
    $result.returned_provenance_matches=$provenance
    $result.auth_options_http_status=200
    if(-not $same -or $provenance -ne 62 -or [int]$observed -lt 1){throw 'INTERRUPTION_ASSERTION_FAILED'}
    $result.result='PASS';$result.stage='COMPLETE'
} catch {
    $result.failure_type=$_.Exception.GetType().Name
    $result.failure_code=$_.Exception.Message
} finally {
    try {StopOwnedHost}catch{$result.cleanup_error='OWNED_HOST_CLEANUP_FAILED'}
    try {
        if(-not $containerId -and $containerCreateAttempted){
            $recover=Docker @('inspect',$name) -AllowFailure
            if($recover.exit -eq 0){$candidate=$recover.output|ConvertFrom-Json;if($candidate[0].Config.Labels.'tunner.content.interruption.run' -ne $run -or $candidate[0].Image -ne $image){throw 'OWNED_RECOVERY_IDENTITY_MISMATCH'};$containerId=$candidate[0].Id}
        }
        if($containerId) {
            $check=(Docker @('inspect',$containerId)).output|ConvertFrom-Json
            if($check[0].Config.Labels.'tunner.content.interruption.run' -ne $run -or $check[0].Image -ne $image){throw 'OWNED_CONTAINER_CLEANUP_IDENTITY_MISMATCH'}
            $volumes=@($check[0].Mounts|Where-Object Type -eq volume|ForEach-Object Name)
            $null=Docker @('rm','--force','--volumes',$containerId)
            if((Docker @('inspect',$containerId) -AllowFailure).exit -eq 0){throw 'OWNED_CONTAINER_REMAINS'}
            foreach($volume in $volumes){if((Docker @('volume','inspect',$volume) -AllowFailure).exit -eq 0){throw 'OWNED_VOLUME_REMAINS'}}
        }
        $result.cleanup=$true
    } catch {$result.cleanup_error='OWNED_CONTAINER_OR_VOLUME_CLEANUP_FAILED'}
    try {
        $keys=[IO.Path]::GetFullPath((Join-Path $ownedRoot 'keys'));$expected=[IO.Path]::GetFullPath($ownedRoot).TrimEnd('\')+'\'
        if(-not $keys.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase) -or -not $ownedRoot.EndsWith($run)){throw 'OWNED_KEYS_PATH_INVALID'}
        if(Test-Path -LiteralPath $keys){if((Get-Item -LiteralPath $keys).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'OWNED_KEYS_REPARSE'};Remove-Item -LiteralPath $keys -Recurse -Force}
        $result.keys_removed=-not(Test-Path -LiteralPath $keys)
    } catch {$result.keys_removed=$false}
    $result.main_after=MainReady
    if(-not $result.cleanup -or -not $result.keys_removed -or -not $result.main_after){$result.result='FAIL'}
    $result.processes=$ownedProcesses.ToArray()
    $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $ownedRoot 'result.json') -Encoding utf8
    Write-Output ($result|ConvertTo-Json -Depth 8 -Compress)
}
