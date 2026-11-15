[CmdletBinding()]
param([switch]$IncludeBonus)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$services = @('identity','catalog','inventory','borrowing','notification','borrowing-worker','gateway','web')
if ($IncludeBonus) { $services += 'recommendation' }
Push-Location $repoRoot
try {
    # Sequential builds fit small Docker VMs; the shared restore stage is cached across hosts.
    foreach ($service in $services) {
        Write-Host "Building $service (sequentially)..."
        docker compose --progress plain build $service
        if ($LASTEXITCODE -ne 0) { throw "Image build failed for $service." }
    }
} finally { Pop-Location }
