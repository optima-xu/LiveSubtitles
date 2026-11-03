param(
    [string]$RuntimeDirectory,
    [string]$WslDistribution='Ubuntu-24.04',
    [string]$LinuxRuntimeDirectory='/opt/live-subtitles',
    [string]$AsrModel='Qwen/Qwen3-ASR-0.6B',
    [string]$TranslationModel='qwen3.5:4b-q4_K_M',
    [string]$PythonIndexUrl='',
    [int]$OllamaPort=11436
)
$ErrorActionPreference='Stop'
function Require-Success([string]$Message){if($LASTEXITCODE -ne 0){throw $Message}}
function Convert-LinuxPath([string]$Path){$p=[IO.Path]::GetFullPath($Path);if($p -notmatch '^[A-Za-z]:\\'){throw 'Choose a local Windows drive for RuntimeDirectory.'};return '/mnt/'+$p.Substring(0,1).ToLowerInvariant()+$p.Substring(2).Replace('\','/')}
if(-not $RuntimeDirectory){$RuntimeDirectory=Join-Path $PSScriptRoot '.runtime'}
$RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
if($LinuxRuntimeDirectory -notmatch '^/opt/live-subtitles[-a-zA-Z0-9_]*$'){throw 'LinuxRuntimeDirectory must be a dedicated /opt/live-subtitles directory (optional suffix).'}
if($AsrModel -notin @('Qwen/Qwen3-ASR-0.6B','Qwen/Qwen3-ASR-1.7B')){throw 'Unsupported ASR model.'}
Get-Command wsl -ErrorAction Stop | Out-Null
& wsl -d $WslDistribution --exec true
Require-Success "Install WSL2 Ubuntu first: wsl --install -d $WslDistribution"
& wsl -d $WslDistribution --exec sh -c 'command -v gcc >/dev/null && test -e /usr/include/python3.12/Python.h'
if($LASTEXITCODE -ne 0){
    Write-Host 'Installing WSL compiler prerequisites for CUDA inference.'
    & wsl -d $WslDistribution -u root --exec apt-get update -qq
    Require-Success 'Cannot update WSL package index.'
    & wsl -d $WslDistribution -u root --exec apt-get install -y --no-install-recommends build-essential python3-dev
    Require-Success 'Cannot install WSL compiler prerequisites.'
}
$linuxUid=((& wsl -d $WslDistribution --exec id -u) -join '').Trim()
$linuxGid=((& wsl -d $WslDistribution --exec id -g) -join '').Trim()
if($linuxUid -notmatch '^\d+$' -or $linuxGid -notmatch '^\d+$'){throw 'Cannot determine WSL user.'}
& wsl -d $WslDistribution --exec test -d $LinuxRuntimeDirectory
if($LASTEXITCODE -ne 0){& wsl -d $WslDistribution -u root --exec install -d -m 0755 -o $linuxUid -g $linuxGid $LinuxRuntimeDirectory;Require-Success 'Cannot create Linux runtime directory.'}
New-Item -ItemType Directory -Force -Path $RuntimeDirectory | Out-Null
$setup=Convert-LinuxPath (Join-Path $PSScriptRoot 'server\setup_runtime.py')
Write-Host 'Installing isolated ASR dependencies. The first installation downloads several GB.'
if($PythonIndexUrl){& wsl -d $WslDistribution --exec env "ASR_PIP_INDEX_URL=$PythonIndexUrl" python3 $setup $LinuxRuntimeDirectory}
else {& wsl -d $WslDistribution --exec python3 $setup $LinuxRuntimeDirectory}
Require-Success 'ASR dependency installation failed.'
$linuxPython="$LinuxRuntimeDirectory/asr-venv/bin/python"
$download=Convert-LinuxPath (Join-Path $PSScriptRoot 'server\download_models.py')
& wsl -d $WslDistribution --exec $linuxPython $download $AsrModel (Convert-LinuxPath (Join-Path $RuntimeDirectory 'models'))
Require-Success 'ASR model download failed.'
$ollama=(Get-Command ollama -ErrorAction SilentlyContinue).Source
if(-not $ollama){$ollama=Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'}
if(-not(Test-Path -LiteralPath $ollama)){throw 'Install Ollama from https://ollama.com/download/windows, then rerun Install.ps1.'}
$oldHost=$env:OLLAMA_HOST;$oldModels=$env:OLLAMA_MODELS;$oldParallel=$env:OLLAMA_NUM_PARALLEL
$ollamaUrl="http://127.0.0.1:$OllamaPort"
$installerService=$null
try{
    $env:OLLAMA_HOST="127.0.0.1:$OllamaPort";$env:OLLAMA_MODELS=Join-Path $RuntimeDirectory 'ollama-models';$env:OLLAMA_NUM_PARALLEL='1'
    try{Invoke-RestMethod "$ollamaUrl/api/version" -TimeoutSec 3 | Out-Null;$ready=$true}catch{$ready=$false}
    if(-not $ready){$installerService=Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden -RedirectStandardOutput (Join-Path $RuntimeDirectory 'ollama.out.log') -RedirectStandardError (Join-Path $RuntimeDirectory 'ollama.err.log') -PassThru}
    for($i=0;$i -lt 45;$i++){try{Invoke-RestMethod "$ollamaUrl/api/version" -TimeoutSec 2 | Out-Null;$ready=$true;break}catch{Start-Sleep -Seconds 1}}
    if(-not $ready){throw 'Ollama did not start. Check the runtime logs.'}
    & $ollama pull $TranslationModel
    Require-Success 'Translation model download failed.'
    $downloaded=(Invoke-RestMethod "$ollamaUrl/api/tags" -TimeoutSec 10).models | Where-Object name -eq $TranslationModel
    if(-not $downloaded){throw 'Downloaded model was not found in Ollama.'}
    Write-Host 'Translation model digest:' $downloaded.digest
}finally{
    if($installerService -and -not $installerService.HasExited){Stop-Process -Id $installerService.Id -ErrorAction SilentlyContinue}
    $env:OLLAMA_HOST=$oldHost;$env:OLLAMA_MODELS=$oldModels;$env:OLLAMA_NUM_PARALLEL=$oldParallel
}
$appRoot=$PSScriptRoot
if(-not(Test-Path -LiteralPath (Join-Path $appRoot 'app\LiveSubtitles.exe'))){
    Get-Command dotnet -ErrorAction Stop | Out-Null
    $appRoot=Join-Path $PSScriptRoot 'dist\LiveSubtitles'
    & (Join-Path $PSScriptRoot 'scripts\Build.ps1') -OutputDirectory $appRoot -SelfContained
}
New-Item -ItemType Directory -Force -Path (Join-Path $appRoot 'data') | Out-Null
$settingsPath=Join-Path $appRoot 'data\settings.json'
$settings=if(Test-Path -LiteralPath $settingsPath){Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json}else{[pscustomobject]@{}}
foreach($entry in @{RuntimeDirectory=$RuntimeDirectory;WslDistribution=$WslDistribution;WslRuntimeDirectory=$LinuxRuntimeDirectory;AsrModel=$AsrModel;TranslationModel=$TranslationModel;OllamaUrl=$ollamaUrl}.GetEnumerator()){$settings | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Set-Content -LiteralPath (Join-Path $appRoot 'runtime-path.txt') -Value $RuntimeDirectory -Encoding utf8
Write-Host "Installed. Start: $appRoot\Start.ps1"
