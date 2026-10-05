param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$packageFiles = @((Join-Path $RepositoryRoot 'Directory.Packages.props')) + @(Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter '*.csproj' | Select-Object -ExpandProperty FullName)
foreach ($path in $packageFiles) {
    if ((Get-Content -Raw -LiteralPath $path) -match '(?i)(PackageVersion|PackageReference).*Grpc\.') {
        throw "TUN-P1-011 must not add a gRPC package without an approved activation trigger: $path"
    }
}
$protos = @(Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter '*.proto' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
if ($protos.Count -gt 0) { throw "TUN-P1-011 must not add .proto files without an approved activation trigger: $($protos.FullName -join ', ')" }
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$sourceText = ($sourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
foreach ($token in @('AddGrpc(', 'MapGrpcService<', 'GrpcChannel.ForAddress', 'ServerCallContext', 'GrpcClientFactory')) {
    if ($sourceText.Contains($token)) { throw "TUN-P1-011 must not add gRPC runtime scaffolding without an approved activation trigger: $token" }
}
$workItem = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot 'governance/work-items/TUN-P1-011.yaml')
$audit = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot 'docs/development/P1_GRPC_PROTOCOL_ELIGIBILITY_AUDIT.md')
if (-not $audit.Contains('Selective activation trigger: NOT_PRESENT')) {
    throw 'TUN-P1-011 eligibility audit is missing the NOT_PRESENT activation result.'
}
foreach ($token in @('product_acceptance_required: false', 'no Grpc.* package', 'gRPC/gRPC-Web endpoint', 'generated client', 'feature/capability-specific ADR')) {
    if (-not $workItem.Contains($token)) { throw "TUN-P1-011 work item is missing boundary statement: $token" }
}
Write-Output 'TUN-P1-011 static gRPC protocol-eligibility validation passed.'