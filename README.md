# Moniswitch

**Switch monitor inputs, keyboard, mouse, and clipboard from one Windows control surface.**

Moniswitch is a free, open-source Windows monitor input switcher and software
KVM. It uses DDC/CI to route individual monitors between HDMI, DisplayPort,
USB-C, DVI, and other reported inputs. Optional LAN tools move keyboard, mouse,
clipboard, and a Linux desktop without buying another collection of boxes.

The useful version of the idea is simple: keep every monitor connected, then
choose which computer gets each screen. The buttons can remain behind the
monitor where the manufacturer apparently intended them to become folklore.

## What Moniswitch does

- Finds connected physical monitors and reads their DDC/CI input capabilities.
- Switches each supported display independently with verified MCCS VCP `0x60`
  commands.
- Saves multi-monitor routes and recalls them from the main window or tray.
- Assigns a changeable keyboard shortcut to a two-input quick route.
- Makes the shortcut target explicit by monitor number and model in both the
  main window and notification-area menu.
- Shares keyboard, mouse, and text clipboard between Windows and Linux through
  Deskflow and Waynergy.
- Can start with Windows while the Linux user service retries after restarts,
  keeping Input Link available without reopening both tools by hand.
- Streams one Linux desktop to one selected monitor or across the full Windows
  display layout with LAN Canvas.
- Keeps private connection details hidden in the interface by default.
- Runs without telemetry, analytics, an account, an updater, or a cloud service.

Moniswitch does not emulate a monitor input. The monitor must support DDC/CI,
and the second computer still needs a working video cable and signal. Software
can move the door; it cannot convince an unplugged GPU to walk through it.

## Start here

1. Enable **DDC/CI** in each monitor's on-screen menu.
2. Keep the Windows PC connected to every monitor Moniswitch will control.
3. Connect the other computer to an unused HDMI, DisplayPort, USB-C, or DVI
   input on the same monitor.
4. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
   on Windows.
5. Open `Moniswitch.exe`, press **Scan**, choose an input, then press **Route**.
6. Configure **Quick route** if one shortcut should move a display between two
   inputs.

The complete first-run guide is in
[`docs/GETTING-STARTED.md`](docs/GETTING-STARTED.md). If a monitor reports HDMI
but keeps showing DisplayPort, go directly to
[`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md). That problem has paperwork.

## Choose the setup you need

| Setup | Video | Keyboard, mouse, clipboard | Extra software |
|---|---|---|---|
| Display routing | Physical monitor cables | Stays on each computer | None |
| Display routing + Input Link | Physical monitor cables | Moves between Windows and Linux | Deskflow + Waynergy |
| LAN Canvas | Linux desktop streamed over LAN | Uses Input Link | Sunshine + Moonlight + SSH |

Display routing is the core. Input Link and LAN Canvas are optional and remain
separate so a broken stream cannot take monitor control down with it.

## How it works

| Layer | Job | Connection |
|---|---|---|
| DDC/CI router | Reads and changes physical monitor inputs | Windows GPU to monitor |
| Input Link | Carries keys, pointer events, and text clipboard | Encrypted trusted LAN |
| LAN Canvas | Carries a hardware-encoded Linux desktop | Sunshine to Moonlight over LAN |

Monitor discovery runs at launch and when **Scan** is pressed. DDC writes occur
only when a route changes. LAN Canvas starts Sunshine and Moonlight only while
the canvas is live. The normal tray path is event-driven; it does not sit there
interrogating the network for sport.

## Requirements

### Core display routing

- Windows 10 or Windows 11.
- .NET 8 Desktop Runtime.
- A monitor that exposes DDC/CI and MCCS input selection.
- A direct display connection that carries DDC commands. Some docks, adapters,
  capture devices, and KVMs block them.

### Input Link

- Both computers on the same trusted local network.
- Deskflow on Windows.
- Deskflow on X11 Linux, or Waynergy plus `wl-clipboard` on supported wlroots
  Wayland compositors.

See [`docs/INPUT-SHARING.md`](docs/INPUT-SHARING.md).

### LAN Canvas

- Moonlight and OpenSSH Client on Windows.
- Sunshine, systemd user services, a supported hardware encoder, and the
  supplied integration scripts on Linux.
- The current Linux sender integration targets Hyprland.

See [`docs/LAN-CANVAS.md`](docs/LAN-CANVAS.md).

## Install and build

Official binaries will be attached to GitHub Releases. Until the first tagged
release is published, build from source with the .NET 8 SDK:

```powershell
dotnet build .\Moniswitch.csproj -c Release
dotnet run --project .\Moniswitch.csproj
```

Create the clean portable Windows bundle and ZIP with:

```powershell
.\tools\Build-Package.ps1
```

The packaging command runs the release privacy gate before it writes the ZIP.
Build artifacts, local settings, credentials, private network addresses, and
machine-specific identifiers are rejected.

## Documentation

- [Getting started](docs/GETTING-STARTED.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Windows-Linux input sharing](docs/INPUT-SHARING.md)
- [LAN Canvas](docs/LAN-CANVAS.md)
- [Runtime footprint](docs/PERFORMANCE.md)
- [Privacy boundary](PRIVACY.md)
- [Security reporting](SECURITY.md)
- [Official repository policy](CONTRIBUTING.md)

## Privacy

Settings stay under `%LocalAppData%\Moniswitch` on the Windows PC. They are not
bundled with the source or release archive. Private view masks saved route names,
addresses, usernames, device identifiers, and paths while keeping monitor model
names visible so a person can identify the physical display in front of them.

Do not attach raw settings, logs, screenshots, or network output to an issue.
The issue form explains what to redact. The full boundary is in
[`PRIVACY.md`](PRIVACY.md).

## Official repository

Moniswitch is authored, maintained, and released by Galactrex. Bug reports and
feature requests are welcome. The official repository does not accept external
pull requests or publish third-party builds under the Moniswitch name.

The MIT License still permits forks and modifications. A fork is allowed to be
a fork. It simply does not become an official release through confidence alone.

## License

Copyright (c) 2026 Galactrex.

Moniswitch is released under the [MIT License](LICENSE). The license permits use,
copying, modification, and distribution while requiring the copyright and
license notice to remain with copies of the software.
