param([Parameter(Mandatory=$true)][string]$WaveFile,[Parameter(Mandatory=$true)][string]$SettingsFile,[int]$Repeats=1)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$wave=[IO.Path]::GetFullPath($WaveFile)
$settings=[IO.Path]::GetFullPath($SettingsFile)
if(-not(Test-Path -LiteralPath $wave)){throw 'Wave file not found.'}
if(-not(Test-Path -LiteralPath $settings)){throw 'Settings file not found. Run Install.cmd first.'}
# Starts/reuses one Ollama instance and supplies audio at playback speed without playing it.
& dotnet run --project (Join-Path $root 'tests\IntegrationProbe') -c Release -- --managed $root $settings $wave $Repeats
if($LASTEXITCODE -ne 0){throw 'ASR/translation test failed. Inspect the printed events.'}
