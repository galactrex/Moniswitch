# Windows and Mac input sharing

Press the shortcut. The monitor switches to the Mac, and the Windows keyboard
and mouse go with it. Press it again and both come back.

Moniswitch runs on Windows and owns the shortcut. The Mac runs Deskflow's own
client: no patches, no extra drivers. Clipboard sharing is on for a Mac,
because the full Deskflow client handles every clipboard format Windows sends.

> **Status: experimental.** We tested the Windows side, the generated
> configuration, and the pinned TLS handshake against the real Deskflow 1.26
> programs. The launchd agent and the macOS permission steps have not met a
> real Mac yet. If you have one, tell us how it went through the issue form.

## What you need

- A Mac on the same trusted local network as the Windows PC.
- [Deskflow](https://github.com/deskflow/deskflow/releases) 1.26 or newer on
  **both** computers. On the Mac, install `Deskflow.app` into `Applications`.
- The Mac connected by cable to an unused input on the shared monitor (USB-C,
  HDMI or DisplayPort). Moniswitch switches that input over DDC/CI from Windows.

## 1. Windows

1. Open **Input Link** in Moniswitch.
2. Under **Other computer**, choose **MAC**.
3. Keep or change the **Mac name**. The Mac must use exactly the same name.
4. Select `deskflow-core.exe`, then press **SAVE + START**.
5. Press **COPY SERVER PIN**. You will paste it on the Mac.
6. In **Quick route**, set **Source A** to the Windows input and **Source B**
   to the input the Mac is plugged into.

Only allow TCP `24800` from the Mac in Windows Firewall if other devices share
the network.

## 2. Mac

Copy the `integration/macos` folder from the Moniswitch download to the Mac,
then run in Terminal as your normal user (not `sudo`):

```sh
sh integration/macos/install-input-client.sh \
  --server WINDOWS_LAN_IP \
  --pin 'SHA256:PASTE_THE_COPIED_PIN' \
  --name mac-pc
```

The installer:

- writes a private Deskflow client configuration to
  `~/Library/Moniswitch/input`, separate from any Deskflow app settings;
- pins the Windows certificate, so the Mac only accepts that one server;
- creates a TLS identity for the Mac that never leaves it;
- registers a launchd agent, `com.galactrex.moniswitch.input`, that starts at
  login and restarts if it stops. No log file is written.

### Allow input control

macOS blocks every program from moving the pointer or typing until you allow
it. The installer opens the right page:

**System Settings → Privacy & Security → Accessibility**

Press **+**, then **Cmd+Shift+G**, and paste the `deskflow-core` path the
installer printed (normally
`/Applications/Deskflow.app/Contents/MacOS/deskflow-core`). Turn it on, then
restart the receiver:

```sh
launchctl kickstart -k "gui/$(id -u)/com.galactrex.moniswitch.input"
```

Quit the Deskflow app itself if it is open. This receiver replaces it.

## Keyboard layout

With **Ctrl acts as Command on the Mac** enabled (the default), Windows habits
keep working:

| Windows key | On the Mac |
|---|---|
| Ctrl | ⌘ Command: Ctrl+C copies, Ctrl+Q quits |
| Windows key | Control |
| Alt | ⌥ Option |
| Shift | Shift |

Turn the option off for Deskflow's native layout, where the Windows key is
Command and Ctrl stays Control.

## Remove it

```sh
sh integration/macos/install-input-client.sh --uninstall
```

Add `--purge` to also delete the configuration and the Mac's TLS identity.

## Troubleshooting

- **Moniswitch says Mac input is offline.** The Mac is not connected yet. Check
  the address, that both names match, and that the pin is the one currently
  shown by **COPY SERVER PIN**. Rerunning the installer is safe.
- **Connected, but the pointer does not move.** Accessibility permission is
  missing or was granted before a Deskflow update replaced the binary. Remove
  and re-add `deskflow-core`, then restart the receiver.
- **"Deskflow is damaged" or blocked from opening.** Open `Deskflow.app` once
  from Finder with right-click → **Open** to approve it, then rerun the
  installer.
- **See what the receiver is doing.** Stop the agent and run the client in the
  foreground:

  ```sh
  launchctl bootout "gui/$(id -u)/com.galactrex.moniswitch.input"
  /Applications/Deskflow.app/Contents/MacOS/deskflow-core client \
    --settings ~/Library/Moniswitch/input/Deskflow.conf
  ```

  Rerun the installer afterwards to restore the background agent. The output
  includes addresses; redact it before sharing.

LAN Canvas (streaming the other computer's desktop) remains Linux-only.
