# Windows–Linux input sharing

Moniswitch keeps display control on Windows. Deskflow carries keyboard and mouse
input from Windows; Waynergy receives it on wlroots compositors such as Hyprland.
The connection uses TLS and a pinned server fingerprint.

Sharing with a Mac instead? See [`MACOS.md`](MACOS.md).

## Topology

- Windows listens on TCP port `24800` through `deskflow-core`.
- X11 Linux systems can use a normal Deskflow client.
- Wayland systems using Hyprland or another wlroots compositor can use Waynergy
  with its `wlr` backend.
- Both PCs remain connected to the same router. No USB switch is required.

## Desktop-independent Linux input

For Plasma, GNOME, Hyprland, or switching between desktops, use the optional
**system input** mode. It keeps the patched `uinput` receiver and its virtual
keyboard/mouse running through login, logout, and desktop changes. It does not
use the active compositor's virtual-input protocol. Clipboard remains disabled.
This targets the normal local desktop seat; KDE session behavior still needs
verification on the user's installation.

After installing and patching the pre-login receiver, run from Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Enable-LinuxSystemInput.ps1
```

The helper uses the existing local SSH settings, stages and tests the supervisor,
then asks for the Linux sudo password in the terminal. It keeps the dedicated
service account, pinned TLS certificate, and restricted `/dev/uinput` access.
A user-service condition prevents the desktop receiver competing with it.
No Windows connector mappings are changed.

To restore the previous login/desktop split from a Linux terminal:

```sh
sudo sh ~/.local/state/Moniswitch/system-input/enable-system-input.sh --restore
```

The original supervisor and service fragments are backed up under
`/var/lib/moniswitch-input/system-input-backup`.

For an Xorg login screen, apply `integration/waynergy/patch-waynergy-uinput.py`
to the Waynergy source tree before rebuilding the binary used by the pre-login
installer. Waynergy 0.0.17 otherwise sends absolute movement from a device that
Xorg/libinput treats as relative, so the greeter discards mouse movement.
The same patch keeps screen edges in sync: Deskflow stops sending movement once
its copy of the pointer reaches an edge, so when the Linux pointer has drifted,
for example after the display reconnects during an input switch, reaching an
edge pushes the Linux pointer onto that edge too instead of leaving an
invisible wall. Rerunning the patch on an already patched tree adds only what
is missing. `test-uinput-motion.py` checks the patched handlers without sending
real input.

## One shortcut

The shortcut under **Quick route** defaults to `Ctrl+Alt+M`. Recording a new
shortcut updates the private Deskflow control binding and restarts the bridge.

Moniswitch owns the physical shortcut. After every key in that shortcut is
released, it sends Deskflow a reserved, modifier-free control signal: `F23`
targets Windows and `F24` targets Linux. Quick route Source A is therefore the
Windows input and Source B is the Linux input while Input Link is enabled. The
explicit targets cannot drift after a restart, and the private keys are never
sent to applications. Deskflow pointer-edge links are intentionally absent, so
moving the mouse at an edge cannot silently undo the selected target.

The shortcut is safe to press as soon as Moniswitch opens. A press during the
launch-time display scan is queued, and a newly started Linux receiver gets a
short connection window before Moniswitch moves the display. Repeated presses
while either wait is in progress collapse into the same pending switch.

The display buttons and saved routes use the same handoff for the configured
Source A / Source B display. Moniswitch waits for Deskflow to report its target
before sending the monitor command. If the monitor command fails, it attempts
to restore the previous input target. It refreshes its shortcut observer ahead
of Deskflow's hook so the return shortcut remains visible while input is remote.
Deskflow INFO status is read in memory; file logging remains disabled.

When login replaces the pre-login receiver with the desktop receiver, Moniswitch
keeps the selected monitor route and restores input to the new connection once
it settles. No Windows-to-Linux round trip is needed. Reconnecting a receiver
does not take input away from Windows when the Windows source is selected.

## Windows / Input Link

Install Deskflow, then open **Input Link** in Moniswitch. Enter the Windows and
Linux screen names, select `deskflow-core.exe`, and press **Save + Start**.
Leave **Start Moniswitch with Windows** enabled if the link and global shortcut
should return automatically after a Windows restart.

Use Deskflow `1.26.0` build 167 or newer. Earlier 1.26 builds contain a TLS
accept bug fixed by Deskflow after the stable 1.26.0 package. Moniswitch repairs
its isolated server settings on every launch: TLS stays enabled, while Deskflow
does not demand a separate client certificate from the already pinned Waynergy
client.

On the first start, Moniswitch creates an isolated Deskflow configuration and a
3072-bit TLS identity under `%LocalAppData%\Moniswitch\deskflow`. Press
**Copy server pin** and place that value in Waynergy's fingerprint file. The
private key never leaves Windows.

The Linux client pins the Windows certificate. Moniswitch's headless server does
not run Deskflow's interactive client-approval dialog, so use this link only on
a trusted LAN and restrict TCP `24800` to the Linux computer in Windows
Firewall when other clients share the network.

## Linux / Waynergy

Install Waynergy, then copy these templates:

- `integration/waynergy/config.ini.example` → `~/.config/waynergy/config.ini`
- `integration/waynergy/moniswitch-waynergy.service` →
  `~/.config/systemd/user/moniswitch-waynergy.service`
- `integration/waynergy/server-fingerprint.example` →
  `~/.config/waynergy/tls/hash/WINDOWS_LAN_IP`

Waynergy 0.0.17 can send an initial clipboard notification before its protocol
hello. Strict Deskflow releases reject that packet order immediately. If
Waynergy is built from source, apply the included compatibility fix before
building it:

```sh
./integration/waynergy/patch-waynergy-handshake.sh \
  /path/to/waynergy/src/uSynergy.c
```

The compatibility patch remains useful for custom Waynergy builds, but the
Moniswitch service disables clipboard traffic so parser failures cannot affect
keyboard and mouse input.

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

The supplied service starts from the normal systemd user target, waits for the
compositor's live Wayland socket, forces the wlroots backend, and treats
disconnects and timeouts as restartable events. If either computer disappears
or restarts, the receiver keeps retrying from the user session. It does not run
as root and does not grant Waynergy access to `/dev/uinput`.

### Before desktop login

The `wlr` backend cannot control a password screen that appears before the
user's Wayland compositor starts. The supplied user service waits safely at
that boundary. Pre-login control requires a separately installed boot receiver
with tightly scoped `/dev/uinput` access; it is a system-wide input-injection
permission and must never be approximated with `chmod 666 /dev/uinput`.

On a trusted private network, install the optional receiver from the repository
after the normal Waynergy link and TLS pin already work. The Linux machine also
needs a C compiler, `pkg-config`, `setsid`, and the Wayland server development
files:

```sh
sudo ./integration/waynergy/install-boot-input.sh "$USER" "$(command -v waynergy)"
```

The installer copies only the required configuration into a root-owned
location. It creates a non-login `moniswitch-input` service account, grants
`/dev/uinput` only to that account, disables clipboard handling before login,
and builds a tiny private Wayland stub. The stub exposes only a standard
keyboard-map seat; it provides no compositor, output, or input-injection
globals. Waynergy can initialize without attaching to the login screen. The boot
receiver retires when logind reports the user's real Wayland session; the
normal `wlr` receiver then handles the desktop.

The installer detects the first connected Linux display mode. Pass width and
height as the third and fourth arguments only when that detection is wrong:

```sh
sudo ./integration/waynergy/install-boot-input.sh \
  "$USER" "$(command -v waynergy)" 1920 1080
```

Remove the pre-login receiver without removing the normal user configuration:

```sh
sudo ./integration/waynergy/uninstall-boot-input.sh
```

## Clipboard

Clipboard sharing is disabled for the Waynergy bridge. Waynergy 0.0.17 can lose
protocol framing on newer multi-format Deskflow payloads, which also takes down
keyboard and mouse input. Moniswitch keeps this channel input-only until a
compatible receiver can isolate clipboard failure from peripheral control.

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
