#!/bin/sh
# Upgrade an already installed, patched Moniswitch pre-login receiver.
set -eu
test "$(id -u)" = 0 || { echo 'Run through sudo.'; exit 1; }
desktop_user=${SUDO_USER:?}
desktop_uid=$(id -u "$desktop_user")
desktop_home=$(getent passwd "$desktop_user" | cut -d: -f6)
script_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
unit=moniswitch-waynergy-boot.service
root=/usr/local/libexec/moniswitch-waynergy
dropin_dir=/etc/systemd/system/$unit.d
dropin=$dropin_dir/50-all-sessions.conf
user_dropin_dir=$desktop_home/.config/systemd/user/moniswitch-waynergy.service.d
user_dropin=$user_dropin_dir/50-system-input.conf
backup=/var/lib/moniswitch-input/system-input-backup

userctl()
{
    runuser -u "$desktop_user" -- env \
        XDG_RUNTIME_DIR="/run/user/$desktop_uid" \
        DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$desktop_uid/bus" \
        systemctl --user "$@"
}

test -x "$root/waynergy"
test -x "$root/moniswitch-wayland-stub"
test -f "$root/moniswitch-waynergy-boot"
test -S "/run/user/$desktop_uid/bus"
test -f "$script_root/moniswitch-waynergy-boot"
sh -n "$script_root/moniswitch-waynergy-boot"
runuser -u moniswitch-input -- test -w /dev/uinput

# Preserve the original supervisor and unit fragments once for rollback.
if ! test -d "$backup"; then
    install -d -o root -g root -m 0700 "$backup"
    cp -p "$root/moniswitch-waynergy-boot" "$backup/supervisor"
    if test -f "$dropin"; then cp -p "$dropin" "$backup/system-dropin"; fi
    if test -f "$user_dropin"; then cp -p "$user_dropin" "$backup/user-dropin"; fi
    if userctl is-active --quiet moniswitch-waynergy.service; then touch "$backup/user-was-active"; fi
fi

restore()
{
    systemctl stop "$unit" || true
    cp -p "$backup/supervisor" "$root/moniswitch-waynergy-boot"
    if test -f "$backup/system-dropin"; then
        cp -p "$backup/system-dropin" "$dropin"
    else
        rm -f -- "$dropin"
    fi
    if test -f "$backup/user-dropin"; then
        cp -p "$backup/user-dropin" "$user_dropin"
    else
        rm -f -- "$user_dropin"
    fi
    systemctl daemon-reload
    userctl daemon-reload
    systemctl start "$unit"
    if test -f "$backup/user-was-active"; then userctl start moniswitch-waynergy.service; fi
}

if [ "${1:-}" = --restore ]; then
    restore
    echo 'Previous receiver setup restored.'
    exit 0
fi

trap 'trap - EXIT HUP INT TERM; restore; exit 1' HUP INT TERM
trap 'code=$?; if [ "$code" -ne 0 ]; then restore; fi' EXIT
install -d -o root -g root -m 0755 "$dropin_dir"
runuser -u "$desktop_user" -- mkdir -p "$user_dropin_dir"
printf '%s\n' '[Unit]' \
    "ConditionPathExists=!$dropin" | \
    runuser -u "$desktop_user" -- tee "$user_dropin" >/dev/null
printf '%s\n' '[Service]' 'Environment=MONISWITCH_ALL_SESSIONS=1' > "$dropin"
chmod 0644 "$dropin"
userctl daemon-reload
userctl stop moniswitch-waynergy.service
systemctl stop "$unit"
install -o root -g root -m 0755 "$script_root/moniswitch-waynergy-boot" "$root/moniswitch-waynergy-boot"
systemctl daemon-reload
systemctl start "$unit"
sleep 3
systemctl is-active --quiet "$unit"
pgrep -u moniswitch-input -f "^$root/waynergy([[:space:]]|$)" >/dev/null
if userctl is-active --quiet moniswitch-waynergy.service; then
    echo 'Desktop receiver still running; restoring the previous setup.' >&2
    exit 1
fi
trap - EXIT HUP INT TERM
echo 'System input enabled: one receiver for login and desktop sessions.'
