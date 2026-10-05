#!/usr/bin/env python3
"""Test supervisor lifetime with fake devices; never connects or injects input."""
import os
import pathlib
import signal
import subprocess
import sys
import tempfile
import time

source = pathlib.Path(sys.argv[1]).read_text()

def wait_for(predicate, message):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError(message)

with tempfile.TemporaryDirectory() as directory:
    root = pathlib.Path(directory)
    def executable(name, content):
        path = root / name
        path.write_text(content)
        path.chmod(0o755)
        return path

    receiver = executable("receiver", '''#!/usr/bin/env python3
import os, pathlib, time
root = pathlib.Path(os.environ['TEST_ROOT'])
with (root / 'starts').open('a') as file: file.write(str(os.getpid()) + '\\n')
while True: time.sleep(1)
''')
    stub = executable("stub", '''#!/usr/bin/env python3
import os, pathlib, socket, sys, time
root = pathlib.Path(os.environ['TEST_ROOT'])
(root / 'stub-pid').write_text(str(os.getpid()))
sock = socket.socket(socket.AF_UNIX)
sock.bind(str(root / sys.argv[1]))
while True: time.sleep(1)
''')
    executable("loginctl", '''#!/bin/sh
touch "$TEST_ROOT/session-checked"
if [ "$1" = list-sessions ]; then
    echo '42 1000 desktop seat0 tty2'
else
    printf '%s\\n' Active=yes Class=user Remote=no Type=wayland
fi
''')
    source = source.replace("waynergy=/usr/local/libexec/moniswitch-waynergy/waynergy", f"waynergy='{receiver}'")
    source = source.replace("wayland_stub=/usr/local/libexec/moniswitch-waynergy/moniswitch-wayland-stub", f"wayland_stub='{stub}'")
    supervisor = executable("supervisor", source)
    env = dict(os.environ, TEST_ROOT=str(root), XDG_RUNTIME_DIR=str(root),
               WAYLAND_DISPLAY="test-wayland", MONISWITCH_WIDTH="1920",
               MONISWITCH_HEIGHT="1080", MONISWITCH_DESKTOP_UID="1000",
               PATH=str(root) + ":" + os.environ["PATH"])
    def starts():
        path = root / "starts"
        return path.read_text().splitlines() if path.exists() else []

    for mode in ("0", "1"):
        env["MONISWITCH_ALL_SESSIONS"] = mode
        proc = subprocess.Popen(["sh", str(supervisor)], env=env,
                                stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        try:
            if mode == "0":
                wait_for(lambda: (root / "session-checked").exists(), "legacy desktop check did not run")
                time.sleep(1.2)
                assert not starts(), "legacy receiver competed with the desktop"
            else:
                (root / "session-checked").unlink()
                wait_for(lambda: len(starts()) == 1, "system receiver stopped for an active desktop")
                time.sleep(1.2)
                assert not (root / "session-checked").exists(), "system mode depends on desktop detection"
                os.kill(int(starts()[0]), signal.SIGKILL)
                wait_for(lambda: len(starts()) == 2, "receiver did not recover after child exit")
        finally:
            proc.terminate()
            proc.wait(timeout=5)
            # Clean up fake children even when a regression breaks the supervisor.
            for pid in starts() + ([(root / 'stub-pid').read_text()] if (root / 'stub-pid').exists() else []):
                try: os.kill(int(pid), signal.SIGKILL)
                except ProcessLookupError: pass
    assert not (root / "test-wayland").exists(), "supervisor did not clean up its socket"
print("Supervisor tests passed: legacy handoff, continuous desktop input, child restart, cleanup; no real input sent")
