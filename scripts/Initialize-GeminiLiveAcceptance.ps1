#Requires -Version 7
# Preparation only. Does not call generateContent or touch the application's databases.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $source[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'") }
}
if ($source['GEMINI_ENABLED'] -ne 'true' -or [string]::IsNullOrWhiteSpace($source['GEMINI_API_KEY'])) { throw 'Live acceptance BLOCKED: API key Not Configured or feature disabled.' }
if ($source['GEMINI_MODEL'] -ne 'gemini-3.5-flash-lite') { throw 'Live acceptance BLOCKED: model must first be verified against Google metadata and pricing.' }
function New-Secret { 'Aa1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) }
function Free-Port {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}
$project='lablend-gemini-live-' + [Guid]::NewGuid().ToString('N').Substring(0,10)
$directory=Join-Path $repoRoot ('.tmp/' + $project); New-Item -ItemType Directory -Path $directory | Out-Null
$values=@{}; $names=@('POSTGRES_ADMIN_PASSWORD','IDENTITY_DB_PASSWORD','CATALOG_DB_PASSWORD','INVENTORY_DB_PASSWORD',
    'BORROWING_DB_PASSWORD','NOTIFICATION_DB_PASSWORD','RECOMMENDATION_DB_PASSWORD','REDIS_PASSWORD','JWT_SIGNING_KEY','INVENTORY_SERVICE_TOKEN')
foreach($name in $names) { $values[$name]=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
$values.GATEWAY_PORT=Free-Port; $values.WEB_PORT=Free-Port
$values.GEMINI_ENABLED='true'; $values.GEMINI_API_KEY=$source['GEMINI_API_KEY']; $values.GEMINI_MODEL=$source['GEMINI_MODEL']
$values.GEMINI_TIMEOUT_SECONDS=if($source['GEMINI_TIMEOUT_SECONDS']) {$source['GEMINI_TIMEOUT_SECONDS']} else {'30'}
$values.GEMINI_MAX_OUTPUT_TOKENS=if($source['GEMINI_MAX_OUTPUT_TOKENS']) {$source['GEMINI_MAX_OUTPUT_TOKENS']} else {'4096'}
$values.RECOMMENDATION_DAILY_LIMIT='1'
$envPath=Join-Path $directory 'acceptance.env'; ($values.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Name + '=' + $_.Value }) | Set-Content -LiteralPath $envPath -Encoding utf8NoBOM
$override=Join-Path $directory 'acceptance.compose.yml'
$services=@('identity','catalog','inventory','borrowing','notification','borrowing-worker','gateway','recommendation','web')
$yaml=@('services:'); foreach($service in $services) { $yaml+=('  '+$service+':'); $yaml+=('    image: lablend-ai-'+$service+':latest') }
$yaml | Set-Content -LiteralPath $override -Encoding utf8NoBOM
$manifest=[ordered]@{
    project=$project; envFile=$envPath; overrideFile=$override; webUrl=('http://localhost:'+$values.WEB_PORT); gatewayUrl=('http://localhost:'+$values.GATEWAY_PORT)
    studentEmail=('student-'+$project+'@acceptance.test'); studentPassword=(New-Secret); otherEmail=('other-'+$project+'@acceptance.test'); otherPassword=(New-Secret)
    signingKey=$values.JWT_SIGNING_KEY; maximumGenerationAttempts=1; configuredModel=$values.GEMINI_MODEL; evidenceDirectory=(Join-Path $directory 'evidence')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'private-manifest.json') -Encoding utf8NoBOM
# Contains secrets; intentionally output only its ignored local path.
Write-Output ('Prepared isolated acceptance manifest: ' + (Join-Path $directory 'private-manifest.json'))
