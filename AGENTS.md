# Beetle working and release flows

## Scope and defaults

This repository is Beetle by Fezcode, a Windows app that controls an ELK-BLEDOM
Bluetooth LE RGB LED strip, built on C# / .NET 10 / Avalonia 12. A shared
`Beetle.Core` library holds the colour maths, the strip's command frames, the
WinRT Bluetooth transport, the GDI screen sampler and the controllers;
`Beetle.Desktop` is the GUI; `Beetle.Tests` covers the pure logic. There is no
CLI. Beetle is Windows-only: Bluetooth and the screen grab use Windows APIs.

Preserve existing changes when working in a dirty checkout. Build scripts live at
the repository root; Forge is the sibling `../Forge` project. Airlift and clockt
are reference implementations of these flows, not Beetle's release target.

## Commit messages

Use a normal commit title and body only. Do not add `Co-Authored-By` trailers or
AI/assistant attribution (including Claude or Codex) to commits or release notes.
For multiline messages or messages containing quotes, write a temporary UTF-8
message file and use `git commit -F <file>`. Check the exit code and verify the
resulting commit before tagging; PowerShell 5.1 can split inline quoted messages.

## Version sources of truth

`Directory.Build.props` `<Version>` is the single source of truth. It stamps every
assembly Beetle ships, and `Beetle.Core.AppVersion` reads that stamp at runtime,
so both `--version`/`-v` outputs and the in-app footer follow it. Never hardcode a
version in C#; use `AppVersion.Current` or `AppVersion.Display` (`Beetle vx.y.z`).

Two build inputs cannot read an assembly, so `version.ps1` synchronizes them:
the `forge.toml` `[app] version` and the installer filename in `README.md`.

```powershell
.\version.ps1                  # report every location + consistency
.\version.ps1 -Bump patch      # 0.1.0 -> 0.1.1 everywhere, then verify
.\version.ps1 -Set 0.2.0       # set an exact version, then verify
.\version.ps1 -Bump minor -DryRun
```

The script exits non-zero on any mismatch or missing location, so it is safe to
gate a release on. Edit versions through it rather than by hand, and read its
report rather than trusting the edit.

## RELEASE workflow

Only an explicit request to **RELEASE** triggers the complete publishing flow.
Ordinary fixes, builds, and installer requests do not imply a version bump,
commit, push, tag, or GitHub release. When RELEASE is requested, perform these
steps in order and stop/report any failure before proceeding:

1. Run `./version.ps1 -Bump patch` by default, or `-Set x.y.z` for a requested
   version. Clarify conflicting/ambiguous version instructions. Run
   `./version.ps1` to verify.
2. Run `./build.ps1 -Test -Publish` to build, test, and publish the framework-dependent
   desktop app into `dist/win-x64/desktop`. Do not skip tests for a release.
   Replacing this output closes any running Beetle window using it; report that
   effect and respect authorization already given.
3. Run `./build-installer.ps1 -SkipBuild` immediately afterward. Forge requires
   the sibling `../Forge/build/forge.exe`, produced by `gobake build` in Forge.
   Verify `dist/installer/Beetle-Setup-<version>.exe` exists, matches the new
   version, and surface that exact path for testing. During RELEASE, launch this
   new Setup executable for the user to install/test; an existing installed copy
   stays old until Setup is run. Keep the wizard's "Open Beetle" finish option
   enabled. Honor any requested test gate.
4. Review and commit the intended changes without attribution, using a message
   file. Verify the commit landed and record its hash before the next steps.
5. Push the commit to Beetle's configured remote/release branch (normally
   `origin main`). Inspect `git remote -v` and the branch first. This working copy
   publishes to `https://github.com/fezcode/Beetle` — if `.git` or a remote is
   missing, confirm the destination before publishing. Never force-push.
6. Create the matching `vX.Y.Z` tag on the verified commit, push it, and create a
   GitHub release with `gh release create vX.Y.Z`, attaching only the matching
   `dist/installer/Beetle-Setup-X.Y.Z.exe` installer asset. Use title
   `Beetle vX.Y.Z`; notes start with `## Beetle vX.Y.Z`, followed by
   `### <feature>` sections and bullets. Supply notes through `--notes-file`
   to avoid PowerShell multiline argument splitting. Verify the published asset.

Airlift lists Beetle from this repository's public GitHub releases and resolves
the Windows asset as `Beetle-Setup-<version>.exe`; keep that filename.

## Build and installer maintenance

- Distributable builds live under `dist/<runtime>/desktop`; Windows installers
  live in `dist/installer` as `Beetle-Setup-<version>.exe`, matching the other
  Fezcode apps. Keep Forge, build scripts and the README consistent with this
  layout. Never clean the whole `dist` directory or delete previous installers.
- Keep `forge.toml` on the `huh` wizard theme, using Beetle's icon and identity.
  `scripts/verify-installer.ps1` rejects any other theme.
- Keep the six wizard steps in order: welcome, license, folder, shortcuts,
  install, finish. Use `LICENSE.txt` for the MIT agreement, offer optional
  Desktop and Start Menu shortcuts, and keep Open Beetle selected on Finish.
- Preserve the stable Forge ID `com.fezcode.beetle`; Forge detects installations
  through its automatically generated Windows uninstall record. Also maintain
  HKCU `Software\fezcode\Beetle` InstallDir and Version values, using
  `${app.version}` for the version, and publish `beetle.ico` for shortcuts.
- Packaging verifies the embedded manifest (`scripts/verify-installer.ps1`), not
  only the source TOML or EXE timestamp. A successful diagnostic is not a
  substitute for checking the visible wizard.
- Publish **framework-dependent**, not self-contained: `--self-contained false`
  with `-r win-x64` and `PublishSingleFile=false`. Beetle ships only its own files
  and runs on the installed .NET 10 runtime (`Microsoft.NETCore.App`); the apphost
  shows Windows' ".NET is required" prompt when it is missing. Packaging fails if
  the publish bundles a runtime (`coreclr.dll`). DLLs, native libraries,
  `.deps.json` and `.runtimeconfig.json` must remain alongside the executable;
  never distribute only the apphost EXE.
- Every build configuration embeds `Assets/beetle.ico` through `ApplicationIcon`.
  A `bin/Debug` exe built before that setting existed has no icon; rebuild rather
  than trusting a stale output.
- Preserve user data under `%LOCALAPPDATA%/Fezcode/Beetle` unless the user
  chooses the installer's option to remove settings/data.
- Fail on inconsistent versions, failed tests/publishing, missing payload, or
  non-GUI Setup executables. Never report an old installer as a new success.
- There is no CI. Beetle is built and released from this machine, so
  `./build.ps1 -Test -Publish` is the only gate before a release.

These flows are adapted from Airlift's and clockt's `AGENTS.md`.

## Beetle specifics

- The strip's command frames live in `Beetle.Core/LedCommands.cs` and are the
  single source of truth; they come from community reverse-engineering of the
  ELK-BLEDOM, not vendor documentation.
- An ELK-BLEDOM stops advertising while anything holds a connection, and it does
  not pair with Windows, so discovery relies on its live advertisement. Every
  connection must release the `GattDeviceService` as well as the
  `BluetoothLEDevice` on disconnect, or Windows keeps the link open and the strip
  disappears from scans.
- Ambient mode samples a `SampleRegion` of the primary monitor, expressed as
  fractions so it survives resolution and DPI changes. Colour boost applies only
  to ambient samples; manual colours are sent exactly as picked.
- `ColorAnalysis` reduces the sampled pixels by `AmbientColorMode`: `Average`, or
  `Selective`, which keeps only pixels with saturation >= 0.25 and value >= 0.15
  (dropping black, white and greys). Selective returns null when nothing coloured
  is left, and `AmbientController` then holds its current colour instead of easing
  toward black. The 128x72 sample size exists so small coloured elements survive
  the halftone shrink; do not lower it without re-checking selective mode.
- On-screen sampler tests are only meaningful when nothing fullscreen (a game)
  covers the staged pattern; capture what was actually on screen before trusting
  a result.
- Beetle is a tray app. The desktop lifetime uses `ShutdownMode.OnExplicitShutdown`;
  closing the window hides it and keeps the strip and ambient mode running. Only the
  tray's Quit, or an OS/application shutdown, runs the real shutdown (stop ambient,
  save settings, release the strip). A named mutex keeps one instance per session,
  because the strip accepts a single Bluetooth connection; a second launch signals the
  running copy to show its window and exits.
- Colour belongs to a palette role, never a literal. `Beetle.Desktop/Theme.cs`
  holds one `Palette` per theme; XAML reads `{DynamicResource Beetle.<Role>}` and
  C# reads `Themes.Brush(p => p.<Role>)`. Every role is `required`, so an
  incomplete palette will not compile. Colour built in code is resolved once, so
  the window is rebuilt when the palette changes.
- `Beetle.Desktop/Assets/beetle.svg` is the single source for the mark: the app
  draws it, `scripts/create-app-icon.ps1` renders `beetle.png` and the multi-size
  `beetle.ico` from it, and `scripts/create-banner.ps1` renders the README's
  `banner-image.png`. Both share `scripts/mark.ps1`. Re-run them after editing the SVG.
- README screenshots live in `docs/screenshots`. Capture only Beetle's own window
  (`PrintWindow`), never the desktop, and keep personal information out of them.
