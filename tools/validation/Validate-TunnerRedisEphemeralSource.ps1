param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$store = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Platform/Redis/RedisEphemeralStore.cs')
$test = Get-Content -Raw (Join-Path $RepositoryRoot 'tests/Tunner.Database.IntegrationTests/RedisEphemeralStoreIntegrationTests.cs')
foreach ($token in @('IDatabase', 'StringSetAsync', 'When.NotExists', 'ScriptEvaluateAsync', 'RequireFiniteTtl')) { if (-not $store.Contains($token)) { throw "TUN-P1-008 Redis store is missing: $token" } }
foreach ($prohibited in @('ConnectionMultiplexer.Connect', 'ConfigurationOptions', 'password=', 'redis://')) { if ($store -match [regex]::Escape($prohibited)) { throw "TUN-P1-008 Redis store contains prohibited runtime configuration: $prohibited" } }
foreach ($token in @('CacheEntriesAreFinite', 'TryAcquireAsync', 'TryReleaseAsync', 'TUNNER_RUN_CONTAINER_TESTS')) { if (-not $test.Contains($token)) { throw "TUN-P1-008 Redis integration coverage is missing: $token" } }
Write-Output 'TUN-P1-008 static Redis ephemeral source validation passed.'