param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Version)
# Reads the manifest embedded in a Forge Setup without running it, and checks it against what a
# Beetle release must contain. Packaging calls this; it fails loudly rather than shipping a bad installer.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$bytes = [IO.File]::ReadAllBytes($Path)
$trailer = $bytes.Length - 72
if ($trailer -lt 64 -or [Text.Encoding]::ASCII.GetString($bytes, $trailer, 5) -ne 'FORGE') { throw 'Missing Forge trailer.' }
$offset = [BitConverter]::ToInt64($bytes, $trailer + 16)
$length = [BitConverter]::ToInt64($bytes, $trailer + 24)
if ($offset -lt 64 -or $length -le 0 -or $offset + $length -ne $trailer) { throw 'Invalid Forge payload bounds.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = $sha.ComputeHash($bytes, $offset, $length) } finally { $sha.Dispose() }
if ([BitConverter]::ToString($hash) -ne [BitConverter]::ToString($bytes, $trailer + 32, 32)) { throw 'Forge checksum mismatch.' }
$stream = [IO.MemoryStream]::new($bytes, [int]$offset, [int]$length, $false)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
try {
    function Read-Entry([string]$Name) {
        $entry = $zip.GetEntry($Name)
        if ($null -eq $entry) { throw "Installer is missing $Name" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $manifest = (Read-Entry 'manifest.json') | ConvertFrom-Json
    if ($manifest.app.id -ne 'com.fezcode.beetle' -or $manifest.app.version -ne $Version) { throw 'Incorrect installer identity/version.' }
    if ($manifest.ui.theme -ne 'huh') { throw 'Installer must use the huh theme.' }
    if (@($manifest.files.dst) -contains '${INSTALLDIR}/coreclr.dll') { throw 'Installer bundles a .NET runtime; Beetle must be framework-dependent.' }
    if (($manifest.steps.type -join ',') -ne 'welcome,license,folder,shortcuts,install,finish') { throw 'Installer must contain all six wizard steps in order.' }
    if ((Read-Entry $manifest.steps[1].file) -notmatch 'MIT License') { throw 'MIT license agreement is missing.' }
    if (@($manifest.shortcuts).Count -ne 2) { throw 'Expected Desktop and Start Menu choices.' }
    foreach ($shortcut in $manifest.shortcuts) {
        if (-not $shortcut.optional -or -not $shortcut.default -or $shortcut.target -ne '${INSTALLDIR}/Beetle.exe' -or $shortcut.icon -ne '${INSTALLDIR}/beetle.ico') { throw 'Incorrect shortcut configuration.' }
    }
    if (($manifest.shortcuts.location -join '|') -ne '${DESKTOP}/Beetle.lnk|${STARTMENU}/Beetle.lnk') { throw 'Unexpected shortcut destinations.' }
    foreach ($name in @('InstallDir', 'Version')) {
        $entry = @($manifest.registry | Where-Object { $_.value -eq $name -and $_.hive -eq 'HKCU' -and $_.key -eq 'Software\fezcode\Beetle' })
        $expected = if ($name -eq 'Version') { '${app.version}' } else { '${INSTALLDIR}' }
        if ($entry.Count -ne 1 -or $entry[0].data -ne $expected) { throw "Missing vendor registration: $name" }
    }
    if ($manifest.uninstall.settings_dirs -notcontains '${LOCALAPPDATA}/Fezcode/Beetle') { throw 'User data preservation is not configured.' }
    $launch = $manifest.steps[5].launches
    if (@($launch).Count -ne 1 -or -not $launch[0].checked -or $launch[0].target -ne '${INSTALLDIR}/Beetle.exe') { throw 'Open Beetle must be selected on Finish.' }
    $destinations = @($manifest.files.dst)
    foreach ($file in @('Beetle.exe', 'Beetle.dll', 'Beetle.Core.dll', 'beetle.ico', 'LICENSE.txt')) {
        if ($destinations -notcontains ('${INSTALLDIR}/' + $file)) { throw "Payload is missing installed file $file" }
    }
    Write-Host "Verified Beetle ${Version}: bundle checksum, six steps, MIT license, shortcuts, registry, folder payload and finish launch."
} finally { $zip.Dispose(); $stream.Dispose() }
