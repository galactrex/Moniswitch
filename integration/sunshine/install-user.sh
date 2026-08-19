#!/bin/sh
set -eu

version="2026.516.143833"
expected_sha256="d0ee0a9cfb66f27869b559455f84622d21615047ccf3443c9a2f572ca971c7a2"
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
install_dir="$HOME/.local/opt/moniswitch-sunshine-appimage"
app_dir="$install_dir/squashfs-root"
config_dir="$HOME/.config/Moniswitch/sunshine"
state_dir="$HOME/.local/state/Moniswitch"
service_dir="$HOME/.config/systemd/user"
helper_dir="$HOME/.local/bin"
appimage="$install_dir/sunshine.AppImage"

mkdir -p "$install_dir" "$config_dir/credentials" "$state_dir" "$service_dir" "$helper_dir"

if [ ! -x "$app_dir/usr/bin/sunshine" ]; then
    curl --location --fail --silent --show-error \
        --output "$appimage" \
        "https://github.com/LizardByte/Sunshine/releases/download/v$version/sunshine.AppImage"
    actual_sha256=$(sha256sum "$appimage" | awk '{print $1}')
    [ "$actual_sha256" = "$expected_sha256" ] || {
        echo "Sunshine checksum mismatch." >&2
        exit 1
    }
    chmod 755 "$appimage"
    (cd "$install_dir" && "$appimage" --appimage-extract >/dev/null)
fi

sed "s|@HOME@|$HOME|g" "$script_dir/sunshine.conf.example" > "$config_dir/sunshine.conf"
cp "$script_dir/apps.json" "$config_dir/apps.json"
cp "$script_dir/moniswitch-sunshine.service" "$service_dir/moniswitch-sunshine.service"
cp "$script_dir/moniswitch-canvas" "$helper_dir/moniswitch-canvas"
chmod 755 "$helper_dir/moniswitch-canvas"

password_file="$state_dir/sunshine-password"
if [ ! -s "$password_file" ]; then
    umask 077
    openssl rand -hex 24 > "$password_file"
fi

password=$(cat "$password_file")
# Sunshine loads the configuration path before dispatching CLI commands. Keep
# it ahead of --creds so the generated state lands in Moniswitch's private
# configuration directory instead of Sunshine's default location.
(cd "$app_dir" && ./usr/bin/sunshine "$config_dir/sunshine.conf" --creds moniswitch "$password")
systemctl --user daemon-reload

echo "Moniswitch LAN Canvas sender installed."
