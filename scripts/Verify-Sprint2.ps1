#Requires -Version 7
[CmdletBinding()]
param([switch]$IncludeBonus)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# Read-only runtime verification. Business writes/faults belong to the private integration fixtures.
& (Join-Path $PSScriptRoot 'Verify-Sprint1.ps1') -IncludeBonus:$IncludeBonus
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$gateway = "http://localhost:$($settings['GATEWAY_PORT'])"
$spec = Invoke-RestMethod "$gateway/platform/borrowing/openapi.json"
foreach ($operation in @('approve', 'handover', 'returns')) {
    $path = '/api/loans/{id}/' + $operation
    if (-not $spec.paths.PSObject.Properties[$path].Value.post) { throw "Missing Borrowing OpenAPI operation $path" }
    $response = Invoke-WebRequest "$gateway/api/loans/$([guid]::NewGuid())/$operation" -Method Post -SkipHttpErrorCheck
    if ($response.StatusCode -ne 401) { throw "Anonymous $operation must return 401." }
    Write-Output "PASS: Gateway OpenAPI POST $path, anonymous 401."
}
Push-Location $repoRoot
try {
    $sql = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
    $applied = @($sql | docker compose exec -T postgres psql -U borrowing_user -d borrowing_db -At -v ON_ERROR_STOP=1)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect owned Borrowing migrations.' }
    foreach ($suffix in @('InitialBorrowing','AddInventoryLifecycle','AddBorrowingOutbox','AddReminderDispatches')) {
        if (-not ($applied | Where-Object { $_ -match ('_' + $suffix + '$') })) { throw "Missing applied migration: $suffix" }
    }
    Write-Output ('PASS: Borrowing migrations: ' + ($applied -join ', '))
    $counts = 'SELECT ''operations='' || count(*) FROM "InventoryOperations" UNION ALL SELECT ''outbox='' || count(*) FROM "OutboxMessages" UNION ALL SELECT ''reminderDispatches='' || count(*) FROM "ReminderDispatches";'
    $counts | docker compose exec -T postgres psql -U borrowing_user -d borrowing_db -At -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'Borrowing Sprint 2 tables are unavailable.' }
    $logs = docker compose logs --no-color --tail 250 borrowing-worker | Out-String
    foreach ($job in @('DueReminderJob','OverdueDetectionJob','ReservationReconciliationJob','OutboxPublisherJob')) {
        if ($logs -notmatch "$job pass completed") { throw "No completed scheduled pass recorded for $job" }
    }
    if ($logs -notmatch 'businessJobsImplemented=true') { throw 'Updated worker readiness log is missing.' }
    Write-Output 'PASS: all four actual scheduled jobs logged completed passes; worker dependency/job heartbeat is healthy.'
    docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli XINFO GROUPS lablend.events.v1'
    if ($LASTEXITCODE -ne 0) { throw 'Redis Streams consumer-group metadata check failed.' }
    Write-Output 'PASS: Redis stream/group metadata is reachable (business delivery/fault evidence is in private integration tests).'
} finally { Pop-Location }
Write-Output 'PASS: Sprint 2 Docker runtime verification. See SPRINT2_M1_ACCEPTANCE.md for separate E2E/failure evidence.'
