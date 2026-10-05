#!/usr/bin/env python3
"""Exercise the patched C handlers without opening devices or sending input."""
import pathlib
import subprocess
import sys
import tempfile

source = (pathlib.Path(sys.argv[1]) / "src/wl_input_uinput.c").read_text()
state = source[source.index("struct state_uinput {"):source.index("#define UINPUT_KEY_MAX")]
motion = source[source.index("static void mouse_rel_motion"):source.index("static void mouse_button")]
key = source[source.index("static void key("):source.index("static bool key_map")]
harness = r'''
#include <assert.h>
#include <stdbool.h>
#include <linux/input.h>
#define UINPUT_KEY_MAX 256
#define logErr(...) ((void)0)
struct wlContext { int width, height; };
struct wlInput { void *state; struct wlContext *wl_ctx; };
struct event { int type, code, value; } events[32];
static int count;
static void emit(int fd, int type, int code, int val) {
    (void)fd;
    assert(count < 32);
    events[count++] = (struct event){type, code, val};
}
'''
harness += state + motion + key + r'''
int main(void) {
    struct state_uinput state = {0};
    struct wlContext context = { 1920, 1080 };
    struct wlInput input = { &state, &context };
    mouse_motion(&input, 500, 300);
    assert(count == 0);
    mouse_motion(&input, 510, 295);
    assert(count == 3);
    assert(events[0].type == EV_REL && events[0].code == REL_X && events[0].value == 10);
    assert(events[1].type == EV_REL && events[1].code == REL_Y && events[1].value == -5);
    assert(events[2].type == EV_SYN && events[2].code == SYN_REPORT);
    count = 0;
    /* Reaching an edge pushes one screen past it so a drifted pointer
     * is clamped at the same edge Deskflow clamped its copy to. */
    mouse_motion(&input, 0, 295);
    assert(events[0].code == REL_X && events[0].value == -510 - 1920);
    assert(events[1].code == REL_Y && events[1].value == 0);
    mouse_motion(&input, 0, 1079);
    assert(events[3].code == REL_X && events[3].value == -1920);
    assert(events[4].code == REL_Y && events[4].value == 784 + 1080);
    mouse_motion(&input, 1919, 1078);
    assert(events[6].code == REL_X && events[6].value == 1919 + 1920);
    assert(events[7].code == REL_Y && events[7].value == -1);
    mouse_motion(&input, 1918, 1078);
    assert(events[9].code == REL_X && events[9].value == -1);
    assert(events[10].code == REL_Y && events[10].value == 0);
    count = 0;
    key(&input, KEY_A + 8, 1);
    key(&input, KEY_A + 8, 0);
    assert(count == 4 && events[0].code == KEY_A && events[0].value == 1);
    assert(events[2].code == KEY_A && events[2].value == 0);
    key(&input, 0, 1);
    key(&input, 1000, 1);
    assert(count == 4);
    return 0;
}
'''
assert "UI_SET_EVBIT, EV_ABS" not in source
with tempfile.TemporaryDirectory() as directory:
    test = pathlib.Path(directory) / "motion.c"
    binary = pathlib.Path(directory) / "motion-test"
    test.write_text(harness)
    subprocess.run(["cc", "-Wall", "-Wextra", "-Werror", str(test), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
print("Mouse delta, edge synchronization, key down/up and invalid-key tests passed; no input sent")
