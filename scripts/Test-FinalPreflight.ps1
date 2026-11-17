#Requires -Version 7
[CmdletBinding()]
param([string]$EvidenceDirectory = '.tmp/final-preflight', [string]$ProjectName = 'lablend-ai')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = [IO.Path]::GetFullPath($EvidenceDirectory, $repoRoot)
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$secretValues = @()
if (Test-Path -LiteralPath (Join-Path $repoRoot '.env')) {
    $secretValues = @(Get-Content -LiteralPath (Join-Path $repoRoot '.env') | ForEach-Object {
        if ($_ -match '^[A-Z_]*(PASSWORD|SIGNING_KEY|SERVICE_TOKEN|API_KEY)=(.+)$') { $Matches[2] }
    })
}
function Invoke-Probe([string]$File, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($arg in $Arguments) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true); $process.WaitForExit()
        return @{ exitCode = -1; output = 'BLOCKED: command timed out after 30 seconds.' }
    }
    $result = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    foreach ($secret in $secretValues) { $result = $result.Replace($secret, '[redacted]') }
    return @{ exitCode = $process.ExitCode; output = $result.Trim() }
}
$report = [ordered]@{
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    commit = (Invoke-Probe git @('rev-parse','HEAD')).output
    branch = (Invoke-Probe git @('branch','--show-current')).output
    project = $ProjectName
    compose = Invoke-Probe docker @('compose','-p',$ProjectName,'config','--quiet')
    daemon = Invoke-Probe docker @('version','--format','{{json .Server}}')
}
if ($report.daemon.exitCode -eq 0) {
    $report.containers = Invoke-Probe docker @('compose','-p',$ProjectName,'ps','--all','--format','json')
    $report.workerState = Invoke-Probe docker @('inspect',"$ProjectName-borrowing-worker-1",'--format','{{json .State}}')
    $report.workerLogs = Invoke-Probe docker @('compose','-p',$ProjectName,'logs','--no-color','--tail','120','borrowing-worker')
    $report.resources = Invoke-Probe docker @('info','--format','architecture={{.Architecture}} cpus={{.NCPU}} memoryBytes={{.MemTotal}}')
    $report.images = Invoke-Probe docker @('compose','-p',$ProjectName,'images','--format','json')
} else {
    $report.runtimeStatus = 'BLOCKED: Docker daemon unavailable; worker exit/OOM/logs cannot be inspected. Exit 139 alone is not a diagnosis.'
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputDirectory 'preflight.json') -Encoding utf8
Write-Output "Preflight evidence: $outputDirectory/preflight.json"
if ($report.compose.exitCode -ne 0 -or $report.daemon.exitCode -ne 0) { exit 1 }
Write-Output 'Preflight commands completed. Inspect evidence; this is not business E2E acceptance.'
