#Requires -Version 7
[CmdletBinding()]
param(
    [string]$Repository = 'https://github.com/NguyenNhuDai05/lablend-ai.git',
    [string]$Revision = 'main',
    [string]$CloneParent = '.tmp/clean-install',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$parent = [IO.Path]::GetFullPath($CloneParent, $repoRoot)
[IO.Directory]::CreateDirectory($parent) | Out-Null
$project = 'lablend-t20-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$clone = Join-Path $parent $project
$evidence = Join-Path $parent "$project-evidence"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$summary = [ordered]@{ project = $project; clone = $clone; revision = $Revision; status = 'NOT RUN'; stages = @() }
function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Output "T20: $Name"
    $summary.stages += @{ name = $Name; status = 'RUNNING' }
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
    $summary.stages[-1].status = 'PASS'
}
function New-Secret { 'Aa1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) }
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}
$savedEnvironment = @{}
$names = @('COMPOSE_PROJECT_NAME','COMPOSE_FILE','GATEWAY_PORT','WEB_PORT','Bootstrap__AdminPassword',
    'LABLEND_E2E_ORIGIN','LABLEND_E2E_GATEWAY','LABLEND_E2E_ADMIN_PASSWORD','LABLEND_E2E_EVIDENCE','LABLEND_E2E_PROJECT')
foreach ($name in $names) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$started = $false
try {
    Invoke-Step 'daemon' { docker version --format '{{.Server.Version}}' }
    Invoke-Step 'clean clone' { git clone --no-local -- $Repository $clone }
    Push-Location $clone
    try {
        Invoke-Step 'checkout revision' { git checkout --detach $Revision }
        $summary.commit = (git rev-parse HEAD).Trim()
        Invoke-Step 'generate environment' { & ./scripts/Initialize-Environment.ps1 }
        $env:COMPOSE_PROJECT_NAME = $project
        $env:GATEWAY_PORT = [string](Get-FreePort)
        do { $env:WEB_PORT = [string](Get-FreePort) } while ($env:WEB_PORT -eq $env:GATEWAY_PORT)
        $env:Bootstrap__AdminPassword = New-Secret
        # Credentials only in the child process environment; override file contains interpolation, never values.
        $override = Join-Path $clone '.tmp-t20.compose.yml'
        @'
services:
  identity:
    environment:
      Bootstrap__Enabled: "true"
      Bootstrap__AdminEmail: "admin@t20.test"
      Bootstrap__AdminPassword: "${Bootstrap__AdminPassword:?private T20 bootstrap required}"
  borrowing-worker:
    environment:
      Reminders__PollSeconds: "2"
'@ | Set-Content -LiteralPath $override -Encoding utf8
        $env:COMPOSE_FILE = (Join-Path $clone 'docker-compose.yml') + [IO.Path]::PathSeparator + $override
        Invoke-Step 'compose configuration' { docker compose config --quiet }
        # Verify scope before any fixture containers are created. Existing named resources must never be reused.
        $existing = @(docker ps -aq --filter "label=com.docker.compose.project=$project")
        $volumes = @(docker volume ls -q --filter "label=com.docker.compose.project=$project")
        if ($existing.Count -or $volumes.Count) { throw 'Generated project is not fresh; refusing to reuse resources.' }
        Invoke-Step 'build sequential images' { & ./scripts/Build-Images.ps1 }
        $started = $true
        Invoke-Step 'start private core' { docker compose up -d --no-build --wait --wait-timeout 240 }
        docker compose ps --all --format json | Set-Content -LiteralPath (Join-Path $evidence 'containers.json') -Encoding utf8
        $env:LABLEND_E2E_ORIGIN = "http://localhost:$env:WEB_PORT"
        $env:LABLEND_E2E_GATEWAY = "http://localhost:$env:GATEWAY_PORT"
        $env:LABLEND_E2E_ADMIN_PASSWORD = $env:Bootstrap__AdminPassword
        $env:LABLEND_E2E_EVIDENCE = $evidence
        $env:LABLEND_E2E_PROJECT = $project
        Push-Location apps/web
        try {
            Invoke-Step 'frontend dependencies' { npm ci }
            Invoke-Step 'Playwright browser' { npx playwright install chromium }
            Invoke-Step 'three-flow Docker E2E' { node e2e/docker-core.mjs }
        } finally { Pop-Location }
        Invoke-Step 'database isolation' { Get-Content -Raw infrastructure/database/verify-isolation.sh | docker compose exec -T postgres bash }
        $summary.status = 'PASS'
    } finally { Pop-Location }
} catch {
    $summary.status = if ($summary.stages[-1].name -eq 'daemon') { 'BLOCKED' } else { 'FAIL' }
    $summary.error = $_.Exception.Message
    if ($summary.stages.Count) { $summary.stages[-1].status = $summary.status }
    Write-Warning $summary.error
} finally {
    if ($started -and -not $KeepRunning) {
        Push-Location $clone
        try {
            # Only containers/networks of this random project; preserve every database/Redis volume.
            docker compose -p $project down --remove-orphans
            $summary.cleanupExitCode = $LASTEXITCODE
        } finally { Pop-Location }
    }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 't20-result.json') -Encoding utf8
    Write-Output "T20 result: $($summary.status). Evidence: $evidence"
}
if ($summary.status -ne 'PASS') { exit 1 }
