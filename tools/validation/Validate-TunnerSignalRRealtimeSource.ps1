param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$realtimeRoot = Join-Path $RepositoryRoot 'src/Tunner.Platform/Realtime'
$hub = Get-Content -Raw (Join-Path $realtimeRoot 'TunnerRealtimeHub.cs')
$endpoint = Get-Content -Raw (Join-Path $realtimeRoot 'TunnerRealtimeServiceCollectionExtensions.cs')
$hint = Get-Content -Raw (Join-Path $realtimeRoot 'RealtimeResynchronizationHint.cs')
$audience = Get-Content -Raw (Join-Path $realtimeRoot 'RealtimeAudience.cs')
$apiHost = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Api/Program.cs')
$test = Get-Content -Raw (Join-Path $RepositoryRoot 'tests/Tunner.Platform.ArchitectureTests/RealtimeFoundationTests.cs')
foreach ($token in @('[Authorize(Policy = TunnerRealtimePolicies.AuthenticatedConnect)]', 'OnConnectedAsync', 'ResolveAsync', 'Groups.AddToGroupAsync', 'Context.Abort()', 'SignalRRealtimeHintPublisher')) { if (-not $hub.Contains($token)) { throw "TUN-P1-010 hub is missing: $token" } }
foreach ($token in @('AddSignalR()', 'RequireAuthenticatedUser()', 'RequireAuthorization(TunnerRealtimePolicies.AuthenticatedConnect)', 'RequireCors(options.BrowserOriginPolicy)', 'RequireRateLimiting(options.ConnectionRateLimitPolicy)')) { if (-not $endpoint.Contains($token)) { throw "TUN-P1-010 endpoint foundation is missing: $token" } }
foreach ($token in @('RealtimeResynchronizationHint;', 'Task ResynchronizeAsync(RealtimeResynchronizationHint hint)', 'IRealtimeHintPublisher')) { if (-not $hint.Contains($token)) { throw "TUN-P1-010 hint foundation is missing: $token" } }
foreach ($token in @('IRealtimeAudienceResolver', 'IAuthorizationService AuthorizationService', 'DenyAllRealtimeAudienceResolver', 'RealtimeAudienceResolution.Deny()')) { if (-not $audience.Contains($token)) { throw "TUN-P1-010 server-derived audience control is missing: $token" } }
foreach ($prohibited in @('AddStackExchangeRedisBackplane', 'MapTunnerRealtimeFoundation', 'AddTunnerRealtimeFoundation')) { if ($apiHost.Contains($prohibited)) { throw "TUN-P1-010 current non-BFF API host must not expose realtime: $prohibited" } }
foreach ($token in @('RealtimeFoundationMapsOnlyAnAuthenticatedBrowserProtectedEndpoint', 'RealtimeFoundationHasNoClientSuppliedSubscriptionOrDomainPayload', 'RealtimeFoundationDeniesScopeUntilAnAuthoritativeResolverIsProvided')) { if (-not $test.Contains($token)) { throw "TUN-P1-010 deterministic coverage is missing: $token" } }
Write-Output 'TUN-P1-010 static SignalR realtime foundation validation passed.'