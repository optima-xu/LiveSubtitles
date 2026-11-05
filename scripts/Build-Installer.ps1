param([string]$PayloadDirectory, [string]$OutputDirectory, [string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PayloadDirectory) {
    $PayloadDirectory = Join-Path $repoRoot 'dist\LiveSubtitles'
    & (Join-Path $PSScriptRoot 'Build.ps1') -SelfContained -OutputDirectory $PayloadDirectory
}
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
foreach ($name in @('app\LiveSubtitles.exe', 'app\coreclr.dll', 'Install.ps1', 'resources\models.lock.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) {
        throw "Incomplete self-contained Windows package: missing $name"
    }
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'dist' }
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
if (-not $CompilerPath) {
    # Use the upstream compiler in portable mode. All build tools stay under dist.
    $toolRoot = Join-Path $repoRoot 'dist\build-tools'
    $toolDir = Join-Path $toolRoot 'InnoSetup-7.1.1'
    $CompilerPath = Join-Path $toolDir 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
        [void][IO.Directory]::CreateDirectory($toolRoot)
        $download = Join-Path $toolRoot 'innosetup-7.1.1-x64.exe'
        $hash = '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
        if (-not (Test-Path -LiteralPath $download) -or (Get-FileHash -LiteralPath $download).Hash -ne $hash) {
            Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $download
        }
        if ((Get-FileHash -LiteralPath $download).Hash -ne $hash) { throw 'Inno Setup download failed SHA256 verification.' }
        $options = '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /PORTABLE=1 /NOICONS /DIR="' + $toolDir + '"'
        $process = Start-Process -FilePath $download -ArgumentList $options -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Inno Setup compiler setup failed ($($process.ExitCode))." }
    }
}
$project = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src\LiveSubtitles\LiveSubtitles.csproj') -Raw)
$version = $project.Project.PropertyGroup.Version
$installerName = "LiveSubtitles-Setup-v$version-windows-x64.exe"
& $CompilerPath '/Qp' "/DAppVersion=$version" "/DPayloadDir=$payload" "/O$output" (Join-Path $repoRoot 'installer\LiveSubtitles.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
$installer = Get-Item -LiteralPath (Join-Path $output $installerName)
Write-Host "Built installer: $($installer.FullName)"
