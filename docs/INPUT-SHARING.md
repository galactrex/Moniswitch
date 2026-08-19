# Windows–Linux input sharing

Moniswitch keeps display control on Windows. Deskflow carries keyboard, mouse,
and clipboard data from Windows; Waynergy receives it on wlroots compositors
such as Hyprland. The connection uses TLS and a pinned server fingerprint.

## Topology

- Windows listens on TCP port `24800` through `deskflow-core`.
- X11 Linux systems can use a normal Deskflow client.
- Wayland systems using Hyprland or another wlroots compositor can use Waynergy
  with its `wlr` backend.
- Both PCs remain connected to the same router. No USB switch is required.

## One shortcut

The shortcut under **Quick route** defaults to `Ctrl+Alt+M`. Recording a new
shortcut updates Moniswitch and Deskflow together, then restarts the bridge.

Moniswitch observes the physical keypress without consuming it. Deskflow moves
input control while Moniswitch changes the configured monitor input. The same
shortcut returns both control and video to Windows.

## Windows / Input Link

Install Deskflow, then open **Input Link** in Moniswitch. Enter the Windows and
Linux screen names, select `deskflow-core.exe`, and press **Save + Start**.

On the first start, Moniswitch creates an isolated Deskflow configuration and a
3072-bit TLS identity under `%LocalAppData%\Moniswitch\deskflow`. Press
**Copy server pin** and place that value in Waynergy's fingerprint file. The
private key never leaves Windows.

The Linux client pins the Windows certificate. Moniswitch's headless server does
not run Deskflow's interactive client-approval dialog, so use this link only on
a trusted LAN and restrict TCP `24800` to the Linux computer in Windows
Firewall when other clients share the network.

## Linux / Waynergy

Install Waynergy and `wl-clipboard`, then copy these templates:

- `integration/waynergy/config.ini.example` → `~/.config/waynergy/config.ini`
- `integration/waynergy/moniswitch-waynergy.service` →
  `~/.config/systemd/user/moniswitch-waynergy.service`
- `integration/waynergy/server-fingerprint.example` →
  `~/.config/waynergy/tls/hash/WINDOWS_LAN_IP`

Edit the Windows LAN address, Linux screen name, and username in the log path.
Create the hash directory, then store the pin copied by Moniswitch in a file
named exactly after the configured Windows host:

```sh
mkdir -p ~/.config/waynergy/tls/hash
printf '%s\n' 'SHA256:REPLACE_WITH_COPIED_PIN' \
  > ~/.config/waynergy/tls/hash/WINDOWS_LAN_IP
chmod 700 ~/.config/waynergy/tls/hash
chmod 600 ~/.config/waynergy/tls/hash/WINDOWS_LAN_IP
```

Keep `tls/tofu = false`. Waynergy will then accept only the pinned Windows
certificate instead of trusting whichever server answers first.

The included key map keeps native Linux semantics: Ctrl remains Ctrl, Alt
remains Alt, and the Windows key becomes Super. Normal scan codes use an offset
of `8`; extended navigation and modifier keys are mapped explicitly.

Start the receiver:

```sh
systemctl --user daemon-reload
systemctl --user enable --now moniswitch-waynergy.service
```

The service restarts only after a failure. It belongs to the graphical session
and does not run as root.

## Clipboard

Text clipboard sharing uses the same encrypted connection. Copy normally on
the source computer, move control to the destination, then paste with that
application's normal command. Most Linux terminals use `Ctrl+Shift+V`; desktop
applications usually use `Ctrl+V`.

Waynergy uses `wl-paste` event watchers. There is no clipboard polling loop.

## Windows service cleanup

Moniswitch owns its `deskflow-core` child process. The separate automatic
Deskflow service is unnecessary for this layout. From an administrator
PowerShell window, run:

```powershell
.\tools\Disable-RedundantDeskflowService.ps1
```

This stops and disables only the service named `Deskflow`. Re-enable it later
with `Set-Service Deskflow -StartupType Automatic` if the standalone Deskflow
GUI needs it.

## Check the link

1. Start on the Windows monitor input.
2. Press the configured shortcut. Input and the selected display move to Linux.
3. Copy a short text value on either side, move control, and paste it on the
   other side.
4. Press the shortcut again. Input and video return to Windows.
