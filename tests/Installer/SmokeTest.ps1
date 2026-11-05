param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [string]$SeedRuntimeDirectory,
    [string]$ScratchDirectory
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\optima-xu.LiveSubtitles_is1'
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'LiveSubtitles.lnk'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'LiveSubtitles.lnk'
foreach ($existing in @($registration, $desktop, $startMenu)) {
    if (Test-Path -LiteralPath $existing) { throw "Use a clean Windows profile for this test. Already exists: $existing" }
}
if (-not $ScratchDirectory) {
    $ScratchDirectory = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
}
$scratch = (Resolve-Path -LiteralPath $ScratchDirectory).Path.TrimEnd('\')
$runName = 'LiveSubtitles-smoke-' + [Guid]::NewGuid().ToString('N')
$testRoot = [IO.Directory]::CreateDirectory((Join-Path $scratch $runName)).FullName
$destination = Join-Path $testRoot '自定义 目录 🧪 [x64]\LiveSubtitles'
$target = Join-Path $destination 'app\LiveSubtitles.exe'
$settingsFile = Join-Path $destination 'data\settings.json'
$runtime = Join-Path $destination '.runtime'
$success = $false
$busyPort = $null

function Invoke-SetupProcess([string]$Path, [string]$Options, [string]$LogName, [int]$ExpectedExitCode = 0) {
    $log = Join-Path $testRoot $LogName
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + $log + '" ' + $Options
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne $ExpectedExitCode -and (Test-Path -LiteralPath $log)) {
        Get-Content -LiteralPath $log -Tail 40 | ForEach-Object { Write-Host $_ }
    }
    return $process.ExitCode
}

function Test-Shortcut([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing shortcut: $Path" }
    $folder = (New-Object -ComObject Shell.Application).NameSpace((Split-Path -Parent $Path))
    $link = $folder.ParseName((Split-Path -Leaf $Path)).GetLink
    if ($link.Path -ne $target -or $link.WorkingDirectory -ne (Split-Path -Parent $target)) {
        throw "Incorrect shortcut $Path. Expected $target; found '$($link.Path)' in '$($link.WorkingDirectory)'."
    }
}

function Uninstall-TestCopy {
    $record = Get-ItemProperty -LiteralPath $registration
    if ($record.InstallLocation.TrimEnd('\') -ne $destination) { throw 'Unexpected registered install location.' }
    $uninstaller = $record.UninstallString.Trim('"')
    if ((Split-Path -Parent $uninstaller) -ne $destination -or (Split-Path -Leaf $uninstaller) -notmatch '^unins\d+\.exe$') {
        throw 'Refusing to run an uninstaller outside the test installation.'
    }
    if ((Invoke-SetupProcess $uninstaller '' 'uninstall.log') -ne 0) { throw 'Uninstall failed.' }
}

try {
    # A failed preparation must not leave app shortcuts or an installed-app record.
    $invalid = Join-Path $testRoot 'incomplete'
    [void][IO.Directory]::CreateDirectory((Join-Path $invalid 'data'))
    [IO.File]::WriteAllText((Join-Path $invalid 'data\settings.json'), '{invalid')
    if ((Invoke-SetupProcess $installer ('/DIR="' + $invalid + '"') 'incomplete.log' 7) -ne 7) {
        throw 'Setup did not return the expected model preparation failure.'
    }
    foreach ($path in @($registration, $desktop, $startMenu, (Join-Path $invalid 'app\LiveSubtitles.exe'))) {
        if (Test-Path -LiteralPath $path) { throw "Incomplete setup registered an application: $path" }
    }
    if ($SeedRuntimeDirectory) {
        # Only copy download inputs. The real installer verifies and imports them into the new store.
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $lock = Get-Content -LiteralPath (Join-Path $repoRoot 'resources\models.lock.json') -Raw | ConvertFrom-Json
        $inputs = @('vad\silero_vad.onnx', ('translation-models\' + $lock.translation.filename))
        $inputs += @($lock.asr.files | ForEach-Object { 'asr-models\' + $_.filename })
        foreach ($relative in $inputs) {
            $file = Join-Path $runtime $relative
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $file))
            [IO.File]::Copy((Join-Path $SeedRuntimeDirectory $relative), $file)
        }
    }
    $dirOption = '/DIR="' + $destination + '"'
    # Simulate an earlier portable installation or another process using the default port.
    $busyPort = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 11436)
    try { $busyPort.Start() } catch [Net.Sockets.SocketException] { $busyPort.Stop(); $busyPort = $null }
    if ((Invoke-SetupProcess $installer $dirOption 'install.log') -ne 0) { throw 'Installation failed.' }
    $record = Get-ItemProperty -LiteralPath $registration
    if ($record.DisplayName -ne 'LiveSubtitles' -or $record.InstallLocation.TrimEnd('\') -ne $destination) {
        throw 'Installed apps entry does not identify the selected installation.'
    }
    Test-Shortcut $startMenu
    Test-Shortcut $desktop
    $settings = Get-Content -LiteralPath $settingsFile -Raw -Encoding utf8 | ConvertFrom-Json
    if (([uri]$settings.OllamaUrl).Port -eq 11436) { throw 'Setup reused an occupied port.' }
    if ($settings.RuntimeDirectory -ne $runtime -or -not (Test-Path -LiteralPath (Join-Path $runtime 'vad\silero_vad.onnx'))) {
        throw 'Models were not installed under the chosen folder.'
    }
    $app = Start-Process -FilePath $desktop -PassThru -WindowStyle Hidden
    try {
        if ($app.Path -ne $target -or -not $app.WaitForInputIdle(30000) -or $app.HasExited) {
            throw 'The desktop shortcut did not open the installed application.'
        }
    } finally {
        if (-not $app.HasExited -and $app.Path -eq $target) {
            [void]$app.CloseMainWindow()
            if (-not $app.WaitForExit(3000)) { $app.Kill(); $app.WaitForExit() }
        }
    }
    Write-Host 'Custom folder, model setup, Installed apps entry and shortcut launch passed.'

    $settings | Add-Member -NotePropertyName Glossary -NotePropertyValue 'subtitle test marker' -Force
    [IO.File]::WriteAllText($settingsFile, ($settings | ConvertTo-Json -Depth 5))
    if ((Invoke-SetupProcess $installer ($dirOption + ' /MERGETASKS=!desktopicon') 'upgrade.log') -ne 0) {
        throw 'Repeat installation failed.'
    }
    Test-Shortcut $startMenu
    if (Test-Path -LiteralPath $desktop) { throw 'The desktop shortcut option was not respected.' }
    if ((Get-Content -LiteralPath $settingsFile -Raw -Encoding utf8 | ConvertFrom-Json).Glossary -ne 'subtitle test marker') {
        throw 'Repeat installation lost the existing preferences.'
    }
    Uninstall-TestCopy
    foreach ($path in @($registration, $desktop, $startMenu, $target)) {
        if (Test-Path -LiteralPath $path) { throw "Uninstall did not remove $path" }
    }
    if (-not (Test-Path -LiteralPath $settingsFile) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'vad\silero_vad.onnx'))) {
        throw 'Uninstall did not preserve the reusable data.'
    }
    Write-Host 'Repeat installation, shortcut opt-out, preference preservation and uninstall passed.'
    $success = $true
} finally {
    if ($busyPort) { $busyPort.Stop() }
    if (Test-Path -LiteralPath $registration) {
        $record = Get-ItemProperty -LiteralPath $registration
        if ($record.InstallLocation.TrimEnd('\') -eq $destination) { Uninstall-TestCopy }
    }
    $owned = Get-Item -LiteralPath $testRoot
    if ($success -and $owned.Parent.FullName.TrimEnd('\') -eq $scratch -and $owned.Name -eq $runName -and
        -not ($owned.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $owned.FullName -Recurse -Force
    } else { Write-Host "Test files and installer logs: $testRoot" }
}
