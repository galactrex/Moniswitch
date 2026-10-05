#!/bin/sh
set -eu
test "$(id -u)" = 0 || { echo 'Run through sudo.'; exit 1; }
desktop_home=$(getent passwd "${SUDO_USER:?}" | cut -d: -f6)
source_binary="$desktop_home/.local/src/moniswitch-waynergy/build/waynergy"
target=/usr/local/libexec/moniswitch-waynergy/waynergy
backup=/usr/local/libexec/moniswitch-waynergy/waynergy.before-relative-mouse
test -x "$source_binary"
test -x "$target"
if ! test -e "$backup"; then cp -p "$target" "$backup"; fi
systemctl stop moniswitch-waynergy-boot.service
install -o root -g root -m 0755 "$source_binary" "$target"
if LD_LIBRARY_PATH=/usr/local/libexec/moniswitch-waynergy/lib ldd "$target" | grep -q 'not found'; then
    cp -p "$backup" "$target"
    systemctl start moniswitch-waynergy-boot.service
    echo 'Missing dependency; restored the previous binary.'
    exit 1
fi
systemctl start moniswitch-waynergy-boot.service
sleep 2
systemctl is-active --quiet moniswitch-waynergy-boot.service
echo 'Pre-login receiver updated. Windows sharing remains stopped.'
