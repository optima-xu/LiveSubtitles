$ErrorActionPreference='Stop'
$app=Join-Path $PSScriptRoot 'app\LiveSubtitles.exe'
if(-not(Test-Path -LiteralPath $app)){$app=Join-Path $PSScriptRoot 'dist\LiveSubtitles\app\LiveSubtitles.exe'}
if(-not(Test-Path -LiteralPath $app)){throw 'Run Install.ps1 or scripts/Build.ps1 first.'}
# This is the visible application the user requested, not a background helper.
Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app)
