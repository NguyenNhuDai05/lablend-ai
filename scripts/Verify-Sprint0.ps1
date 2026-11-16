[CmdletBinding()]
param([switch]$IncludeBonus)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$gateway = "http://localhost:$($settings['GATEWAY_PORT'])"
$web = "http://localhost:$($settings['WEB_PORT'])"
$contexts = @('identity','catalog','inventory','borrowing','notification')
if ($IncludeBonus) { $contexts += 'recommendation' }
Push-Location $repoRoot
try {
    docker compose config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Invalid Compose configuration.' }
    $expectedServices = @('postgres','redis','gateway','web','borrowing-worker') + $contexts
    $containers = @(docker compose ps --format json | ForEach-Object { $_ | ConvertFrom-Json })
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect containers.' }
    foreach ($name in $expectedServices) {
        $container = @($containers | Where-Object { $_.Service -eq $name })
        if ($container.Count -ne 1 -or $container[0].State -ne 'running' -or $container[0].Health -ne 'healthy') {
            throw "Service $name is not running and healthy."
        }
    }
    Write-Host "PASS: $($expectedServices.Count) expected containers are running and healthy."
    foreach ($name in $contexts) {
        $requestId = 'sprint0-' + [Guid]::NewGuid().ToString('N')
        $response = Invoke-RestMethod "$gateway/platform/$name/health/ready" -Headers @{'X-Correlation-ID'=$requestId}
        if ($response.status -ne 'Healthy' -or $response.correlationId -ne $requestId) {
            throw "Gateway -> $name readiness/correlation failed."
        }
        $schema = Invoke-RestMethod "$gateway/platform/$name/openapi.json"
        if (-not $schema.openapi -or -not $schema.info.title) { throw "Invalid OpenAPI schema for $name." }
        Write-Host "PASS: Gateway -> $name actual readiness, correlation forwarding and OpenAPI."
    }
    $front = Invoke-WebRequest "$web/" -UseBasicParsing
    if ($front.StatusCode -ne 200 -or $front.Content -notmatch 'LabLend AI') { throw 'Frontend entry point failed.' }
    $viaWeb = Invoke-RestMethod "$web/health/live"
    if ($viaWeb.status -ne 'Healthy' -or -not $viaWeb.correlationId) { throw 'nginx -> Gateway failed.' }
    Write-Host 'PASS: frontend entry point and nginx -> Gateway health.'
    docker compose exec -T borrowing dotnet /probe/HealthProbe.dll grpc http://inventory:8081
    if ($LASTEXITCODE -ne 0) { throw 'Inter-container standard gRPC health failed.' }
    Write-Host 'PASS: Borrowing container -> Inventory standard gRPC health (NOT allocation).'
    # Copy bytes instead of piping Windows stdout: CRLF added by PowerShell can break bash stdin.
    docker compose cp infrastructure/database/verify-isolation.sh postgres:/tmp/lablend-verify-isolation.sh
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy database isolation check.' }
    docker compose exec -T postgres bash /tmp/lablend-verify-isolation.sh
    if ($LASTEXITCODE -ne 0) { throw 'Database ownership/isolation check failed.' }
    Write-Host 'Sprint 0 foundation runtime checks passed. Business acceptance is documented separately in PR8_INTEGRATION_ACCEPTANCE.md.'
} finally { Pop-Location }
