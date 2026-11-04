$ErrorActionPreference='Continue'
$repoRoot=Split-Path -Parent $PSScriptRoot
$settingsFile=Join-Path $repoRoot 'data\settings.json'
if(-not(Test-Path -LiteralPath $settingsFile)){$settingsFile=Join-Path $repoRoot 'dist\LiveSubtitles\data\settings.json'}
if(-not(Test-Path -LiteralPath $settingsFile)){Write-Host 'Run Install.cmd first.';exit 1}
$settings=Get-Content -LiteralPath $settingsFile -Raw -Encoding utf8 | ConvertFrom-Json
if(Get-Command nvidia-smi -ErrorAction SilentlyContinue){nvidia-smi --query-gpu=name,memory.total,memory.free --format=csv,noheader}
Write-Host 'Ollama:' $settings.OllamaUrl
foreach($endpoint in @('/api/version','/api/tags','/api/ps')){
    try{Invoke-RestMethod ($settings.OllamaUrl.TrimEnd('/')+$endpoint) -TimeoutSec 5 | ConvertTo-Json -Depth 6}catch{Write-Host $_.Exception.Message}
}
Write-Host 'VAD installed:' (Test-Path -LiteralPath (Join-Path $settings.RuntimeDirectory 'vad\silero_vad.onnx'))
Write-Host 'Logs:' (Join-Path (Split-Path -Parent $settingsFile) 'diagnostics.log')
