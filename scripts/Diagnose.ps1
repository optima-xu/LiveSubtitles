$ErrorActionPreference='Continue'
$repoRoot=Split-Path -Parent $PSScriptRoot
$settingsFile=Join-Path $repoRoot 'data\settings.json'
if(-not(Test-Path -LiteralPath $settingsFile)){$settingsFile=Join-Path $repoRoot 'dist\LiveSubtitles\data\settings.json'}
if(-not(Test-Path -LiteralPath $settingsFile)){Write-Host 'Run Install.ps1 first.';exit 1}
$settings=Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
Write-Host 'GPU:'
nvidia-smi --query-gpu=name,memory.total,memory.free --format=csv,noheader
Write-Host 'WSL:'
wsl --list --verbose
$asrUri=if($settings.AsrUrl){[Uri]$settings.AsrUrl}else{[Uri]'ws://127.0.0.1:17861/asr'}
$health="http://$($asrUri.Host):$($asrUri.Port)/health"
foreach($url in @($health,($settings.OllamaUrl+'/api/version'))){try{Write-Host $url;Invoke-RestMethod $url -TimeoutSec 5 | ConvertTo-Json -Depth 4}catch{Write-Host $_.Exception.Message}}
Write-Host 'Logs:' (Join-Path (Split-Path -Parent $settingsFile) 'diagnostics.log')
