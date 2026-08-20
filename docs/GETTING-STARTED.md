# Getting started

Moniswitch controls the monitor, not the other computer. Each computer keeps
its own video connection; Moniswitch asks the display which input to show.

Start with one monitor and two live video cables. Add the grand architecture
after the first route works.

## 1. Check the monitor

Open the monitor's physical on-screen menu and enable **DDC/CI**. The setting
may appear under System, General, Other, or Setup.

The monitor must expose the MCCS input-selection control, VCP code `0x60`.
Moniswitch reads the monitor's reported capabilities and lists only the inputs
it can address. Support varies by monitor firmware and connection type.

For the first test:

- connect the Windows PC directly to the monitor;
- avoid a dock, adapter, capture device, or hardware KVM if possible;
- connect the second computer to another input on that monitor;
- wake both computers and confirm both video cables work.

## 2. Open Moniswitch

Install the .NET 8 Desktop Runtime, extract the official ZIP, and run
`Moniswitch.exe`.

Moniswitch starts in the notification area. Opening the executable again brings
the same instance forward; it does not create a procession of identical tray
icons.

Windows may show a SmartScreen warning while official builds are unsigned.
Verify that the file came from the Galactrex GitHub release and compare its
published SHA-256 hash before choosing **Run anyway**. Never download a build
from an issue attachment or a third-party mirror and assume the icon means
anything.

## 3. Find and route a display

1. Press **Scan**.
2. Confirm the monitor model appears and reports **Routable**.
3. Choose the second computer's input in that monitor's source list.
4. Press **Route**.
5. Choose the Windows input and press **Route** again.

Moniswitch retries the DDC write and reads the current input back when the
monitor continues answering. Some monitors stop answering DDC after leaving the
Windows input; Moniswitch keeps the physical handle open so it can still send
the return command.

## 4. Configure Quick route

Quick route toggles one routable monitor between two inputs.

1. Choose **Hotkey display**. The number matches Windows **Display settings →
   Identify**: `01`, `02`, and so on. Monitor cards use the same Windows number,
   with the model name visible beside it.
2. Set **Source A** and **Source B**.
3. Select the shortcut field and press the new combination.
4. Use **Route selected** once to test it.

The same target is available from the notification-area menu under **Hotkey
display**. Choosing a target does not switch it immediately; it decides which
display the shortcut will move.

The default shortcut is `Ctrl+Alt+M`. A shortcut must contain Ctrl, Alt, or
Shift so normal typing cannot transfer a monitor halfway through a sentence.
Letters, numbers, function keys, arrows, navigation keys, and common punctuation
are supported. Compact keyboards may require Fn to produce an F-key, so bind a
letter or navigation key if the keyboard treats function keys as a side quest.

## 5. Save a multi-monitor route

Set the desired input for each monitor, press **Save route**, and give it a local
name. Saved routes can be recalled from the main window or the notification-area
menu.

Route names stay in the local settings file. With private view enabled they are
shown as numbered saved routes, while monitor model names remain visible.

## 6. Add optional computer handoff

Display routing is now complete. Add only what the setup needs:

- [Input Link](INPUT-SHARING.md) moves keyboard, mouse, and text clipboard
  between Windows and Linux.
- [LAN Canvas](LAN-CANVAS.md) streams the Linux desktop to one monitor or the
  full Windows display arrangement.

If the first route did not work, stop here and use the
[troubleshooting guide](TROUBLESHOOTING.md). A streaming stack does not improve
a cable problem. It merely gives the cable problem colleagues.
