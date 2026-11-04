param(
    [string]$RuntimeDirectory,
    [string]$OllamaExecutable,
    [int]$OllamaPort=11436
)
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
function Require-Success([string]$Message){if($LASTEXITCODE -ne 0){throw $Message}}
function Test-Asset([string]$Path,[long]$Size,[string]$Hash){
    return (Test-Path -LiteralPath $Path) -and (Get-Item -LiteralPath $Path).Length -eq $Size -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Hash
}
function Get-Asset([string]$Url,[string]$Path,[long]$Size,[string]$Hash){
    Write-Host "Checking $([IO.Path]::GetFileName($Path))..."
    if(Test-Asset $Path $Size $Hash){return}
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $partial="$Path.partial"
    if((Test-Path -LiteralPath $partial) -and (Get-Item -LiteralPath $partial).Length -ge $Size){
        if(Test-Asset $partial $Size $Hash){Move-Item -LiteralPath $partial -Destination $Path -Force;return}
        Remove-Item -LiteralPath $partial
    }
    Write-Host "Downloading $([IO.Path]::GetFileName($Path)) ($([Math]::Round($Size/1MB)) MB)..."
    & curl.exe --fail --location --retry 3 --retry-all-errors --continue-at - --output $partial $Url
    Require-Success 'Download failed. Rerun Install.cmd to resume.'
    if(-not(Test-Asset $partial $Size $Hash)){throw "Integrity check failed: $partial. The file was not installed."}
    Move-Item -LiteralPath $partial -Destination $Path -Force
}
function Get-OllamaVersion([string]$Path){
    if(-not $Path -or -not(Test-Path -LiteralPath $Path)){return [version]'0.0.0'}
    $output=(& $Path --version 2>&1 | Out-String)
    $matches=[regex]::Matches($output,'\d+\.\d+\.\d+')
    if($matches.Count){return [version]$matches[$matches.Count-1].Value}
    return [version]'0.0.0'
}
function Post-Json([string]$Endpoint,$Body){
    $json=ConvertTo-Json -InputObject $Body -Depth 15 -Compress
    return Invoke-RestMethod -Uri ($script:ollamaUrl+$Endpoint) -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 300
}
function Import-Blob([string]$Path,[string]$Hash){
    $url="$script:ollamaUrl/api/blobs/sha256:$Hash"
    try{Invoke-WebRequest -Uri $url -Method Head -UseBasicParsing -TimeoutSec 10 | Out-Null;return}catch{}
    # --upload-file streams the file instead of allocating multi-GB request bodies.
    & curl.exe --fail --silent --show-error --request POST --upload-file $Path $url
    Require-Success "Ollama could not import $Path"
}
if($OllamaPort -lt 1024 -or $OllamaPort -gt 65535){throw 'OllamaPort must be between 1024 and 65535.'}
$appRoot=$PSScriptRoot
if(-not(Test-Path -LiteralPath (Join-Path $appRoot 'app\LiveSubtitles.exe'))){
    if(-not(Test-Path -LiteralPath (Join-Path $appRoot 'src\LiveSubtitles\LiveSubtitles.csproj'))){throw 'The release is incomplete. Extract the entire ZIP first.'}
    $appRoot=Join-Path $PSScriptRoot 'dist\LiveSubtitles'
    & (Join-Path $PSScriptRoot 'scripts\Build.ps1') -OutputDirectory $appRoot -SelfContained
}
$settingsPath=Join-Path $appRoot 'data\settings.json'
$settings=if(Test-Path -LiteralPath $settingsPath){Get-Content -LiteralPath $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json}else{[pscustomobject]@{}}
if(-not $RuntimeDirectory){$RuntimeDirectory=if($settings.RuntimeDirectory){$settings.RuntimeDirectory}else{Join-Path $appRoot '.runtime'}}
if(-not $PSBoundParameters.ContainsKey('OllamaPort') -and $settings.OllamaUrl){$OllamaPort=([uri]$settings.OllamaUrl).Port}
$RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
New-Item -ItemType Directory -Force -Path $RuntimeDirectory | Out-Null
$resources=Join-Path $PSScriptRoot 'resources'
$lock=Get-Content -LiteralPath (Join-Path $resources 'models.lock.json') -Raw -Encoding utf8 | ConvertFrom-Json
if(-not $OllamaExecutable){
    $candidates=@((Join-Path $RuntimeDirectory 'ollama\ollama.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'))
    if($settings.OllamaExecutable){$candidates=@($settings.OllamaExecutable)+$candidates}
    $command=Get-Command ollama.exe -ErrorAction SilentlyContinue
    if($command){$candidates+=$command.Source}
    foreach($candidate in $candidates){if((Get-OllamaVersion $candidate) -ge [version]$lock.ollama.version){$OllamaExecutable=$candidate;break}}
}
if((Get-OllamaVersion $OllamaExecutable) -lt [version]$lock.ollama.version){
    if($PSBoundParameters.ContainsKey('OllamaExecutable')){throw 'The selected Ollama executable must be version 0.32.0 or newer.'}
    $archive=Join-Path $RuntimeDirectory 'downloads\ollama-windows-amd64.zip'
    Get-Asset $lock.ollama.url $archive $lock.ollama.size_bytes $lock.ollama.sha256
    $portable=Join-Path $RuntimeDirectory 'ollama'
    Expand-Archive -LiteralPath $archive -DestinationPath $portable -Force
    $OllamaExecutable=Join-Path $portable 'ollama.exe'
    if(-not(Test-Path -LiteralPath $OllamaExecutable)){throw 'The Ollama archive did not contain ollama.exe.'}
}
$vad=Join-Path $RuntimeDirectory 'vad\silero_vad.onnx'
Get-Asset $lock.vad.url $vad $lock.vad.size_bytes $lock.vad.sha256
$asrFiles=@{}
foreach($file in $lock.asr.files){
    $path=Join-Path $RuntimeDirectory ('asr-models\'+$file.filename)
    $url="https://huggingface.co/$($lock.asr.repository)/resolve/$($lock.asr.revision)/$($file.filename)"
    Get-Asset $url $path $file.size_bytes $file.sha256
    $asrFiles[$file.filename]="sha256:$($file.sha256)"
}
$translation=Join-Path $RuntimeDirectory ('translation-models\'+$lock.translation.filename)
$translationUrl="https://huggingface.co/$($lock.translation.repository)/resolve/$($lock.translation.revision)/$($lock.translation.filename)"
Get-Asset $translationUrl $translation $lock.translation.size_bytes $lock.translation.sha256
$oldHost=$env:OLLAMA_HOST;$oldModels=$env:OLLAMA_MODELS;$oldParallel=$env:OLLAMA_NUM_PARALLEL;$oldLoaded=$env:OLLAMA_MAX_LOADED_MODELS
$script:ollamaUrl="http://127.0.0.1:$OllamaPort"
$installerService=$null
try{
    $env:OLLAMA_HOST="127.0.0.1:$OllamaPort"
    $env:OLLAMA_MODELS=Join-Path $RuntimeDirectory 'ollama-models'
    $env:OLLAMA_NUM_PARALLEL='1';$env:OLLAMA_MAX_LOADED_MODELS='2'
    $ready=$false
    try{$version=Invoke-RestMethod "$ollamaUrl/api/version" -TimeoutSec 3;$ready=$true}catch{}
    if(-not $ready){
        $installerService=Start-Process -FilePath $OllamaExecutable -ArgumentList 'serve' -WindowStyle Hidden -RedirectStandardOutput (Join-Path $RuntimeDirectory 'ollama.out.log') -RedirectStandardError (Join-Path $RuntimeDirectory 'ollama.err.log') -PassThru
        for($i=0;$i -lt 45;$i++){
            if($installerService.HasExited){throw 'Ollama exited. Check the runtime logs.'}
            try{$version=Invoke-RestMethod "$ollamaUrl/api/version" -TimeoutSec 2;$ready=$true;break}catch{Start-Sleep -Seconds 1}
        }
    }
    if(-not $ready){throw 'Ollama did not start. Check the runtime logs.'}
    if([version]($version.version -split '-')[0] -lt [version]$lock.ollama.version){throw 'The Ollama service on this port is too old. Update it or choose another -OllamaPort.'}
    foreach($file in $lock.asr.files){Import-Blob (Join-Path $RuntimeDirectory ('asr-models\'+$file.filename)) $file.sha256}
    Write-Host 'Importing ASR model and audio encoder...'
    # File.ReadAllText avoids PowerShell 5.1 provider metadata on Get-Content strings.
    $template=[IO.File]::ReadAllText((Join-Path $resources 'Qwen3-ASR.template')).TrimEnd()
    $license=[IO.File]::ReadAllText((Join-Path $resources 'QWEN-ASR-LICENSE.txt'))
    $result=Post-Json '/api/create' @{model=$lock.asr.model;files=$asrFiles;template=$template;license=$license;parameters=@{temperature=0;num_ctx=2048;stop=@('<|im_start|>','<|im_end|>')};stream=$false}
    if($result.status -ne 'success'){throw 'ASR import did not complete.'}
    $modelfile=Join-Path (Split-Path -Parent $translation) 'Hy-MT2.Modelfile'
    Write-Host 'Importing translation model...'
    Copy-Item -LiteralPath (Join-Path $resources 'Hy-MT2.Modelfile') -Destination $modelfile -Force
    & $OllamaExecutable create $lock.translation.model -f $modelfile
    Require-Success 'Translation import failed.'
    $installed=(Invoke-RestMethod "$ollamaUrl/api/tags" -TimeoutSec 10).models
    foreach($model in @($lock.asr.model,$lock.translation.model)){
        $tag=$installed | Where-Object name -eq $model | Select-Object -First 1
        if(-not $tag){throw "Missing imported model: $model"}
        $parts=$model.Split(':')
        $manifest=Join-Path $RuntimeDirectory ("ollama-models\manifests\registry.ollama.ai\library\"+$parts[0]+'\'+$parts[1])
        $expectedDigest=([string]$tag.digest).Replace('sha256:','').ToLowerInvariant()
        if(-not(Test-Path -LiteralPath $manifest) -or (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedDigest){
            throw 'The running Ollama uses another model directory. Rerun with an unused -OllamaPort so the app can manage its own model store.'
        }
    }
}finally{
    if($installerService -and -not $installerService.HasExited){Stop-Process -Id $installerService.Id -ErrorAction SilentlyContinue}
    $env:OLLAMA_HOST=$oldHost;$env:OLLAMA_MODELS=$oldModels;$env:OLLAMA_NUM_PARALLEL=$oldParallel;$env:OLLAMA_MAX_LOADED_MODELS=$oldLoaded
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsPath) | Out-Null
foreach($name in @('AsrUrl','WslDistribution','WslRuntimeDirectory')){$settings.PSObject.Properties.Remove($name)}
foreach($entry in @{RuntimeDirectory=$RuntimeDirectory;AsrModel=$lock.asr.model;TranslationModel=$lock.translation.model;OllamaUrl=$ollamaUrl;OllamaExecutable=[IO.Path]::GetFullPath($OllamaExecutable)}.GetEnumerator()){$settings | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Set-Content -LiteralPath (Join-Path $appRoot 'runtime-path.txt') -Value $RuntimeDirectory -Encoding utf8
Write-Host "Installed. Open $appRoot\Start.cmd. Click Start and wait until ready before playing audio."
