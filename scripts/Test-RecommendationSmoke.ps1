#Requires -Version 7
# Isolated Docker runtime smoke: generated test credentials, no Gemini key, no application volume reuse.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = 'lablend-recommendation-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$directory = Join-Path $repoRoot ('.tmp/' + $project)
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$envFile = Join-Path $directory 'smoke.env'
$override = Join-Path $directory 'smoke.compose.yml'
$secretNames = @('POSTGRES_ADMIN_PASSWORD','IDENTITY_DB_PASSWORD','CATALOG_DB_PASSWORD','INVENTORY_DB_PASSWORD',
    'BORROWING_DB_PASSWORD','NOTIFICATION_DB_PASSWORD','RECOMMENDATION_DB_PASSWORD','REDIS_PASSWORD','JWT_SIGNING_KEY','INVENTORY_SERVICE_TOKEN')
$settings = @{}
$lines = foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env.example')) {
    if ($line -match '^([A-Z_]+)=$' -and $secretNames -contains $Matches[1]) {
        $name = $Matches[1]; $value = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $settings[$name] = $value; $name + '=' + $value
    } else { $line }
}
$lines | Set-Content -LiteralPath $envFile -Encoding utf8NoBOM
@'
services:
  recommendation:
    image: lablend-ai-recommendation:latest
    ports:
      - "127.0.0.1::8080"
'@ | Set-Content -LiteralPath $override -Encoding utf8NoBOM
$compose = @('compose','--env-file',$envFile,'-p',$project,'-f',(Join-Path $repoRoot 'docker-compose.yml'),'-f',$override,'--profile','bonus')
function Invoke-SmokeCompose([string[]]$Arguments) {
    & docker @compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Isolated Recommendation smoke Docker command failed.' }
}
function Base64Url([byte[]]$Value) { [Convert]::ToBase64String($Value).TrimEnd('=').Replace('+','-').Replace('/','_') }
function Token([string]$Role) {
    $header = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payload = @{ iss='lablend-ai'; aud='lablend-web'; sub=[Guid]::NewGuid().ToString(); role=$Role; exp=[DateTimeOffset]::UtcNow.AddMinutes(5).ToUnixTimeSeconds() } | ConvertTo-Json -Compress
    $body = $header + '.' + (Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($settings['JWT_SIGNING_KEY']))
    try { return $body + '.' + (Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($body)))) } finally { $hmac.Dispose() }
}
Push-Location $repoRoot
try {
    Invoke-SmokeCompose -Arguments @('up','-d','--no-build','--wait','--wait-timeout','180','postgres','recommendation')
    $address = (& docker @compose port recommendation 8080).Trim()
    if ($LASTEXITCODE -ne 0 -or $address -notmatch '^127\.0\.0\.1:\d+$') { throw 'Unexpected smoke loopback binding.' }
    $base = 'http://' + $address
    $health = Invoke-WebRequest "$base/health/ready" -SkipHttpErrorCheck
    if ($health.StatusCode -ne 200) { throw 'Smoke readiness failed.' }
    $body = @{ purpose='Thử nghiệm smoke không gọi Google'; constraints=@() } | ConvertTo-Json -Compress
    foreach ($role in @('anonymous','LAB_STAFF','STUDENT')) {
        $headers = @{}; if ($role -ne 'anonymous') { $headers.Authorization = 'Bearer ' + (Token $role) }
        $response = Invoke-WebRequest "$base/api/recommendations" -Method Post -ContentType 'application/json' -Body $body -Headers $headers -SkipHttpErrorCheck
        $expected = switch ($role) { 'anonymous' {401} 'LAB_STAFF' {403} 'STUDENT' {503} }
        if ($response.StatusCode -ne $expected) { throw "Unexpected Recommendation HTTP status for $role." }
        # Some PowerShell versions expose application/problem+json as bytes, unlike application/json.
        $responseText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        if ($role -eq 'STUDENT' -and ($responseText | ConvertFrom-Json).code -ne 'AI_UNAVAILABLE') { throw ('Disabled AI returned unexpected code: ' + ($responseText | ConvertFrom-Json).code) }
    }
    Write-Output 'PASS: isolated Docker Recommendation readiness, automatic migrations and JWT 401/403/disabled 503. No Google call or real key.'
} finally {
    # Only this generated fixture; retain all volumes, including its isolated database for inspection.
    & docker @compose down --remove-orphans
    Pop-Location
}
