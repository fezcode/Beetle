param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Version,
    [string]$Forge = (Join-Path $PSScriptRoot '../../Forge/build/forge.exe')
)
# Reads the manifest embedded in a Forge Setup without running it, and checks it against what a
# Beetle release must contain. Packaging calls this; it fails loudly rather than shipping a bad installer.
#
# Two bundle formats are understood:
#   Forge 1: [stub][zip payload][72-byte trailer "FORGE"...] - the manifest is read straight from the zip.
#   Forge 2: [stub][zstd entries + index][72-byte trailer "FRGTRL2A"...sha256..."FRGTRL2B"]. The checksum
#            is verified here; entries are zstd-compressed, so the manifest and payload listing come from
#            `forge inspect`, and the license is checked by size against the repository's LICENSE.txt.
$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes($Path)
$trailer = $bytes.Length - 72
if ($trailer -lt 64) { throw 'Setup is too small to carry a Forge payload.' }

function Test-Checksum([long]$Offset, [long]$Length) {
    if ($Offset -lt 64 -or $Length -le 0 -or $Offset + $Length -ne $trailer) { throw 'Invalid Forge payload bounds.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes, $Offset, $Length) } finally { $sha.Dispose() }
    if ([BitConverter]::ToString($hash) -ne [BitConverter]::ToString($bytes, $trailer + 32, 32)) { throw 'Forge checksum mismatch.' }
}

$offset = [BitConverter]::ToInt64($bytes, $trailer + 16)
$length = [BitConverter]::ToInt64($bytes, $trailer + 24)
$format = $null
if ([Text.Encoding]::ASCII.GetString($bytes, $trailer, 5) -eq 'FORGE') { $format = 1 }
elseif ([Text.Encoding]::ASCII.GetString($bytes, $trailer, 8) -eq 'FRGTRL2A' -and [Text.Encoding]::ASCII.GetString($bytes, $trailer + 64, 8) -eq 'FRGTRL2B') {
    if ([BitConverter]::ToUInt32($bytes, $trailer + 8) -ne 2) { throw 'Unsupported Forge 2 trailer version.' }
    $format = 2
}
else { throw 'Missing Forge trailer.' }
Test-Checksum $offset $length

# Each format yields: the manifest, the license text (or null when only its size can be checked), and the
# set of installed destinations ('${INSTALLDIR}/Beetle.exe', ...).
$licenseText = $null
if ($format -eq 1) {
    Add-Type -AssemblyName System.IO.Compression
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
        $licenseText = Read-Entry $manifest.steps[1].file
    } finally { $zip.Dispose(); $stream.Dispose() }
    $destinations = @($manifest.files.dst)
}
else {
    $forgeExecutable = [IO.Path]::GetFullPath($Forge)
    if (-not (Test-Path -LiteralPath $forgeExecutable)) { throw "Forge not found (needed to read a Forge 2 manifest): $forgeExecutable" }
    $lines = @(& $forgeExecutable inspect $Path)
    if ($LASTEXITCODE -ne 0) { throw "forge inspect failed with exit code $LASTEXITCODE" }
    $split = [array]::FindIndex([string[]]$lines, [Predicate[string]] { param($line) $line -match '^payload:' })
    if ($split -lt 1) { throw 'forge inspect printed no payload listing.' }
    $manifest = ($lines[0..($split - 1)] -join "`n") | ConvertFrom-Json
    # Listing rows: "<size>  <entry path>".
    $entries = @{}
    foreach ($line in $lines[($split + 1)..($lines.Count - 1)]) {
        if ($line -match '^\s*(\d+)\s+(\S.*?)\s*$') { $entries[$Matches[2]] = [long]$Matches[1] }
    }
    if ($entries.Count -eq 0) { throw 'Installer payload is empty.' }
    $licenseFile = $manifest.steps[1].file
    if (-not $entries.ContainsKey($licenseFile)) { throw "Installer is missing $licenseFile" }
    $repoLicense = Join-Path $PSScriptRoot "../$licenseFile"
    if ($entries[$licenseFile] -ne (Get-Item -LiteralPath $repoLicense).Length) { throw "Bundled $licenseFile differs in size from the repository's." }
    $licenseText = Get-Content -LiteralPath $repoLicense -Raw
    # Installed files: explicit [[files]] plus every payload entry under a [[dirs]] source.
    $destinations = @($manifest.files | Where-Object { $_ } | ForEach-Object { $_.dst })
    foreach ($dir in @($manifest.dirs | Where-Object { $_ })) {
        $prefix = $dir.src.TrimEnd('/') + '/'
        foreach ($entry in $entries.Keys) {
            if ($entry.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $destinations += $dir.dst.TrimEnd('/') + '/' + $entry.Substring($prefix.Length) }
        }
    }
}

if ($manifest.app.id -ne 'com.fezcode.beetle' -or $manifest.app.version -ne $Version) { throw 'Incorrect installer identity/version.' }
if ($manifest.ui.theme -ne 'huh') { throw 'Installer must use the huh theme.' }
if ($destinations -contains '${INSTALLDIR}/coreclr.dll') { throw 'Installer bundles a .NET runtime; Beetle must be framework-dependent.' }
if (($manifest.steps.type -join ',') -ne 'welcome,license,folder,shortcuts,install,finish') { throw 'Installer must contain all six wizard steps in order.' }
if ($licenseText -notmatch 'MIT License') { throw 'MIT license agreement is missing.' }
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
foreach ($file in @('Beetle.exe', 'Beetle.dll', 'Beetle.Core.dll', 'Beetle.deps.json', 'Beetle.runtimeconfig.json', 'beetle.ico', 'LICENSE.txt')) {
    if ($destinations -notcontains ('${INSTALLDIR}/' + $file)) { throw "Payload is missing installed file $file" }
}
Write-Host "Verified Beetle ${Version} (Forge $format bundle): checksum, six steps, MIT license, shortcuts, registry, folder payload and finish launch."
