#Requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$config=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($config.project -notmatch '^lablend-gemini-live-[0-9a-f]{10}$') { throw 'Only an isolated live acceptance project is supported.' }
$restore=@{}
function Set-PrivateEnvironment([string]$Name,[string]$Value) {
    $restore[$Name]=[Environment]::GetEnvironmentVariable($Name,'Process')
    [Environment]::SetEnvironmentVariable($Name,$Value,'Process')
}
try {
    foreach($line in Get-Content -LiteralPath $config.envFile) {
        if($line -match '^([A-Z_]+)=(.*)$') { Set-PrivateEnvironment $Matches[1] $Matches[2] }
    }
    Set-PrivateEnvironment 'COMPOSE_PROJECT_NAME' $config.project
    Set-PrivateEnvironment 'COMPOSE_FILE' ((Join-Path $repoRoot 'docker-compose.yml')+[IO.Path]::PathSeparator+$config.overrideFile)
    & (Join-Path $PSScriptRoot 'Start-Core.ps1') -IncludeBonus -WaitTimeout 240
} finally {
    foreach($entry in $restore.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process') }
}
