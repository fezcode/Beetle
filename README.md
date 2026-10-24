![Beetle - control an ELK-BLEDOM LED strip from your PC](./banner-image.png)

# Beetle

Control an ELK-BLEDOM Bluetooth LED strip from Windows. Turn it on and off, pick a colour,
set the brightness, and run an ambient mode that keeps the strip in tune with your screen.

Built with C# / .NET 10 / Avalonia 12, in the same code-first, dark-and-accented style as the
other Fezcode apps. No cloud, no account, no background service: just the app and your strip.

<p align="center">
  <img src="./docs/screenshots/main.png" alt="Beetle connected to a strip, with power and the colour picker" width="360">
  &nbsp;
  <img src="./docs/screenshots/ambient.png" alt="Beetle's ambient mode with sample area and colour boost" width="360">
</p>

## Features

- **Connect** to an ELK-BLEDOM strip over Bluetooth LE. The strip does not pair with Windows;
  Beetle finds it from its live advertisement and reconnects to it on launch.
- **Power** the strip on and off.
- **Colour**: a saturation/value square, a hue strip, a hex field and twelve presets. The colour
  streams to the strip as you drag.
- **Brightness**: the strip's hardware brightness, 0 to 100%.
- **Ambient mode**: samples your screen about 25 times a second and eases the strip toward its
  average colour.
  - **Sample area**: follow the full screen, the center, an edge, or drag out any area you like.
  - **Colour boost**: brightens and saturates colourful parts of the screen so a dark red reads as
    red on the strip, while dark greys and black stay dark.
- **Lives in the tray.** Closing the window keeps Beetle running, ambient mode included. Click
  the tray icon to bring it back, or right-click it to turn the strip on or off, start or stop
  ambient mode, or quit. Launching Beetle again shows the copy that is already running.
- **Two themes**, Carapace and Scarab. Beetle remembers your strip, colour, brightness, sample
  area, boost and theme.

<p align="center">
  <img src="./docs/screenshots/scarab.png" alt="Beetle in the Scarab theme" width="360">
</p>

## Install

Run `Beetle-Setup-0.1.0.exe`, attached to the [GitHub releases](https://github.com/fezcode/Beetle/releases).
It installs per user, offers Desktop and Start Menu shortcuts, and keeps your settings when you
uninstall. Beetle is also listed in [Airlift](https://github.com/fezcode/Airlift).

## Use

1. Power the strip and make sure its phone app is not connected to it: an ELK-BLEDOM stops
   advertising while anything is connected, which hides it from scans.
2. Open Beetle and press **Scan**, then **Connect**.
3. Use **Power**, **Color** and **Brightness**, or press **Start ambient**. The colour picker
   pauses while ambient mode runs.
4. Close the window when you are done with it; Beetle carries on from the tray. Choose
   **Quit Beetle** from the tray icon's menu to exit completely.

```
Beetle -v, --version       print the version
Beetle -h, --help          print help
```

Settings live in `%LOCALAPPDATA%\Fezcode\Beetle\settings.json`.

## Build

Requires the .NET 10 SDK on Windows.

```powershell
.\build.ps1 -Run              # build and launch
.\build.ps1 -Test             # build and run the tests
.\build.ps1 -Test -Publish    # tests, then a self-contained dist/win-x64/desktop
.\build-installer.ps1 -SkipBuild   # package dist/installer/Beetle-Setup-0.1.0.exe with Forge
.\version.ps1                 # check the version is consistent everywhere
```

The installer is built with Fezcode's Forge, expected at `../Forge/build/forge.exe`.
See [AGENTS.md](AGENTS.md) for the full release flow.

## How it works

- **Bluetooth.** `Beetle.Core` talks to the strip through Windows' built-in Bluetooth LE stack
  (the WinRT `Windows.Devices.Bluetooth` APIs). It writes to GATT characteristic `fff3` under
  service `fff0`, without response, which is the channel the strip expects.
- **Protocol.** Commands are the 9-byte ELK-BLEDOM frames, `7E ... EF`, in `LedCommands.cs`. The
  vendor publishes no protocol; these come from community reverse-engineering, so a frame that
  misbehaves on a particular unit is a one-line change there.
- **Ambient.** `GdiScreenSampler` shrinks the chosen area of the primary monitor to a small bitmap
  with a halftone (area-averaging) stretch and averages it. `AmbientController` applies the colour
  boost, eases the strip toward each sample, and sends a frame only when the colour has moved
  enough to be worth a write. The strip is a single zone, so it shows one colour at a time.

| Project | Contents |
|---|---|
| `Beetle.Core` | Colour maths, command frames, Bluetooth transport, screen sampler, controllers, settings. No UI dependency. |
| `Beetle.Desktop` | The Avalonia app: theme, UI helpers, window chrome, colour picker, area picker, tray icon, main window. |
| `Beetle.Tests` | xUnit tests for frame encoding, colour maths, boost, sample areas and controller behaviour. |

## Limits

- Windows only. Avalonia is cross-platform; Bluetooth and the screen grab are not.
- Ambient mode samples the primary monitor.
- Brightness is sent as 0 to 100. Some ELK-BLEDOM variants expect another scale; if yours ignores
  it, adjust `LedCommands.Brightness`.

## License

[MIT](LICENSE.txt). The bundled fonts, DM Sans and Manrope, keep their own SIL Open Font Licenses.
