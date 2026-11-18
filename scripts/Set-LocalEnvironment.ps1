# Dot-source this script in the same terminal as dotnet run / dotnet ef.
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('gateway','identity','catalog','inventory','borrowing','notification','recommendation','worker')][string]$Service)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Run scripts/Initialize-Environment.ps1 first.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envPath) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Jwt__SigningKey = $settings['JWT_SIGNING_KEY']
$env:Jwt__Issuer = 'lablend-ai'
$env:Jwt__Audience = 'lablend-web'
$env:ConnectionStrings__Redis = "localhost:$($settings['REDIS_DEV_PORT']),password=$($settings['REDIS_PASSWORD'])"
$env:Inventory__ServiceToken = $settings['INVENTORY_SERVICE_TOKEN']
$env:Gemini__Enabled = if ($Service -eq 'recommendation' -and $settings['GEMINI_ENABLED']) { $settings['GEMINI_ENABLED'] } else { 'false' }
$env:Gemini__ApiKey = if ($Service -eq 'recommendation') { $settings['GEMINI_API_KEY'] } else { '' }
$env:Gemini__Model = if ($settings['GEMINI_MODEL']) { $settings['GEMINI_MODEL'] } else { 'gemini-3.5-flash-lite' }
$env:Gemini__TimeoutSeconds = if ($settings['GEMINI_TIMEOUT_SECONDS']) { $settings['GEMINI_TIMEOUT_SECONDS'] } else { '30' }
$env:Gemini__MaxOutputTokens = if ($settings['GEMINI_MAX_OUTPUT_TOKENS']) { $settings['GEMINI_MAX_OUTPUT_TOKENS'] } else { '4096' }
$env:Recommendation__DailyLimit = if ($settings['RECOMMENDATION_DAILY_LIMIT']) { $settings['RECOMMENDATION_DAILY_LIMIT'] } else { '20' }
$env:Catalog__BaseAddress = 'http://localhost:5102/'
$env:Inventory__GrpcAddress = 'http://localhost:5181'
$ports = @{gateway=5080; identity=5101; catalog=5102; inventory=5103; borrowing=5104; notification=5105; recommendation=5106}
if ($Service -eq 'gateway') {
    $env:ASPNETCORE_URLS = "http://localhost:$($settings['GATEWAY_PORT'])"
    $env:Cors__AllowedOrigins__0 = 'http://localhost:5173'
    $env:Cors__AllowedOrigins__1 = "http://localhost:$($settings['WEB_PORT'])"
} else {
    $context = if ($Service -eq 'worker') { 'borrowing' } else { $Service }
    $passwordName = $context.ToUpperInvariant() + '_DB_PASSWORD'
    $env:ConnectionStrings__Database = "Host=localhost;Port=$($settings['POSTGRES_DEV_PORT']);Database=$($context)_db;Username=$($context)_user;Password=$($settings[$passwordName]);Timeout=3"
    if ($Service -eq 'inventory') {
        $env:Kestrel__Endpoints__Rest__Url = 'http://localhost:5103'
        $env:Kestrel__Endpoints__Grpc__Url = 'http://localhost:5181'
    } elseif ($Service -ne 'worker') {
        $env:ASPNETCORE_URLS = "http://localhost:$($ports[$Service])"
    }
}
Write-Host "Local environment loaded for $Service. Credentials are not printed."
