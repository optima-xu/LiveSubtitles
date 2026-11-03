param([Parameter(Mandatory=$true)][string]$WaveFile,[int]$Repeats=1,[string]$AsrUrl='ws://127.0.0.1:17861/asr',[string]$OllamaUrl='http://127.0.0.1:11436')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$wave=[IO.Path]::GetFullPath($WaveFile)
if(-not(Test-Path -LiteralPath $wave)){throw 'Wave file not found.'}
# Both services must already be running. This probe sends a supplied file in real time; it does not play it.
& dotnet run --project (Join-Path $root 'tests\IntegrationProbe') -c Release -- $wave $Repeats $AsrUrl $OllamaUrl
if($LASTEXITCODE -ne 0){throw 'Live ASR/translation smoke test failed. Inspect the printed events.'}
