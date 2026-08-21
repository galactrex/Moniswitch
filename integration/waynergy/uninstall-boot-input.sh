#!/bin/sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
    printf '%s\n' 'Run this uninstaller through sudo.' >&2
    exit 1
fi

service_name=moniswitch-waynergy-boot.service
systemctl disable --now "$service_name" 2>/dev/null || true

rm -f -- \
    "/etc/systemd/system/$service_name" \
    /etc/udev/rules.d/70-moniswitch-uinput.rules \
    /etc/modules-load.d/moniswitch-uinput.conf
rm -rf -- /etc/moniswitch-waynergy /usr/local/libexec/moniswitch-waynergy

systemctl daemon-reload
udevadm control --reload-rules
udevadm trigger --subsystem-match=misc --action=add
udevadm settle

if id moniswitch-input >/dev/null 2>&1; then
    userdel moniswitch-input
fi
if getent group moniswitch-input >/dev/null 2>&1; then
    groupdel moniswitch-input
fi

printf '%s\n' 'Moniswitch pre-login input receiver removed.'
