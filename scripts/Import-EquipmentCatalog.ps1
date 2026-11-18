#Requires -Version 7
<#
.SYNOPSIS
    Import the equipment catalog (and optional DEMO stock) through the real Catalog/Inventory REST API.
.DESCRIPTION
    Preview (dry run) by default: validates the JSON, compares it with what the API returns and prints
    Created/Updated/Skipped/Conflicted/Failed without writing anything.
      -Apply           create missing categories and equipment types.
      -UpdateExisting  also update records that exist but differ (otherwise they are reported Conflicted).
      -WithDemoStock   also create 1-5 AVAILABLE units per type with serial DEMO-<demoCode>-NNN (loopback gateway only).
    Existing asset units are never updated or retired, so RESERVED/ON_LOAN units are never touched.
    Matching is by name (case-insensitive), so reruns never create duplicates.
    Sign-in mirrors Initialize-DemoData.ps1: password only from $env:LABLEND_DEMO_ADMIN_PASSWORD, token kept in memory.
.EXAMPLE
    $env:LABLEND_DEMO_ADMIN_PASSWORD = Read-Host 'Local demo admin password' -MaskInput
    ./scripts/Import-EquipmentCatalog.ps1                                # preview
    ./scripts/Import-EquipmentCatalog.ps1 -Apply                         # catalog only
    ./scripts/Import-EquipmentCatalog.ps1 -Apply -WithDemoStock          # catalog + DEMO- units
    Remove-Item Env:LABLEND_DEMO_ADMIN_PASSWORD
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot '../data/demo/equipment-catalog.vi.json'),
    [string]$Gateway = 'http://localhost:5080',
    [string]$Origin = 'http://localhost:3000',
    [string]$AdminEmail = 'admin@demo.test',
    [switch]$Apply,
    [switch]$UpdateExisting,
    [switch]$WithDemoStock,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$Gateway = $Gateway.TrimEnd('/')
$DemoNote = 'Dữ liệu demo do Import-EquipmentCatalog.ps1 tạo; không phải tài sản thật.'
$SeedTypePrefix = 'e0000000-0000-0000-0000-' # fixed seed types (T07 race test relies on their unit counts)

function Key([string]$s) { "$s".Trim().Normalize().ToLowerInvariant() }
function Norm($s) { "$s".Trim().Normalize() }
function TagKey($tags) { (@($tags) | ForEach-Object { "$_".Trim().ToLowerInvariant() } | Where-Object { $_ } | Sort-Object -Unique) -join ',' }

function Test-Catalog($data) {
    $e = [System.Collections.Generic.List[string]]::new()
    if ($data.version -ne 1) { $e.Add('version must be 1.') }
    $cats = @{}
    foreach ($c in @($data.categories)) {
        $n = Norm $c.name
        if ($n.Length -lt 2 -or $n.Length -gt 150) { $e.Add("category '$n': name must be 2-150 characters.") }
        if ((Norm $c.description).Length -gt 1000) { $e.Add("category '$n': description exceeds 1000 characters.") }
        $loc = Norm $c.demoLocation
        if ($loc.Length -lt 1 -or $loc.Length -gt 150) { $e.Add("category '$n': demoLocation must be 1-150 characters.") }
        if ($cats.ContainsKey((Key $n))) { $e.Add("category '$n': duplicate name.") }
        $cats[(Key $n)] = $true
    }
    $types = @{}; $codes = @{}
    foreach ($t in @($data.equipmentTypes)) {
        $n = Norm $t.name; $at = "type '$n'"
        if ($n.Length -lt 2 -or $n.Length -gt 150) { $e.Add("${at}: name must be 2-150 characters.") }
        if ($types.ContainsKey((Key $n))) { $e.Add("${at}: duplicate name.") }
        $types[(Key $n)] = $true
        if (-not $cats.ContainsKey((Key $t.category))) { $e.Add("${at}: unknown category '$($t.category)'.") }
        if ($null -ne $t.description -and $t.description -isnot [string]) { $e.Add("${at}: description must be a string.") }
        elseif ((Norm $t.description).Length -gt 2000) { $e.Add("${at}: description exceeds 2000 characters.") }
        if ($null -ne $t.specifications -and $t.specifications -isnot [string]) { $e.Add("${at}: specifications must be a string.") }
        elseif ((Norm $t.specifications).Length -gt 4000) { $e.Add("${at}: specifications exceed 4000 characters.") }
        if ($null -ne $t.tags -and $t.tags -isnot [array]) { $e.Add("${at}: tags must be an array.") }
        elseif (@($t.tags).Count -gt 20) { $e.Add("${at}: at most 20 tags.") }
        foreach ($tag in @($t.tags)) {
            if ($tag -isnot [string] -or $tag -cnotmatch '^[a-z0-9][a-z0-9-]{0,49}$') { $e.Add("${at}: tag '$tag' must be lowercase a-z, 0-9, '-' (max 50).") }
        }
        if ($null -ne $t.imageUrl) {
            # Same rule as Catalog's [ImageUrl]: http(s) URL, or an image bundled in apps/web/public/equipment.
            $uri = $null
            if ($t.imageUrl -is [string] -and $t.imageUrl -cmatch '^/equipment/[a-z0-9][a-z0-9-]{0,99}\.(webp|png|jpe?g)$') {
                if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "../apps/web/public$($t.imageUrl)") -PathType Leaf)) {
                    $e.Add("${at}: image file apps/web/public$($t.imageUrl) does not exist.")
                }
            }
            elseif ($t.imageUrl -isnot [string] -or $t.imageUrl.Length -gt 500 -or
                -not [uri]::TryCreate($t.imageUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin 'http', 'https') {
                $e.Add("${at}: imageUrl must be null, an absolute http(s) URL (max 500) or /equipment/<name>.webp|png|jpg.")
            }
        }
        if ($t.isActive -isnot [bool]) { $e.Add("${at}: isActive must be true or false.") }
        $stock = $t.demoStock ?? 0
        if ($stock -isnot [long] -and $stock -isnot [int] -or $stock -lt 0 -or $stock -gt 5) { $e.Add("${at}: demoStock must be an integer 0-5.") }
        elseif ($stock -gt 0) {
            if ("$($t.demoCode)" -cnotmatch '^[A-Z0-9]{2,12}$') { $e.Add("${at}: demoCode must be 2-12 uppercase letters/digits.") }
            elseif ($codes.ContainsKey($t.demoCode)) { $e.Add("${at}: duplicate demoCode '$($t.demoCode)'.") }
            $codes["$($t.demoCode)"] = $true
        }
    }
    , $e
}

function Invoke-Api([string]$Method, [string]$ApiPath, $Body = $null) {
    $headers = @{ Origin = $Origin; 'X-LabLend-CSRF' = '1' }
    if ($script:token) { $headers.Authorization = "Bearer $script:token" }
    $request = @{ Uri = "$Gateway$ApiPath"; Method = $Method; Headers = $headers; TimeoutSec = 30; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 5 -Compress; $request.ContentType = 'application/json; charset=utf-8' }
    $response = Invoke-WebRequest @request
    $parsed = $null
    if ($response.Content) { try { $parsed = $response.Content | ConvertFrom-Json } catch { $parsed = $response.Content } }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $parsed }
}
function Get-Api([string]$ApiPath) {
    $r = Invoke-Api GET $ApiPath
    if ($r.Status -ge 400) { throw "GET $ApiPath failed (HTTP $($r.Status)). The account must be LAB_STAFF or ADMIN." }
    $r.Body
}
function Get-AllPages([string]$ApiPath) {
    $items = [System.Collections.Generic.List[object]]::new(); $page = 1
    do {
        $r = Get-Api "$ApiPath&page=$page&pageSize=100"
        foreach ($i in @($r.items)) { $items.Add($i) }
        $page++
    } while ($items.Count -lt $r.total -and @($r.items).Count -gt 0)
    , $items
}
function Reason($r) { "HTTP $($r.Status)" + $(if ($r.Body.detail) { ": $($r.Body.detail)" } elseif ($r.Body.title) { ": $($r.Body.title)" }) }

# ponytail: loads every category/type/asset each run; fine for a lab-sized dataset, add server-side lookups past ~10k assets.
function Get-Snapshot {
    [pscustomobject]@{
        Categories = @(Get-Api '/api/categories?includeInactive=true')
        Types      = (Get-AllPages '/api/equipment-types?includeInactive=true&sortBy=name')
        Assets     = (Get-AllPages '/api/inventory/assets?sortBy=serialNumber')
    }
}
function Write-Counts([string]$Label, $snap) {
    $demo = @($snap.Assets | Where-Object { $_.serialNumber -like 'DEMO-*' }).Count
    Write-Host ("{0}: categories={1}, equipmentTypes={2}, assetUnits={3} (DEMO-={4}, other={5})" -f $Label,
        $snap.Categories.Count, $snap.Types.Count, $snap.Assets.Count, $demo, ($snap.Assets.Count - $demo))
}

# ---- 1. Validate -------------------------------------------------------------------------------------------
$data = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
$errors = Test-Catalog $data
if ($errors.Count) { $errors | ForEach-Object { Write-Host "INVALID  $_" }; throw "$($errors.Count) validation error(s) in $Path; nothing was sent." }
Write-Host "Schema OK: $(@($data.categories).Count) categories, $(@($data.equipmentTypes).Count) equipment types in $Path."
if ($ValidateOnly) { return }
if ($WithDemoStock -and -not ([uri]$Gateway).IsLoopback) { throw '-WithDemoStock only runs against a loopback gateway (local dev/demo stack).' }
if (-not $env:LABLEND_DEMO_ADMIN_PASSWORD) { throw 'Set $env:LABLEND_DEMO_ADMIN_PASSWORD locally (Read-Host -MaskInput). No default password is provided.' }

# ---- 2. Sign in and read current state ------------------------------------------------------------------
$login = Invoke-Api POST '/api/auth/login' @{ email = $AdminEmail; password = $env:LABLEND_DEMO_ADMIN_PASSWORD }
if ($login.Status -ne 200 -or -not $login.Body.accessToken) { throw "Login failed for $AdminEmail ($(Reason $login))." }
$script:token = $login.Body.accessToken
$before = Get-Snapshot
$mode = if ($Apply) { 'APPLY' } else { 'PREVIEW (dry run, nothing is written)' }
Write-Host "Mode: $mode$(if ($UpdateExisting) { ' +UpdateExisting' })$(if ($WithDemoStock) { ' +WithDemoStock' })"
Write-Counts 'Before' $before

$report = [System.Collections.Generic.List[object]]::new()
function Add-Row($Kind, $Name, $Action, $Detail = '') { $report.Add([pscustomobject]@{ Kind = $Kind; Name = $Name; Action = $Action; Detail = $Detail }) }
$created = if ($Apply) { 'Created' } else { 'WouldCreate' }
$updated = if ($Apply) { 'Updated' } else { 'WouldUpdate' }

# ---- 3. Categories ----------------------------------------------------------------------------------------
$catByName = @{}; foreach ($c in $before.Categories) { $catByName[(Key $c.name)] = $c }
$catPlan = @{} # key -> @{ Id; Usable; Location }
foreach ($c in @($data.categories)) {
    $k = Key $c.name; $existing = $catByName[$k]
    $plan = @{ Id = $existing.id; Usable = [bool]$existing.isActive; Location = (Norm $c.demoLocation) }
    $catPlan[$k] = $plan
    if (-not $existing) {
        if (-not $Apply) { $plan.Usable = $true; Add-Row Category $c.name $created; continue }
        $r = Invoke-Api POST '/api/categories' @{ name = (Norm $c.name); description = $c.description }
        if ($r.Status -eq 201) { $plan.Id = $r.Body.id; $plan.Usable = $true; Add-Row Category $c.name $created }
        else { Add-Row Category $c.name Failed (Reason $r) }
        continue
    }
    $diff = @()
    if (-not $existing.isActive) { $diff += 'inactive' }
    if ((Norm $existing.description) -cne (Norm $c.description)) { $diff += 'description' }
    if (-not $diff) { Add-Row Category $c.name Skipped 'already exists'; continue }
    if (-not $UpdateExisting) { Add-Row Category $c.name Conflicted "differs: $($diff -join ', ') (rerun with -UpdateExisting to update)"; continue }
    if (-not $Apply) { $plan.Usable = $true; Add-Row Category $c.name $updated ($diff -join ', '); continue }
    $r = Invoke-Api PUT "/api/categories/$($existing.id)" @{ name = $existing.name; description = $c.description; isActive = $true }
    if ($r.Status -eq 200) { $plan.Usable = $true; Add-Row Category $c.name $updated ($diff -join ', ') } else { Add-Row Category $c.name Failed (Reason $r) }
}

# ---- 4. Equipment types -----------------------------------------------------------------------------------
$typesByName = @{}
foreach ($x in $before.Types) {
    $k = Key $x.name
    if (-not $typesByName.ContainsKey($k)) { $typesByName[$k] = [System.Collections.Generic.List[object]]::new() }
    $typesByName[$k].Add($x)
}
$typePlan = @{} # key -> @{ Type (API object or $null when only planned); Pending }
foreach ($t in @($data.equipmentTypes)) {
    $k = Key $t.name; $cat = $catPlan[(Key $t.category)]
    $matching = @($typesByName[$k] | Where-Object { $_ })
    $same = $matching | Where-Object { $cat.Id -and $_.categoryId -eq $cat.Id } | Select-Object -First 1
    $body = @{ name = (Norm $t.name); description = $t.description; specifications = $t.specifications; tags = @($t.tags | Where-Object { $_ }); imageUrl = $t.imageUrl }
    if (-not $same -and $matching.Count) { Add-Row EquipmentType $t.name Conflicted "name already used in category '$($matching[0].categoryName)'; not moved"; continue }
    if (-not $same) {
        if (-not $cat.Usable) { Add-Row EquipmentType $t.name Failed "category '$($t.category)' is not available"; continue }
        if (-not $Apply) { $typePlan[$k] = @{ Type = $null; Pending = $true }; Add-Row EquipmentType $t.name $created; continue }
        $r = Invoke-Api POST '/api/equipment-types' ($body + @{ categoryId = $cat.Id })
        if ($r.Status -ne 201) { Add-Row EquipmentType $t.name Failed (Reason $r); continue }
        $new = $r.Body
        if (-not $t.isActive) { $d = Invoke-Api DELETE "/api/equipment-types/$($new.id)"; if ($d.Status -eq 204) { $new.isActive = $false } }
        $typePlan[$k] = @{ Type = $new; Pending = $false }; Add-Row EquipmentType $t.name $created
        continue
    }
    $typePlan[$k] = @{ Type = $same; Pending = $false }
    $diff = @()
    if ((Norm $same.description) -cne (Norm $t.description)) { $diff += 'description' }
    if ((Norm $same.specifications) -cne (Norm $t.specifications)) { $diff += 'specifications' }
    if ((TagKey $same.tags) -cne (TagKey $t.tags)) { $diff += 'tags' }
    if ((Norm $same.imageUrl) -cne (Norm $t.imageUrl)) { $diff += 'imageUrl' }
    if ([bool]$same.isActive -ne $t.isActive) { $diff += 'isActive' }
    if (-not $diff) { Add-Row EquipmentType $t.name Skipped 'already exists'; continue }
    if (-not $UpdateExisting) { Add-Row EquipmentType $t.name Conflicted "differs: $($diff -join ', ') (rerun with -UpdateExisting to update)"; continue }
    if (-not $Apply) { Add-Row EquipmentType $t.name $updated ($diff -join ', '); continue }
    $body.name = $same.name # keep the existing spelling; never moves the type to another category
    $r = Invoke-Api PUT "/api/equipment-types/$($same.id)" ($body + @{ categoryId = $same.categoryId; isActive = $t.isActive })
    if ($r.Status -eq 200) { $typePlan[$k].Type = $r.Body; Add-Row EquipmentType $t.name $updated ($diff -join ', ') } else { Add-Row EquipmentType $t.name Failed (Reason $r) }
}

# ---- 5. Optional DEMO stock: create-only, never updates or retires an existing unit ------------------------
if ($WithDemoStock) {
    $assetBySerial = @{}; foreach ($a in $before.Assets) { $assetBySerial[$a.serialNumber.ToUpperInvariant()] = $a }
    foreach ($t in @($data.equipmentTypes | Where-Object { ($_.demoStock ?? 0) -gt 0 })) {
        $plan = $typePlan[(Key $t.name)]
        if (-not $plan) { Add-Row Asset $t.name Skipped 'equipment type not imported (see rows above)'; continue }
        $type = $plan.Type
        if ($type) {
            if ("$($type.id)".StartsWith($SeedTypePrefix)) { Add-Row Asset $t.name Skipped 'seed equipment type; demo stock not added'; continue }
            if (-not $type.isActive) { Add-Row Asset $t.name Skipped 'equipment type inactive'; continue }
            $real = @($before.Assets | Where-Object { $_.equipmentTypeId -eq $type.id -and $_.serialNumber -notlike 'DEMO-*' })
            if ($real.Count) { Add-Row Asset $t.name Skipped "has $($real.Count) real (non-DEMO) unit(s); demo stock not added"; continue }
        }
        $location = $catPlan[(Key $t.category)].Location
        foreach ($i in 1..$t.demoStock) {
            $serial = 'DEMO-{0}-{1:D3}' -f $t.demoCode, $i
            $existing = $assetBySerial[$serial]
            if ($existing) {
                if ($type -and $existing.equipmentTypeId -eq $type.id) { Add-Row Asset $serial Skipped "exists ($($existing.status))" }
                else { Add-Row Asset $serial Conflicted "serial belongs to another equipment type ($($existing.equipmentTypeId))" }
                continue
            }
            if (-not $Apply) { Add-Row Asset $serial $created "$($t.name) @ $location"; continue }
            $r = Invoke-Api POST '/api/inventory/assets' @{ equipmentTypeId = $type.id; serialNumber = $serial; location = $location; notes = $DemoNote }
            if ($r.Status -eq 201) { Add-Row Asset $serial $created "$($t.name) ($($r.Body.status))" } else { Add-Row Asset $serial Failed (Reason $r) }
        }
    }
}

# ---- 6. Report ---------------------------------------------------------------------------------------------
$changes = @($report | Where-Object Action -NE 'Skipped')
if ($changes.Count) { $changes | Format-Table Kind, Action, Name, Detail -AutoSize -Wrap | Out-String -Width 200 | Write-Host }
Write-Host 'Summary:'
$report | Group-Object Kind, Action | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-28} {1}" -f $_.Name, $_.Count) }
$after = if ($Apply) { Get-Snapshot } else { $before }
Write-Counts $(if ($Apply) { 'After' } else { 'After (unchanged: preview)' }) $after

$liveUnits = @{}; foreach ($a in $after.Assets) { if ($a.status -notin 'RETIRED', 'LOST') { $liveUnits[$a.equipmentTypeId] = 1 + [int]$liveUnits[$a.equipmentTypeId] } }
$activeTypes = @($after.Types | Where-Object isActive)
$noImage = @($activeTypes | Where-Object { -not $_.imageUrl })
$noStock = @($activeTypes | Where-Object { -not $liveUnits[$_.id] })
Write-Host "Active equipment types without image: $($noImage.Count)/$($activeTypes.Count)"
Write-Host "Active equipment types without stock (no unit outside RETIRED/LOST): $($noStock.Count)/$($activeTypes.Count)"
$noStock | ForEach-Object { Write-Host "  - $($_.name)" }

$script:token = $null
if (@($report | Where-Object Action -EQ 'Failed').Count) { exit 1 }
