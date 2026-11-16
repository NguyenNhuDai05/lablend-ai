# LOCAL DEVELOPMENT ONLY: mints an HS256 JWT signed with JWT_SIGNING_KEY from .env, so Catalog/Inventory/
# Notification can be tested before Identity login exists. Remove once Identity issues real tokens.
# Usage: ./scripts/New-DevToken.ps1 -Role LAB_STAFF   → paste into Swagger "Authorize" or curl -H "Authorization: Bearer ..."
[CmdletBinding()]
param(
    [ValidateSet('STUDENT','LAB_STAFF','ADMIN')][string]$Role = 'LAB_STAFF',
    [Guid]$UserId = [Guid]::NewGuid(),
    [ValidateRange(1, 480)][int]$Minutes = 60
)
$ErrorActionPreference = 'Stop'
$envPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Run scripts/Initialize-Environment.ps1 first.' }
$key = (Get-Content -LiteralPath $envPath | Where-Object { $_ -match '^JWT_SIGNING_KEY=.+' } | Select-Object -First 1) -replace '^JWT_SIGNING_KEY=', ''
if (-not $key) { throw 'JWT_SIGNING_KEY is missing in .env.' }

function ConvertTo-Base64Url([byte[]]$Bytes) { [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
$utf8 = [Text.Encoding]::UTF8
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$claims = [ordered]@{ iss = 'lablend-ai'; aud = 'lablend-web'; sub = "$UserId"; role = $Role; iat = $now; nbf = $now; exp = $now + 60 * $Minutes }
$unsigned = (ConvertTo-Base64Url $utf8.GetBytes('{"alg":"HS256","typ":"JWT"}')) + '.' + (ConvertTo-Base64Url $utf8.GetBytes(($claims | ConvertTo-Json -Compress)))
$hmac = [Security.Cryptography.HMACSHA256]::new($utf8.GetBytes($key))
try { "$unsigned." + (ConvertTo-Base64Url ($hmac.ComputeHash($utf8.GetBytes($unsigned)))) } finally { $hmac.Dispose() }
