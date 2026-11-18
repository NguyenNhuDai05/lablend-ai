#Requires -Version 7
<#
.SYNOPSIS
    Share the running LabLend stack through a temporary public https link (Cloudflare Quick Tunnel).
.DESCRIPTION
    Starts (or rebuilds) the Docker stack plus a cloudflared container, reads the random
    https://*.trycloudflare.com URL from its log and recreates Identity so sign-in cookies
    are accepted from that origin. The link lives while this machine and Docker are running;
    it changes every time the tunnel restarts, so rerun this script after a restart.
    Anyone with the link can open the site and register a STUDENT account: share it only
    with the team and run -Stop afterwards.
.EXAMPLE
    ./scripts/Share-Web.ps1 -Build            # rebuild images from this checkout, then share
    ./scripts/Share-Web.ps1                   # share the current images
    ./scripts/Share-Web.ps1 -Stop             # close the link
    ./scripts/Share-Web.ps1 -EnvFile ../lablend-ai/.env -ProjectName lablend-ai   # reuse another checkout's .env and data volumes
#>
[CmdletBinding()]
param(
    [string]$EnvFile = (Join-Path $PSScriptRoot '../.env'),
    [string]$ProjectName,
    [switch]$Build,
    [switch]$Stop,
    [int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Missing $EnvFile. Run scripts/Initialize-Environment.ps1 first." }
$compose = @('compose', '--env-file', (Resolve-Path $EnvFile), '-f', (Join-Path $root 'docker-compose.yml'), '-f', (Join-Path $root 'docker-compose.share.yml'))
if ($ProjectName) { $compose += @('-p', $ProjectName) }
function Compose { & docker @compose @args; if ($LASTEXITCODE) { throw "docker $($args -join ' ') failed ($LASTEXITCODE)." } }

if ($Stop) {
    Compose stop tunnel
    Remove-Item Env:PUBLIC_ORIGIN -ErrorAction SilentlyContinue
    Compose up -d --no-deps --wait identity   # back to localhost-only origins
    Write-Host 'Public link closed.'
    return
}

Remove-Item Env:PUBLIC_ORIGIN -ErrorAction SilentlyContinue
if ($Build) { Compose up -d --wait --build } else { Compose up -d --wait }
Compose up -d --no-deps --force-recreate tunnel   # fresh tunnel = fresh URL, so old log lines cannot be picked up

$deadline = (Get-Date).AddSeconds($TimeoutSeconds); $url = $null
while (-not $url -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $url = (& docker @compose logs --no-log-prefix tunnel 2>&1 | Select-String -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -AllMatches |
        ForEach-Object { $_.Matches.Value } | Select-Object -Last 1)
}
if (-not $url) { throw "No trycloudflare.com URL within $TimeoutSeconds s. Check: docker $($compose -join ' ') logs tunnel" }

$env:PUBLIC_ORIGIN = $url
Compose up -d --no-deps --wait identity
Remove-Item Env:PUBLIC_ORIGIN

# The tunnel may need a few seconds before the hostname resolves publicly.
$ok = $false
foreach ($i in 1..15) {
    try { $ok = (Invoke-WebRequest "$url/web-health" -TimeoutSec 10 -SkipHttpErrorCheck).StatusCode -eq 200 } catch { }
    if ($ok) { break }; Start-Sleep -Seconds 3
}
Write-Host ''
Write-Host "Public link: $url" -ForegroundColor Green
if (-not $ok) { Write-Warning 'The link did not answer yet from this machine; try it in a browser in a minute.' }
Write-Host 'Gateway limits each client IP to 120 requests/minute; through the tunnel everyone shares one IP.'
Write-Host ("Close it with: ./scripts/Share-Web.ps1 -Stop" + $(if ($ProjectName) { " -EnvFile $EnvFile -ProjectName $ProjectName" }))
