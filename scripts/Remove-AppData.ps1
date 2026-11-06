# Invoked only when the uninstaller's user opts to remove local data.
$ErrorActionPreference = 'Stop'
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
$root = [IO.Directory]::GetParent($PSScriptRoot).FullName
$lock = Get-Content -LiteralPath (Join-Path $root 'resources\models.lock.json') -Raw -Encoding utf8 | ConvertFrom-Json
$dirs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Get-ManagedPath([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)|:') { throw "Invalid relative path: $Relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $root $Relative))
    if (-not $path.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside the installation.' }
    # Check ancestors before reading or deleting anything below them. Never follow a junction.
    $part = $path
    while ($part) {
        if ((Test-Path -LiteralPath $part) -and ([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint)) { return }
        $part = [IO.Path]::GetDirectoryName($part)
    }
    return $path
}

function Remove-ManagedFile([string]$Relative) {
    $file = Get-ManagedPath $Relative
    if (-not $file) { return }
    if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
    $parent = [IO.Path]::GetDirectoryName($file)
    while ($parent -and $parent -ne $root) {
        [void]$dirs.Add($parent)
        $parent = [IO.Path]::GetDirectoryName($parent)
    }
}

function Read-Digests([string]$Manifest) {
    $manifestData = [IO.File]::ReadAllText($Manifest) | ConvertFrom-Json
    foreach ($part in @($manifestData.config) + @($manifestData.layers)) {
        if ($part.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw "Invalid model manifest: $Manifest" }
        $Matches[1]
    }
}

# Delete only these models' manifests and blobs. Blobs used by another local model stay.
$store = '.runtime\ollama-models'
$digests = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$ownManifests = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($model in @($lock.asr.model, $lock.translation.model)) {
    $name, $tag = $model.Split(':')
    $relative = "$store\manifests\registry.ollama.ai\library\$name\$tag"
    $path = Get-ManagedPath $relative
    if ($path -and [IO.File]::Exists($path)) {
        foreach ($digest in (Read-Digests $path)) { [void]$digests.Add($digest) }
        [void]$ownManifests.Add($path)
    }
}
foreach ($file in @($lock.asr.files) + @($lock.translation)) { [void]$digests.Add($file.sha256) }
# Ollama 0.32 first creates a stop-parameters layer, then merges temperature and
# context parameters into another layer. The first layer is no longer in the manifest.
# This exact digest identifies that intermediate layer from our ASR import.
[void]$digests.Add('f02dd72bb2423204352eabc5637b44d79d17f109fdb510a7c51455892aa2d216')
$manifests = Get-ManagedPath "$store\manifests"
if ($manifests -and [IO.Directory]::Exists($manifests)) {
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($manifests)
    while ($pending.Count) {
        foreach ($item in [IO.DirectoryInfo]::new($pending.Pop()).EnumerateFileSystemInfos()) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $digests.Clear(); continue }
            if ($item -is [IO.DirectoryInfo]) { $pending.Push($item.FullName) }
            elseif (-not $ownManifests.Contains($item.FullName)) {
                try { foreach ($digest in (Read-Digests $item.FullName)) { [void]$digests.Remove($digest) } }
                catch { $digests.Clear() }
            }
        }
    }
}
foreach ($path in $ownManifests) { Remove-ManagedFile $path.Substring($root.Length + 1) }
foreach ($digest in $digests) { Remove-ManagedFile "$store\blobs\sha256-$digest" }

# Only paths recorded in the official portable archive belong to this copy of Ollama.
$archiveName = '.runtime\downloads\ollama-windows-amd64.zip'
$archive = Get-ManagedPath $archiveName
$inventoryName = '.runtime\downloads\ollama-files.txt'
$inventory = Get-ManagedPath $inventoryName
if ($inventory -and [IO.File]::Exists($inventory)) {
    foreach ($entry in [IO.File]::ReadAllLines($inventory)) { Remove-ManagedFile ('.runtime\ollama\' + $entry) }
} elseif ($archive -and [IO.File]::Exists($archive) -and (Get-FileHash -LiteralPath $archive).Hash -eq $lock.ollama.sha256) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try { foreach ($entry in $zip.Entries) { if ($entry.Name) { Remove-ManagedFile ('.runtime\ollama\' + $entry.FullName) } } }
    finally { $zip.Dispose() }
}
foreach ($file in $lock.asr.files) {
    Remove-ManagedFile ('.runtime\asr-models\' + $file.filename)
    Remove-ManagedFile ('.runtime\asr-models\' + $file.filename + '.partial')
}
foreach ($relative in @(('.runtime\translation-models\' + $lock.translation.filename), '.runtime\vad\silero_vad.onnx', $archiveName)) {
    Remove-ManagedFile $relative
    Remove-ManagedFile ($relative + '.partial')
}
foreach ($relative in @($inventoryName,'.runtime\downloads\extracting-ollama','.runtime\translation-models\Hy-MT2.Modelfile',
    '.runtime\ollama.err.log','.runtime\ollama.out.log','data\settings.json','data\settings.json.installing',
    'data\diagnostics.log','data\diagnostics.log.previous','runtime-path.txt')) { Remove-ManagedFile $relative }
foreach ($dir in ($dirs | Sort-Object Length -Descending)) {
    if ([IO.Directory]::Exists($dir) -and -not ([IO.Directory]::EnumerateFileSystemEntries($dir) | Select-Object -First 1)) {
        [IO.Directory]::Delete($dir, $false)
    }
}
