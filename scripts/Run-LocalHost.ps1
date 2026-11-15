[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('gateway','identity','catalog','inventory','borrowing','notification','recommendation','worker')][string]$Service)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Set-LocalEnvironment.ps1') -Service $Service
$repoRoot = Split-Path -Parent $PSScriptRoot
$projects = @{
    gateway='src/Gateway/LabLend.Gateway/LabLend.Gateway.csproj'
    identity='src/Services/Identity/Identity.Api/Identity.Api.csproj'
    catalog='src/Services/Catalog/Catalog.Api/Catalog.Api.csproj'
    inventory='src/Services/Inventory/Inventory.Host/Inventory.Host.csproj'
    borrowing='src/Services/Borrowing/Borrowing.Api/Borrowing.Api.csproj'
    notification='src/Services/Notification/Notification.Api/Notification.Api.csproj'
    recommendation='src/Services/Recommendation/Recommendation.Api/Recommendation.Api.csproj'
    worker='src/Workers/LabLend.Borrowing.Worker/LabLend.Borrowing.Worker.csproj'
}
dotnet run --project (Join-Path $repoRoot $projects[$Service]) --no-launch-profile
exit $LASTEXITCODE
