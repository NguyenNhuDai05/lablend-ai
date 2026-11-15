[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    Write-Host '.env already exists; preserved. No values were overwritten.'
    exit 0
}
$secretNames = @('POSTGRES_ADMIN_PASSWORD','IDENTITY_DB_PASSWORD','CATALOG_DB_PASSWORD',
    'INVENTORY_DB_PASSWORD','BORROWING_DB_PASSWORD','NOTIFICATION_DB_PASSWORD',
    'RECOMMENDATION_DB_PASSWORD','REDIS_PASSWORD','JWT_SIGNING_KEY','INVENTORY_SERVICE_TOKEN')
$lines = Get-Content -LiteralPath (Join-Path $repoRoot '.env.example')
$result = foreach ($line in $lines) {
    if ($line -match '^([A-Z_]+)=$' -and $secretNames -contains $Matches[1]) {
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $Matches[1] + '=' + ([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant())
    } else { $line }
}
# CreateNew is deliberate: never overwrite another terminal's concurrent setup.
$stream = [IO.File]::Open($envPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $encoding = New-Object Text.UTF8Encoding $false
    $data = $encoding.GetBytes(($result -join "`n") + "`n")
    $stream.Write($data, 0, $data.Length)
} finally { $stream.Dispose() }
Write-Host 'Created ignored .env with random local credentials. Gemini is disabled. No secret values printed.'
