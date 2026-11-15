#Requires -Version 7
# DEMO (R18, Member 2): Catalog + Inventory trên stack Docker đang chạy (`docker compose up -d`).
# REST đi qua Gateway như web app. gRPC không publish ra host, nên client demo chạy trong mạng backend và đóng vai
# Borrowing (src/Services/Inventory/Inventory.DemoClient). Dùng dev token local (New-DevToken.ps1), không in secret.
# Chạy lại được nhiều lần: kính hiển vi được trả về AVAILABLE, thiết bị demo được ngừng dùng ở cuối bước 4.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Run scripts/Initialize-Environment.ps1 first.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envPath) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$gateway = "http://localhost:$(if ($settings['GATEWAY_PORT']) { $settings['GATEWAY_PORT'] } else { 5080 })"
$microscopeUnit = 'a0000000-0000-0000-0000-000000000010' # seed: the only unit of e…04

function Write-Section([string]$Title) { Write-Host "`n== $Title" -ForegroundColor Cyan }
function Write-Line([string]$Text) { Write-Host "  $Text" }

$tokens = @{}
function Invoke-Api([string]$Method, [string]$Path, [string]$Role, $Body, [int]$Expect = 200) {
    $headers = @{}
    if ($Role) {
        if (-not $tokens[$Role]) { $tokens[$Role] = & (Join-Path $PSScriptRoot 'New-DevToken.ps1') -Role $Role -Minutes 15 }
        $headers.Authorization = "Bearer $($tokens[$Role])"
    }
    $request = @{ Uri = "$gateway$Path"; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $Body) { $request.ContentType = 'application/json'; $request.Body = $Body | ConvertTo-Json }
    $data = Invoke-RestMethod @request
    if ($status -ne $Expect) { throw "$Method $Path → $status (expected $Expect): $($data | ConvertTo-Json -Compress -Depth 5)" }
    $shown = if ($Path.Length -gt 70) { $Path.Substring(0, 67) + '…' } else { $Path }
    Write-Line ("{0,-6} {1} [{2}] → {3}" -f $Method, $shown, $(if ($Role) { $Role } else { 'ẩn danh' }), $status)
    $data
}

Write-Section '1. Docker: service của M2 đang chạy'
Push-Location $repoRoot
try {
    foreach ($service in 'postgres', 'catalog', 'inventory', 'gateway') {
        $container = docker compose ps -q $service
        $health = if ($container) { docker inspect -f '{{.State.Health.Status}}' $container } else { 'not running' }
        if ($health -ne 'healthy') { throw "$service is $health. Run: docker compose up -d --build" }
        Write-Line "$service : healthy"
    }
    docker compose exec -T inventory dotnet /probe/HealthProbe.dll grpc http://localhost:8081
    if ($LASTEXITCODE) { throw 'Inventory gRPC health check failed.' }
    Write-Line 'inventory gRPC (cổng 8081, chỉ trong mạng backend): SERVING'

    Write-Section '2. REST công khai qua Gateway: Catalog + tồn kho'
    $types = (Invoke-Api GET '/api/equipment-types?sortBy=name&pageSize=10').items
    $query = ($types | ForEach-Object { "equipmentTypeIds=$($_.id)" }) -join '&'
    $stock = Invoke-Api GET "/api/inventory/availability?$query"
    $types | Select-Object @{ n = 'Loại thiết bị'; e = { $_.name } }, @{ n = 'Danh mục'; e = { $_.categoryName } },
        @{ n = 'Sẵn sàng/Tổng'; e = { $id = $_.id; $s = $stock | Where-Object equipmentTypeId -eq $id; "$($s.available)/$($s.total)" } } |
        Format-Table -AutoSize | Out-String | Write-Host

    Write-Section '3. Phân quyền JWT: danh sách asset chỉ cho Staff/Admin'
    $null = Invoke-Api GET '/api/inventory/assets?pageSize=1' -Expect 401
    $null = Invoke-Api GET '/api/inventory/assets?pageSize=1' STUDENT -Expect 403
    $page = Invoke-Api GET '/api/inventory/assets?pageSize=1' LAB_STAFF
    Write-Line "LAB_STAFF thấy $($page.total) thiết bị"

    Write-Section '4. Staff quản lý một thiết bị: CRUD + bảo trì + lịch sử'
    $asset = Invoke-Api POST '/api/inventory/assets' LAB_STAFF -Expect 201 -Body @{
        equipmentTypeId = 'e0000000-0000-0000-0000-000000000003'; serialNumber = "DEMO-$(Get-Date -Format 'yyMMddHHmmss')"
        location = 'Phòng demo'; notes = $null
    }
    $path = "/api/inventory/assets/$($asset.id)"
    $null = Invoke-Api PUT $path LAB_STAFF -Body @{ equipmentTypeId = $asset.equipmentTypeId; serialNumber = $asset.serialNumber; location = 'Phòng demo - Kệ 2'; notes = 'đã hiệu chuẩn' }
    $null = Invoke-Api POST "$path/maintenance" LAB_STAFF -Expect 201 -Body @{ reason = 'Kiểm tra định kỳ' }
    $null = Invoke-Api POST "$path/maintenance/complete" LAB_STAFF -Body @{ notes = 'OK' }
    $null = Invoke-Api DELETE $path LAB_STAFF -Expect 204
    $movements = Invoke-Api GET "$path/movements" LAB_STAFF
    $movements | Sort-Object id | Select-Object @{ n = 'Thay đổi'; e = { "$(if ($_.fromStatus) { $_.fromStatus } else { '(tạo mới)' }) → $($_.toStatus)" } },
        @{ n = 'Bởi'; e = { "staff $($_.changedBy.Substring(0, 8))" } } | Format-Table -AutoSize | Out-String | Write-Host

    Write-Section '5. gRPC: client đóng vai Borrowing, chạy trong mạng backend'
    dotnet build src/Services/Inventory/Inventory.DemoClient -v q -nologo | Out-Null
    if ($LASTEXITCODE) { throw 'Build Inventory.DemoClient failed.' }
    $inventory = docker compose ps -q inventory
    $network = docker inspect -f '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}}{{end}}' $inventory
    $image = docker inspect -f '{{.Config.Image}}' $inventory
    $client = Join-Path $repoRoot 'src/Services/Inventory/Inventory.DemoClient/bin/Debug/net8.0'
    $env:INVENTORY_SERVICE_TOKEN = $settings['INVENTORY_SERVICE_TOKEN'] # passed to docker by name, never printed
    try {
        docker run --rm --network $network -e INVENTORY_SERVICE_TOKEN -v "${client}:/demo:ro" --entrypoint dotnet $image /demo/Inventory.DemoClient.dll
        if ($LASTEXITCODE) { throw 'gRPC demo failed.' }
    }
    finally { Remove-Item Env:INVENTORY_SERVICE_TOKEN -ErrorAction SilentlyContinue }

    Write-Section '6. StockMovements của kính hiển vi sau bước gRPC (REST)'
    $history = Invoke-Api GET "/api/inventory/assets/$microscopeUnit/movements" LAB_STAFF
    $history | Select-Object -First 3 | Sort-Object id | Select-Object @{ n = 'Lúc'; e = { ([datetime]$_.occurredAt).ToString('HH:mm:ss') } },
        @{ n = 'Thay đổi'; e = { "$($_.fromStatus) → $($_.toStatus)" } }, @{ n = 'Phiếu mượn'; e = { "$($_.loanId)".Substring(0, 8) } } |
        Format-Table -AutoSize | Out-String | Write-Host

    Write-Section 'OpenAPI qua Gateway'
    Write-Line "$gateway/platform/catalog/openapi.json"
    Write-Line "$gateway/platform/inventory/openapi.json"
}
finally { Pop-Location }
