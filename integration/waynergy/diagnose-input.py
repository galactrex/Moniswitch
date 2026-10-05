#!/usr/bin/env python3
"""Count Waynergy kernel events without grabbing devices or recording keys."""
import collections
import json
import os
import pathlib
import select
import struct
import time

if os.geteuid() != 0:
    raise SystemExit("Run with sudo to read the system receiver's input devices.")

event = struct.Struct("llHHi")
devices = {}
for node in pathlib.Path("/sys/class/input").glob("event*"):
    name = (node / "device/name").read_text().strip()
    if name in ("waynergy keyboard", "waynergy mouse"):
        fd = os.open("/dev/input/" + node.name, os.O_RDONLY | os.O_NONBLOCK)
        devices[fd] = name.split()[-1]

receivers = []
for proc in pathlib.Path("/proc").glob("[0-9]*"):
    try:
        if (proc / "comm").read_text().strip() == "waynergy":
            receivers.append(proc)
    except OSError:
        pass

def io_snapshot():
    result = {}
    for proc in receivers:
        try:
            for line in (proc / "io").read_text().splitlines():
                key, value = line.split(":", 1)
                if key in ("rchar", "wchar", "syscr", "syscw"):
                    result[key] = result.get(key, 0) + int(value)
        except OSError:
            pass
    return result

print("Ready: switch to Linux, move the mouse, tap Shift, then return to Windows.", flush=True)
print("Observing for 35 seconds; no keys, coordinates, or device grabs are recorded.", flush=True)
counts = collections.Counter()
start_io = io_snapshot()
deadline = time.monotonic() + 35
try:
    while devices and time.monotonic() < deadline:
        ready, _, _ = select.select(list(devices), [], [], min(1, max(0, deadline - time.monotonic())))
        for fd in ready:
            try:
                data = os.read(fd, event.size * 256)
            except BlockingIOError:
                continue
            if not data:
                os.close(fd)
                del devices[fd]
                counts["devices_removed"] += 1
                continue
            for offset in range(0, len(data) - event.size + 1, event.size):
                _, _, kind, _, _ = event.unpack_from(data, offset)
                if kind in (1, 2):
                    counts[devices[fd] + ("_key_events" if kind == 1 else "_motion_events")] += 1
finally:
    end_io = io_snapshot()
    for fd in devices:
        os.close(fd)
print(json.dumps({"receiver_count": len(receivers), "device_count": len(devices),
                  "events": dict(counts),
                  "receiver_io_delta": {key: end_io.get(key, 0) - value for key, value in start_io.items()}}, sort_keys=True))
