<#
.SYNOPSIS
  Build, test, publish and run Beetle. Requires the .NET 10 SDK.
.EXAMPLE
  .\build.ps1 -Run                  # build and launch
  .\build.ps1 -Test                 # build and run the test suite
  .\build.ps1 -Test -Publish        # release gate: tests, then a framework-dependent dist/win-x64/desktop
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test,
    [switch]$Publish,
    [switch]$Run,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'version.ps1')
if (-not (Show-Report $projectRoot $null)) { throw 'Version mismatch. Run version.ps1 before building.' }
# Beetle talks to Windows' Bluetooth stack and grabs the screen through GDI, so it is Windows-only.
if ($Runtime -notin @('win-x64', 'win-arm64')) { throw "Unsupported runtime: $Runtime (Beetle is Windows-only)" }
$previousTelemetry = $env:AVALONIA_TELEMETRY_OPTOUT
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Push-Location $projectRoot
try {
    Invoke-DotNet @('build', 'Beetle.slnx', '-c', $Configuration, '--nologo')
    if ($Test) { Invoke-DotNet @('test', 'Beetle.slnx', '--no-build', '-c', $Configuration, '--nologo') }
    if ($Publish) {
        $outputPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "dist/$Runtime/desktop"))
        if (-not $outputPath.StartsWith((Join-Path $projectRoot 'dist') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output must stay within dist.' }
        # Replace only this runtime's generated payload, so stale files cannot linger.
        if (Test-Path -LiteralPath $outputPath) {
            $items = @((Get-Item -LiteralPath $outputPath)) + @(Get-ChildItem -LiteralPath $outputPath -Recurse -Force)
            if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Refusing to clean linked publish output: $outputPath" }
            Remove-Item -LiteralPath $outputPath -Recurse -Force
        }
        # Framework-dependent: Beetle ships only its own files and runs on the installed .NET 10
        # runtime. The apphost (Beetle.exe) shows Windows' standard ".NET is required" prompt, with a
        # download link, on a machine without it.
        Invoke-DotNet @('publish', 'Beetle.Desktop/Beetle.Desktop.csproj', '-c', $Configuration, '-r', $Runtime, '--self-contained', 'false', '-p:PublishSingleFile=false', '-p:DebugType=None', '--nologo', '-o', "dist/$Runtime/desktop")
        Get-ChildItem -LiteralPath $outputPath -File -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    }
    if ($Run) { Invoke-DotNet @('run', '--project', 'Beetle.Desktop', '-c', $Configuration, '--no-build') }
}
finally { Pop-Location; $env:AVALONIA_TELEMETRY_OPTOUT = $previousTelemetry }
