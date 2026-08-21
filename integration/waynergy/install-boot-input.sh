#!/bin/sh
set -eu

service_name=moniswitch-waynergy-boot.service
service_user=moniswitch-input
service_group=moniswitch-input
service_root=/usr/local/libexec/moniswitch-waynergy
service_config=/etc/moniswitch-waynergy
unit_path="/etc/systemd/system/$service_name"
udev_rule=/etc/udev/rules.d/70-moniswitch-uinput.rules
module_file=/etc/modules-load.d/moniswitch-uinput.conf

if [ "$(id -u)" -ne 0 ]; then
    printf '%s\n' 'Run this installer through sudo.' >&2
    exit 1
fi

desktop_user=${1:-${SUDO_USER:-}}
waynergy_source=${2:-}
display_width=${3:-}
display_height=${4:-}
if [ -z "$desktop_user" ] || ! id "$desktop_user" >/dev/null 2>&1; then
    printf '%s\n' 'A valid desktop username is required.' >&2
    exit 1
fi
if [ -z "$waynergy_source" ] || [ ! -x "$waynergy_source" ]; then
    printf '%s\n' 'The existing Waynergy executable path is required.' >&2
    exit 1
fi

desktop_home=$(getent passwd "$desktop_user" | cut -d: -f6)
desktop_uid=$(id -u "$desktop_user")
source_config="$desktop_home/.config/waynergy"
script_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

for required in \
    "$source_config/config.ini" \
    "$script_root/moniswitch-waynergy-boot" \
    "$script_root/moniswitch-waynergy-boot.service" \
    "$script_root/moniswitch-wayland-stub.c"; do
    if [ ! -f "$required" ]; then
        printf 'Required file is missing: %s\n' "$required" >&2
        exit 1
    fi
done

tls_setting_matches()
{
    awk -v wanted_key="$2" -v wanted_value="$3" '
        BEGIN { section = ""; found = 0 }
        /^[[:space:]]*\[/ {
            section = tolower($0)
            gsub(/[[:space:]]/, "", section)
            next
        }
        section == "[tls]" {
            line = tolower($0)
            sub(/#.*/, "", line)
            gsub(/[[:space:]]/, "", line)
            if (line == wanted_key "=" wanted_value) {
                found = 1
            }
        }
        END { exit found ? 0 : 1 }
    ' "$1"
}

if ! tls_setting_matches "$source_config/config.ini" tofu false; then
    printf '%s\n' 'Refusing pre-login input without tls/tofu = false.' >&2
    exit 1
fi
if ! tls_setting_matches "$source_config/config.ini" enable true; then
    printf '%s\n' 'Refusing pre-login input without tls/enable = true.' >&2
    exit 1
fi

valid_dimension()
{
    case "$1" in
        ''|0|*[!0-9]*) return 1 ;;
        *) return 0 ;;
    esac
}

if ! valid_dimension "$display_width" || ! valid_dimension "$display_height"; then
    for connector_status in /sys/class/drm/card*-*/status; do
        [ -r "$connector_status" ] || continue
        [ "$(sed -n '1p' "$connector_status")" = connected ] || continue
        connector_modes=${connector_status%/status}/modes
        [ -r "$connector_modes" ] || continue
        IFS= read -r detected_mode < "$connector_modes" || true
        detected_width=${detected_mode%%x*}
        detected_height=${detected_mode#*x}
        if valid_dimension "$detected_width" && valid_dimension "$detected_height"; then
            display_width=$detected_width
            display_height=$detected_height
            break
        fi
    done
fi

if ! valid_dimension "$display_width" || ! valid_dimension "$display_height"; then
    display_width=1920
    display_height=1080
fi

if ! command -v cc >/dev/null 2>&1 || ! command -v pkg-config >/dev/null 2>&1 || \
   ! command -v setsid >/dev/null 2>&1 || \
   ! pkg-config --exists wayland-server; then
    printf '%s\n' \
        'A C compiler, pkg-config, setsid, and the Wayland server development files are required.' >&2
    exit 1
fi

pin_count=$(find "$source_config/tls/hash" -maxdepth 1 -type f 2>/dev/null | wc -l)
if [ "$pin_count" -lt 1 ]; then
    printf '%s\n' 'No pinned Deskflow server certificate was found.' >&2
    exit 1
fi

if ! getent group "$service_group" >/dev/null 2>&1; then
    groupadd --system "$service_group"
fi
if ! id "$service_user" >/dev/null 2>&1; then
    useradd \
        --system \
        --gid "$service_group" \
        --home-dir /var/lib/moniswitch-input \
        --shell /usr/bin/nologin \
        "$service_user"
fi

service_gid=$(getent group "$service_group" | cut -d: -f3)
service_account=$(getent passwd "$service_user")
account_home=$(printf '%s\n' "$service_account" | cut -d: -f6)
account_shell=$(printf '%s\n' "$service_account" | cut -d: -f7)
account_gid=$(printf '%s\n' "$service_account" | cut -d: -f4)
if [ "$account_home" != /var/lib/moniswitch-input ] || \
   [ "$account_shell" != /usr/bin/nologin ] || \
   [ "$account_gid" != "$service_gid" ]; then
    printf '%s\n' 'The moniswitch-input account name is already in use unexpectedly.' >&2
    exit 1
fi
if getent passwd | awk -F: -v gid="$service_gid" -v expected="$service_user" '
    $4 == gid && $1 != expected { found = 1 }
    END { exit found ? 0 : 1 }
'; then
    printf '%s\n' 'Another account already uses the moniswitch-input primary group.' >&2
    exit 1
fi
if getent group "$service_group" | awk -F: -v expected="$service_user" '
    {
        count = split($4, members, ",")
        for (member_index = 1; member_index <= count; member_index++) {
            if (members[member_index] != "" && members[member_index] != expected) {
                found = 1
            }
        }
    }
    END { exit found ? 0 : 1 }
'; then
    printf '%s\n' 'Another account is already a member of the moniswitch-input group.' >&2
    exit 1
fi

temporary=$(mktemp -d)
trap 'rm -rf -- "$temporary"' EXIT INT TERM

awk '
    BEGIN { section = "" }
    /^[[:space:]]*\[/ {
        section = tolower($0)
        gsub(/[[:space:]]/, "", section)
    }
    section == "[log]" && /^[[:space:]]*path[[:space:]]*=/ {
        print "path = /dev/null"
        next
    }
    { print }
' "$source_config/config.ini" > "$temporary/config.ini"

sed "s/@DESKTOP_UID@/$desktop_uid/g" \
    "$script_root/moniswitch-waynergy-boot.service" |
    sed "s/@WIDTH@/$display_width/g; s/@HEIGHT@/$display_height/g" \
        > "$temporary/$service_name"

if ! grep -Eq '^[[:space:]]*xkb_keymap[[:space:]]*\{' "$temporary/config.ini"; then
    printf '%s\n' \
        '' \
        'xkb_keymap {' \
        '    xkb_keycodes { include "evdev+aliases(qwerty)" };' \
        '    xkb_types { include "complete" };' \
        '    xkb_compat { include "complete" };' \
        '    xkb_symbols { include "pc+us+inet(evdev)" };' \
        '};' \
        >> "$temporary/config.ini"
fi

cc -std=c11 -O2 -Wall -Wextra -Werror \
    $(pkg-config --cflags wayland-server) \
    "$script_root/moniswitch-wayland-stub.c" \
    -o "$temporary/moniswitch-wayland-stub" \
    $(pkg-config --libs wayland-server)

install -d -o root -g root -m 0755 "$service_root"
install -d -o root -g root -m 0755 "$service_root/lib"
find "$service_root/lib" -mindepth 1 -maxdepth 1 -type f -delete
install -o root -g root -m 0755 "$waynergy_source" "$service_root/waynergy"
install -o root -g root -m 0755 \
    "$script_root/moniswitch-waynergy-boot" \
    "$service_root/moniswitch-waynergy-boot"
install -o root -g root -m 0755 \
    "$temporary/moniswitch-wayland-stub" \
    "$service_root/moniswitch-wayland-stub"

ldd "$waynergy_source" | awk -v prefix="$desktop_home/" '
    $2 == "=>" && index($3, prefix) == 1 { print $3 }
' | while IFS= read -r dependency; do
    [ -f "$dependency" ] || continue
    install -o root -g root -m 0644 "$dependency" "$service_root/lib/"
done

if LD_LIBRARY_PATH="$service_root/lib" ldd "$service_root/waynergy" | grep -q 'not found'; then
    printf '%s\n' 'The isolated Waynergy copy still has a missing library.' >&2
    exit 1
fi

install -d -o root -g "$service_group" -m 0750 "$service_config"
install -d -o root -g "$service_group" -m 0750 "$service_config/tls/hash"
install -o root -g "$service_group" -m 0640 "$temporary/config.ini" "$service_config/config.ini"

find "$service_config/tls/hash" -mindepth 1 -maxdepth 1 -type f -delete
for pin in "$source_config"/tls/hash/*; do
    [ -f "$pin" ] || continue
    install -o root -g "$service_group" -m 0640 "$pin" "$service_config/tls/hash/"
done

if ! tls_setting_matches "$service_config/config.ini" tofu false || \
   ! tls_setting_matches "$service_config/config.ini" enable true || \
   [ "$(find "$service_config/tls/hash" -maxdepth 1 -type f | wc -l)" -lt 1 ]; then
    printf '%s\n' 'The isolated TLS configuration failed verification.' >&2
    exit 1
fi

printf '%s\n' \
    'KERNEL=="uinput", SUBSYSTEM=="misc", GROUP:="moniswitch-input", MODE:="0660", OPTIONS+="static_node=uinput"' \
    > "$temporary/70-moniswitch-uinput.rules"
printf '%s\n' 'uinput' > "$temporary/moniswitch-uinput.conf"
install -o root -g root -m 0644 "$temporary/70-moniswitch-uinput.rules" "$udev_rule"
install -o root -g root -m 0644 "$temporary/moniswitch-uinput.conf" "$module_file"
install -o root -g root -m 0644 "$temporary/$service_name" "$unit_path"

modprobe uinput
udevadm control --reload-rules
udevadm trigger --subsystem-match=misc --action=add
udevadm settle
systemctl daemon-reload
systemctl enable "$service_name"
systemctl restart "$service_name"

sleep 2
if ! systemctl is-active --quiet "$service_name" || \
   ! pgrep -u "$service_user" -f "^$service_root/waynergy([[:space:]]|$)" >/dev/null 2>&1; then
    printf '%s\n' 'The pre-login receiver did not remain ready after startup.' >&2
    exit 1
fi

if ! runuser -u "$service_user" -- test -r /dev/uinput || \
   ! runuser -u "$service_user" -- test -w /dev/uinput; then
    printf '%s\n' 'The dedicated service account could not access /dev/uinput.' >&2
    exit 1
fi

printf '%s\n' 'Moniswitch pre-login input receiver installed.'
