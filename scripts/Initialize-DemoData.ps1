#Requires -Version 7
[CmdletBinding()]
param([string]$Gateway = 'http://localhost:5080', [string]$Origin = 'http://localhost:3000',
    [string]$AdminEmail = 'admin@demo.test', [string]$Prefix = 'PRN232')
$ErrorActionPreference = 'Stop'
if (-not $env:LABLEND_DEMO_ADMIN_PASSWORD -or -not $env:LABLEND_DEMO_USER_PASSWORD) {
    throw 'Set LABLEND_DEMO_ADMIN_PASSWORD and LABLEND_DEMO_USER_PASSWORD locally. No default password is provided.'
}
if ($Prefix -notmatch '^[A-Za-z0-9-]{2,20}$') { throw 'Prefix must contain 2-20 letters, digits or hyphens.' }
$Gateway = $Gateway.TrimEnd('/')
function Invoke-Api([string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = '') {
    $headers = @{ Origin = $Origin; 'X-LabLend-CSRF' = '1' }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $args = @{ Uri = "$Gateway$Path"; Method = $Method; Headers = $headers; TimeoutSec = 20; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) { $args.Body = $Body | ConvertTo-Json -Depth 5; $args.ContentType = 'application/json' }
    $response = Invoke-WebRequest @args
    if ($response.StatusCode -ge 400) { throw "Demo API failed: $Method $Path (HTTP $($response.StatusCode)). Inspect state before rerunning mutations." }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}
$admin = Invoke-Api '/api/auth/login' POST @{ email = $AdminEmail; password = $env:LABLEND_DEMO_ADMIN_PASSWORD }
$token = $admin.accessToken
foreach ($role in @('STUDENT','LAB_STAFF')) {
    $email = ($Prefix + '-' + $role + '@demo.test').ToLowerInvariant()
    $page = Invoke-Api ("/api/users?keyword=" + [uri]::EscapeDataString($email)) -Token $token
    $user = $page.items | Where-Object email -EQ $email | Select-Object -First 1
    if (-not $user) {
        $user = Invoke-Api '/api/auth/register' POST @{ displayName = "$Prefix $role"; email = $email; password = $env:LABLEND_DEMO_USER_PASSWORD; confirmPassword = $env:LABLEND_DEMO_USER_PASSWORD }
        if ($role -eq 'LAB_STAFF') { $user = Invoke-Api "/api/users/$($user.id)/role" PUT @{ role = $role } $token }
    } elseif (-not $user.isActive -or $user.roles -notcontains $role) {
        throw 'An existing demo account has different role/active state; preserve it for operator review.'
    }
    Write-Output "Demo account ready: $email ($role). Existing passwords are preserved."
}
$categories = Invoke-Api '/api/categories' -Token $token
$categoryName = "$Prefix Demo"
$category = $categories | Where-Object name -EQ $categoryName | Select-Object -First 1
if (-not $category) { $category = Invoke-Api '/api/categories' POST @{ name = $categoryName; description = 'Reusable course demonstration equipment' } $token }
$equipmentName = "$Prefix Signal Kit"
$page = Invoke-Api ("/api/equipment-types?keyword=" + [uri]::EscapeDataString($equipmentName) + '&categoryId=' + $category.id) -Token $token
$equipment = $page.items | Where-Object name -EQ $equipmentName | Select-Object -First 1
if (-not $equipment) { $equipment = Invoke-Api '/api/equipment-types' POST @{ categoryId = $category.id; name = $equipmentName; specifications = 'Demo laboratory signal measurement kit'; tags = @('prn232-demo') } $token }
for ($number = 1; $number -le 3; $number++) {
    $serial = "$Prefix-DEMO-$number".ToUpperInvariant()
    $page = Invoke-Api ("/api/inventory/assets?keyword=" + [uri]::EscapeDataString($serial)) -Token $token
    $asset = $page.items | Where-Object serialNumber -EQ $serial | Select-Object -First 1
    if (-not $asset) { $asset = Invoke-Api '/api/inventory/assets' POST @{ equipmentTypeId = $equipment.id; serialNumber = $serial; location = 'PRN232 demo lab' } $token }
    if ($asset.equipmentTypeId -ne $equipment.id) { throw 'Existing serial belongs to another type; nothing overwritten.' }
    Write-Output "Demo physical unit: $serial ($($asset.status))."
}
Write-Output 'Demo data prepared through authorized REST commands. No loan, history, event or status was fabricated/reset.'
