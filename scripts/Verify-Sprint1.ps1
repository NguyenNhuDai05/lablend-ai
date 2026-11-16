[CmdletBinding()]
param([switch]$IncludeBonus)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# Runtime checks are read-only. Auth/loan writes and concurrency tests use the isolated PostgreSQL fixture.
& (Join-Path $PSScriptRoot 'Verify-Sprint0.ps1') -IncludeBonus:$IncludeBonus
if ($LASTEXITCODE -ne 0) { throw 'Platform regression verification failed.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$gateway = "http://localhost:$($settings['GATEWAY_PORT'])"
$specs = @{}
foreach ($service in @('identity','borrowing')) {
    $specs[$service] = Invoke-RestMethod "$gateway/platform/$service/openapi.json"
}
$required = @(
    @{ Service='identity'; Path='/api/auth/register'; Method='post' },
    @{ Service='identity'; Path='/api/auth/login'; Method='post' },
    @{ Service='identity'; Path='/api/auth/refresh'; Method='post' },
    @{ Service='identity'; Path='/api/auth/logout'; Method='post' },
    @{ Service='identity'; Path='/api/auth/me'; Method='get' },
    @{ Service='identity'; Path='/api/users/me'; Method='get' },
    @{ Service='identity'; Path='/api/users/me'; Method='put' },
    @{ Service='identity'; Path='/api/users/me/change-password'; Method='post' },
    @{ Service='identity'; Path='/api/users'; Method='get' },
    @{ Service='identity'; Path='/api/users/{id}'; Method='get' },
    @{ Service='identity'; Path='/api/users/{id}'; Method='put' },
    @{ Service='identity'; Path='/api/users/{id}/active'; Method='patch' },
    @{ Service='identity'; Path='/api/users/{id}/role'; Method='put' },
    @{ Service='identity'; Path='/api/users/{id}/role'; Method='patch' },
    @{ Service='borrowing'; Path='/api/loans'; Method='post' },
    @{ Service='borrowing'; Path='/api/loans'; Method='get' },
    @{ Service='borrowing'; Path='/api/loans/my'; Method='get' },
    @{ Service='borrowing'; Path='/api/loans/{id}'; Method='get' },
    @{ Service='borrowing'; Path='/api/loans/{id}/cancel'; Method='post' },
    @{ Service='borrowing'; Path='/api/loans/{id}/reject'; Method='post' }
)
foreach ($operation in $required) {
    $path = $specs[$operation.Service].paths.PSObject.Properties[$operation.Path]
    if (-not $path -or -not $path.Value.PSObject.Properties[$operation.Method]) {
        throw "Missing actual Swagger operation: $($operation.Service) $($operation.Method) $($operation.Path)"
    }
}
Write-Output "PASS: all $($required.Count) Identity/Borrowing operations are present in actual OpenAPI through Gateway."
foreach ($path in @('/api/auth/me','/api/loans/my')) {
    $status = 0
    try {
        Invoke-WebRequest "$gateway$path" -UseBasicParsing -ErrorAction Stop | Out-Null
        $status = 200
    } catch {
        if (-not $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
    }
    if ($status -ne 401) { throw "Anonymous $path returned $status instead of 401." }
    Write-Output "PASS: anonymous GET $path through running Compose Gateway returns 401."
}
Push-Location $repoRoot
try {
    $sql = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
    foreach ($service in @('identity','borrowing')) {
        $database = $service + '_db'
        $user = $service + '_user'
        # SQL via stdin preserves quoted identifiers in both Windows PowerShell and PowerShell 7.
        $migrations = @($sql | docker compose exec -T postgres psql -U $user -d $database -At -v ON_ERROR_STOP=1)
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect $database migrations." }
        $expected = if ($service -eq 'identity') { '20261115162335_InitialIdentity' } else { '20261115163138_InitialBorrowing' }
        if ($migrations -notcontains $expected) { throw "Required migration is not applied in $database." }
        Write-Output "PASS: $database has applied migration $expected (read using $user)."
    }
} finally { Pop-Location }
Write-Output 'PASS: Sprint 1 runtime checks. Actual Catalog business integration remains pending Member 2.'
