# LAN Canvas

LAN Canvas streams one Linux desktop into a selected Windows monitor or across
the complete Windows virtual-screen bounds. The chosen target becomes one
matching headless output on Linux, then one borderless Moonlight window placed
exactly on that monitor or display span on Windows.

This single-canvas design is deliberate. A target can be one monitor or the
full arrangement, but each session remains one hardware-decoded HEVC surface.
That keeps the sender count at one and preserves the selected geometry.

## Requirements

- Windows 10 or 11 with Moniswitch, OpenSSH Client, and Moonlight Qt.
- Both computers on the same trusted LAN.
- Linux with Hyprland, systemd user services, and hardware HEVC encoding.
- SSH key authentication from Windows to Linux.

Sunshine chooses the available hardware encoder. The tested NVIDIA path uses
NVENC; AMD and Intel systems can use Sunshine's supported hardware backends.
Very wide canvases may exceed H.264 limits, so Moniswitch requests HEVC.

## Install the Linux sender

Copy `integration/sunshine` to the Linux computer and run:

```sh
cd integration/sunshine
sh ./install-user.sh
```

The installer downloads the pinned Sunshine AppImage, verifies its SHA-256,
extracts it under `~/.local/opt`, and creates an isolated Moniswitch
configuration. It generates a random pairing password with mode `0600`; the
password stays on Linux.

The installed service is not enabled. `moniswitch-canvas start` creates the
headless output, temporarily suspends Linux's physical outputs, and starts
Sunshine with the canvas as its only capture target. `moniswitch-canvas stop`
stops Sunshine, restores the user's configured physical layout, and removes the
headless output. This avoids Sunshine's current Hyprland headless-output
fallback without leaving another service or capture process resident.

## Windows setup

1. Open **LAN Canvas** in Moniswitch.
2. Choose **All displays / span** or one monitor under **Canvas target**.
3. Enter the Linux address, SSH user, private-key path, and Moonlight path.
4. Copy the UFW rules, run them on Linux, then press **Pair** once.
5. Press **Start Canvas**. Press **Stop** to return the sender to zero.

The target list uses the monitor model name, resolution, and orientation. A
single-monitor target creates a stream at that monitor's native Windows bounds;
the span target uses the union of every connected display.

Moniswitch resolves the address that Linux sees for the current Windows SSH
session. **Copy UFW rules** places these host-restricted commands on the
clipboard:

```sh
sudo ufw allow from WINDOWS_LAN_IP to any port 47984:47990 proto tcp
sudo ufw allow from WINDOWS_LAN_IP to any port 48010 proto tcp
sudo ufw allow from WINDOWS_LAN_IP to any port 47998:48000 proto udp
```

Only that Windows address receives access. The copied set matches Sunshine's
documented TCP `47984-47990`, TCP `48010`, and UDP `47998-48000` mappings.

## What starts

| State | Windows | Linux |
|---|---|---|
| Canvas off | Moniswitch only | no Sunshine process, no virtual output |
| Canvas live | one Moonlight process | one Sunshine process and one headless output |

Keyboard, mouse, and clipboard stay on the separate Deskflow/Waynergy link.
Sunshine input is disabled, so the video path does not create a second input
stack. Move the Windows pointer through the configured Deskflow screen edge to
control Linux; the keys, pointer events, and text clipboard then travel through
that single input link while Moonlight carries video only.

Waynergy is rebound automatically after Canvas changes the Linux output
topology and again when the physical output is restored. This keeps its pointer
coordinate map attached to the currently visible output while preserving the
same encrypted Deskflow connection and native Linux key mapping.

## Verify the lifecycle

On Linux:

```sh
systemctl --user is-active moniswitch-sunshine.service
hyprctl monitors MONISWITCH
pgrep -a sunshine
```

After **Stop**, the service should be `inactive`, `pgrep` should return nothing,
and `MONISWITCH` should no longer appear.

References: [Sunshine configuration](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2configuration.html),
[Sunshine's Hyprland headless-capture issue](https://github.com/LizardByte/Sunshine/issues/5087),
[Hyprland `hyprctl` headless outputs](https://wiki.hypr.land/Configuring/Advanced-and-Cool/Using-hyprctl/),
[Moonlight Qt](https://github.com/moonlight-stream/moonlight-qt).
