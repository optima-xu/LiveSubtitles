param([string]$OutputDirectory, [switch]$SelfContained)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repoRoot 'dist\LiveSubtitles'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$project=Join-Path $repoRoot 'src\LiveSubtitles\LiveSubtitles.csproj'
$publishArgs=@('publish',$project,'-c','Release','-r','win-x64','-o',(Join-Path $OutputDirectory 'app'),'--self-contained',($SelfContained.IsPresent.ToString().ToLowerInvariant()),'-p:DebugType=none','-p:DebugSymbols=false')
& dotnet @publishArgs
if($LASTEXITCODE -ne 0){throw 'Windows build failed.'}
$oldSymbols=Join-Path $OutputDirectory 'app\LiveSubtitles.pdb'
if(Test-Path -LiteralPath $oldSymbols){Remove-Item -LiteralPath $oldSymbols}
New-Item -ItemType Directory -Force -Path (Join-Path $OutputDirectory 'server'),(Join-Path $OutputDirectory 'scripts') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'server') -File | Copy-Item -Destination (Join-Path $OutputDirectory 'server') -Force
foreach($name in 'Install.ps1','Start.ps1','Install.cmd','Start.cmd','README.md','TESTING.md','CONTRIBUTING.md','LICENSE','THIRD_PARTY_NOTICES.md'){
    if(Test-Path -LiteralPath (Join-Path $repoRoot $name)){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $OutputDirectory $name) -Force}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Diagnose.ps1') -Destination (Join-Path $OutputDirectory 'scripts\Diagnose.ps1') -Force
if(Test-Path -LiteralPath (Join-Path $repoRoot 'docs')){Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $OutputDirectory -Recurse -Force}
Write-Host "Built: $OutputDirectory"
