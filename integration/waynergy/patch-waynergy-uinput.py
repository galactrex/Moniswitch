#!/usr/bin/env python3
"""Make Waynergy 0.0.17's uinput mouse usable by Xorg/libinput greeters."""
import pathlib
import sys

path = pathlib.Path(sys.argv[1]) / "src" / "wl_input_uinput.c"
original = path.read_bytes()
text = path.read_text()
relative_marker = "/* Moniswitch: relative-only uinput mouse. */"
edge_marker = "/* Moniswitch: keep screen edges in sync. */"
applied = []

def replace(old, new):
    global text
    if text.count(old) != 1:
        raise SystemExit("Unexpected Waynergy source; no changes written")
    text = text.replace(old, new, 1)

if relative_marker not in text:
    replace("\tint mouse_fd;", "\tint mouse_fd;\n\tint last_x, last_y;\n\tbool position_known;")
    replace("\temit(ui->mouse_fd, EV_ABS, ABS_X, x);\n\temit(ui->mouse_fd, EV_ABS, ABS_Y, y);\n\temit(ui->mouse_fd, EV_SYN, SYN_REPORT, 0);",f"""\t{relative_marker}
\tif (ui->position_known) {{
\t\tmouse_rel_motion(input, x - ui->last_x, y - ui->last_y);
\t}}
\tui->last_x = x;
\tui->last_y = y;
\tui->position_known = true;""")
    start = text.index("\tstruct uinput_abs_setup x = {")
    end = text.index("\n\tTRY_IOCTL(ui->mouse_fd, UI_SET_EVBIT, EV_SYN);", start)
    text = text[:start] + "\tui->position_known = false;\n" + text[end:]
    for line in (
        "\tTRY_IOCTL(ui->mouse_fd, UI_SET_EVBIT, EV_ABS);\n",
        "\tTRY_IOCTL(ui->mouse_fd, UI_SET_ABSBIT, ABS_X);\n",
        "\tTRY_IOCTL(ui->mouse_fd, UI_SET_ABSBIT, ABS_Y);\n",
        "\tTRY_IOCTL(ui->mouse_fd, UI_ABS_SETUP, &x);\n",
        "\tTRY_IOCTL(ui->mouse_fd, UI_ABS_SETUP, &y);\n",
    ):
        replace(line, "")
    replace("if (code > UINPUT_KEY_MAX)", "if (code < 0 || code > UINPUT_KEY_MAX)")
    applied.append("relative mouse movement and keycode bounds")

if edge_marker not in text:
    # Deskflow clamps its copy of the pointer to [0, size - 1] and stops
    # sending motion once that copy is clamped. If the real pointer drifted,
    # for example when the display reconnects during a monitor input switch,
    # the copy reaches an edge first and the real pointer stops short of it:
    # an invisible wall. Pushing one screen past an edge makes the compositor
    # clamp the real pointer at the same edge, so the two agree again.
    replace("static void mouse_motion(struct wlInput *input, int x, int y)\n", f"""{edge_marker}
static int edge_delta(int target, int last, int size)
{{
\tint delta = target - last;

\tif (size <= 1)
\t\treturn delta;
\tif (target <= 0)
\t\treturn delta - size;
\tif (target >= size - 1)
\t\treturn delta + size;
\treturn delta;
}}

static void mouse_motion(struct wlInput *input, int x, int y)
""")
    replace("\t\tmouse_rel_motion(input, x - ui->last_x, y - ui->last_y);",
            "\t\tmouse_rel_motion(input,\n"
            "\t\t\tedge_delta(x, ui->last_x, input->wl_ctx->width),\n"
            "\t\t\tedge_delta(y, ui->last_y, input->wl_ctx->height));")
    applied.append("screen-edge synchronization")

if not applied:
    print("uinput mouse patch already applied")
    sys.exit(0)
backup = path.with_suffix(path.suffix + ".before-moniswitch-uinput")
if not backup.exists():
    backup.write_bytes(original)
path.write_text(text)
print("Patched uinput " + " and ".join(applied))
