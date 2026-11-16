#Requires -Version 7
[CmdletBinding()]
param([switch]$IncludeBonus, [ValidateRange(30,600)][int]$WaitTimeout = 180)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # Bound peak startup/JIT/migration pressure on small Docker VMs. No retries, resets or volume removal.
    # Borrowing API completes its owned migration before the worker; Inventory RPC is ready first.
    foreach ($service in @('postgres','redis','identity','catalog','inventory','borrowing','notification','borrowing-worker','gateway','web')) {
        Write-Output "Starting $service and waiting for actual health..."
        docker compose up -d --no-build --wait --wait-timeout $WaitTimeout $service
        if ($LASTEXITCODE -ne 0) { throw "Startup failed for $service. Preserve containers and inspect logs/state before retrying." }
    }
    if ($IncludeBonus) {
        docker compose --profile bonus up -d --no-build --wait --wait-timeout $WaitTimeout recommendation
        if ($LASTEXITCODE -ne 0) { throw 'Optional Recommendation host failed. Core remains independent.' }
    }
} finally { Pop-Location }
Write-Output 'Core startup completed with each service healthy. Business E2E acceptance is a separate gate.'
