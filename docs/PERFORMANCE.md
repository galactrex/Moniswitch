# Runtime footprint

Moniswitch keeps the tray path still. It does not poll displays, the clipboard,
or the network. Monitor discovery runs at launch and on **Scan**; input and
clipboard changes arrive as events; LAN Canvas exists only while requested.

## Reference measurement

Measured on Windows 11 on 2026-08-18 after closing all Moniswitch windows:

| Process | Working set | Private memory | CPU over 10 seconds |
|---|---:|---:|---:|
| `Moniswitch.exe` | 13.8 MB | 16.2 MB | 0 ms |
| `deskflow-core.exe` | 18.5 MB | 3.6 MB | 15.6 ms |

The Linux input path measured approximately 15.5 MB total RSS across Waynergy
and its two event-driven `wl-paste` watchers. Sunshine is absent while LAN
Canvas is off.

Working-set values vary with Windows memory pressure, display count, runtime
version, and which windows have been opened. The CPU sample is the stronger idle
signal: no scheduled polling work ran during the interval.

## Reproduce it

Start Moniswitch, close its settings window so only the notification icon
remains, then run:

```powershell
.\tools\Measure-Idle.ps1 -Seconds 10
```

The script reads process counters only. It does not change Moniswitch or the
Deskflow connection.

Deskflow's installer may leave an unrelated automatic `Deskflow` service
running. Moniswitch does not use it. Disable that one redundant service once
with `tools/Disable-RedundantDeskflowService.ps1` from an administrator shell.
