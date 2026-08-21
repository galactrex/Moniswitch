# Troubleshooting

Start at the layer that failed. Moniswitch has three independent paths:

1. DDC/CI changes the monitor input.
2. Input Link carries keyboard, mouse, and text clipboard.
3. LAN Canvas carries video over the network.

Fixing all three at once is not faster. It is only louder.

## The monitor does not appear

1. Confirm Windows detects the display in **Settings > System > Display**.
2. Connect the Windows GPU directly to the monitor for testing.
3. Remove docks, adapters, capture devices, and hardware KVMs temporarily.
4. Power the monitor off and on, then press **Scan**.
5. Update the GPU driver if Windows itself is failing to enumerate the display.

Moniswitch can only inspect physical monitors Windows exposes through its
monitor APIs.

## The monitor appears but is not routable

- Enable **DDC/CI** in the monitor's physical menu.
- Try another port or cable from the Windows PC.
- Disable any monitor setting that blocks external control.
- Check whether the monitor exposes input selection through MCCS VCP `0x60`.
- Test without a dock or adapter; many pass video but block DDC traffic.

A monitor can support brightness control over DDC without supporting software
input selection. DDC/CI is a road, not a promise about every destination.

## Moniswitch reports HDMI, but the screen still shows DisplayPort

The app sends the input-selection command, waits, then reads the monitor's
reported value when possible. Some firmware acknowledges the value without
moving the visible source, or reports the requested source before the panel has
actually switched.

1. Press **Scan** and retry the route once.
2. Disable automatic input selection in the monitor menu, then retry.
3. Use the monitor's physical control once to confirm that exact input works.
4. Power-cycle the monitor.
5. Test the Windows connection without a dock or adapter.

Moniswitch does not inspect or reject the second computer's HDMI signal. It has
no code path into that GPU. If the monitor switches but shows **No signal**, use
the next section.

## The monitor switches and shows No signal

- Wake the second computer and unlock it.
- Confirm the cable is connected to the GPU output, not an inactive motherboard
  port.
- Test the cable and input manually with the monitor controls.
- Confirm the second operating system has enabled that output.
- Try a resolution and refresh rate the monitor accepts.

The source command and the source signal are separate facts. Moniswitch handles
the first one.

## The monitor switched away and will not switch back

Keep Moniswitch running on Windows while testing another source. Use the global
shortcut or the notification-area **Displays** menu to route the display back.

If the monitor firmware stops accepting DDC on an inactive input, use its
physical source control once, return to Windows, then:

1. enable DDC/CI again if the monitor reset it;
2. press **Scan**;
3. verify both sources with Quick route.

## The shortcut does nothing

- Record another shortcut in **Quick route**.
- Include Ctrl, Alt, or Shift.
- On compact keyboards, avoid an F-key that requires Fn.
- Close software that already owns the same global combination.
- Verify both Quick route sources are different and the chosen monitor is
  routable.
- Open **Hotkey display** in the main window or notification-area menu and
  confirm the checked `01 / MODEL` entry is the physical display you intend to
  move. The number is Windows' **Display settings → Identify** number, not the
  screen's left-to-right position. Selecting it changes the shortcut target
  without routing the display.

Moniswitch observes the shortcut without consuming it so Deskflow can receive
the same physical keypress.

## Keyboard input becomes strange on Linux

The supplied Waynergy configuration preserves Ctrl and Alt, maps the Windows
key to Super, applies the normal evdev offset, and explicitly maps extended
keys. Start from `integration/waynergy/config.ini.example` instead of building a
mapping by rumor.

Then check:

```sh
systemctl --user status moniswitch-waynergy.service
journalctl --user -u moniswitch-waynergy.service -n 100 --no-pager
```

Do not publish the raw output. Remove usernames, home paths, hostnames, network
addresses, and certificate fingerprints before attaching a relevant excerpt to
an issue.

## Input Link does not connect

1. Confirm both computers are on the same trusted LAN.
2. Confirm **Input Link** points to `deskflow-core.exe`.
3. Confirm the Windows and Linux screen names match the client configuration.
4. Allow TCP port `24800` from the Linux computer through Windows Firewall.
5. Copy the server pin again and update Waynergy's fingerprint file.
6. Restart the user service on Linux.

For Hyprland and other wlroots sessions, use the supplied user service. It
starts from `default.target`, finds the live Wayland socket, forces Waynergy's
`wlr` backend, and restarts after a disconnect or timeout. This avoids relying
on `graphical-session.target`, which some compositors never activate. A startup
message saying that `/dev/uinput` could not be opened is not evidence that the
wlroots backend failed; do not solve it with `chmod 666 /dev/uinput`.

If Linux is still showing its login screen, Hyprland has not started and the
`wlr` receiver cannot exist yet. This is not a slow network connection. Use a
properly installed boot-level `uinput` receiver for pre-login control, or log in
locally and let the normal user service connect after the compositor starts.

The server certificate is pinned by the Linux client. If the Windows TLS
identity was regenerated, the old pin is supposed to fail. Security behaving as
designed can be inconvenient in a remarkably authentic way.

## Clipboard text does not paste

- Install `wl-clipboard` on Wayland Linux.
- Confirm the Waynergy user service is active.
- Copy text after the input link is connected.
- Use `Ctrl+Shift+V` in most Linux terminals and `Ctrl+V` in desktop apps.

Input Link shares text clipboard content. It does not promise file transfer,
images, rich formatting, or the contents of an application-specific clipboard.

## Windows OpenSSH Client not found

Install **OpenSSH Client** from Windows **Optional features**, then reopen
Moniswitch. LAN Canvas calls the Windows OpenSSH client directly and uses
non-interactive key authentication.

## SSH authentication fails

- Confirm the Linux SSH server is installed, running, and reachable.
- Confirm the host and user in LAN Canvas are correct.
- Select the private key that matches a public key in the Linux user's
  `authorized_keys` file.
- Confirm the key file exists and the Linux account can run the supplied
  user-level canvas command.
- Test the same key with Windows `ssh` before testing LAN Canvas.

Moniswitch uses SSH batch mode. It will not display a password prompt or collect
a sudo password. That is deliberate; credentials do not belong in an app log or
an issue screenshot.

## UFW failed or Sunshine is blocked

Press **Copy UFW rules** in LAN Canvas. The generated commands restrict
Sunshine's ports to the Windows address observed by the Linux SSH session.

On Linux, first check:

```sh
sudo ufw status verbose
```

Then run the copied commands in a terminal with administrator rights. If UFW is
not installed or is not the firewall used by that distribution, translate the
same restricted ports into the active firewall instead of enabling a second
firewall blindly.

Do not replace the restricted source with `Anywhere` merely to silence an error.
The error will become quiet. The network exposure will not.

## LAN Canvas treats all monitors as one surface

Choose a specific model under **Canvas target** to place the stream on one
monitor. Choose **All displays / span** only when one borderless canvas should
cover the complete Windows virtual-screen bounds.

LAN Canvas intentionally creates one stream surface. It can match one monitor
or span the arrangement; it does not create one independent Sunshine stream per
monitor.

## LAN Canvas opens and immediately closes

Check the Linux lifecycle:

```sh
systemctl --user status moniswitch-sunshine.service
hyprctl monitors
pgrep -a sunshine
```

Confirm that:

- the supplied Sunshine integration was installed;
- the hardware encoder is available;
- Moonlight is paired;
- the copied firewall rules are active;
- the selected geometry is supported by the encoder.

After **Stop**, Sunshine should be inactive and the temporary `MONISWITCH`
output should be gone.

## Before opening an issue

Run the latest official build, search existing issues, and use the supplied bug
form. Redact:

- public and private IP addresses;
- usernames, machine names, and home directories;
- serial numbers and device identifiers;
- SSH key paths, certificate fingerprints, and tokens;
- unrelated browser tabs, notifications, and filenames in screenshots.

A ten-line sanitized excerpt is useful. A complete settings directory is an
incident wearing the costume of a bug report.
