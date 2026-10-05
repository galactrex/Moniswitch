#!/bin/sh
# Moniswitch Input Link receiver for macOS.
#
# Runs Deskflow's own client as a per-user launchd agent with an isolated
# configuration. The Windows server certificate is pinned; nothing is trusted
# on first use. Run as the Mac user who sits at the keyboard, not with sudo.
set -eu

label=com.galactrex.moniswitch.input
state_dir="$HOME/Library/Moniswitch/input"
settings_file="$state_dir/Deskflow.conf"
tls_dir="$state_dir/tls"
certificate="$tls_dir/deskflow.pem"
trusted_servers="$tls_dir/trusted-servers"
agent_dir="$HOME/Library/LaunchAgents"
agent="$agent_dir/$label.plist"

server=
pin=
screen_name=mac-pc
port=24800
deskflow_app=
mode=install
skip_launchctl=${MONISWITCH_SKIP_LAUNCHCTL:-0}

usage() {
    cat <<'USAGE'
Usage:
  install-input-client.sh --server WINDOWS_LAN_IP --pin SHA256:PIN [options]
  install-input-client.sh --uninstall [--purge]

Options:
  --server HOST     Windows PC address on the local network.
  --pin PIN         Value from Moniswitch > Input Link > COPY SERVER PIN.
  --name NAME       Mac screen name. Must match "Mac name" in Moniswitch.
                    Default: mac-pc
  --port PORT       Deskflow port. Default: 24800
  --deskflow PATH   Deskflow.app location. Default: /Applications/Deskflow.app
                    or ~/Applications/Deskflow.app
  --uninstall       Stop and remove the launchd agent.
  --purge           With --uninstall, also delete the receiver configuration
                    and its TLS identity.
USAGE
}

fail() {
    printf 'moniswitch: %s\n' "$1" >&2
    exit 1
}

purge=0
while [ $# -gt 0 ]; do
    case "$1" in
        --server) [ $# -ge 2 ] || fail "--server needs a value"; server=$2; shift 2 ;;
        --pin) [ $# -ge 2 ] || fail "--pin needs a value"; pin=$2; shift 2 ;;
        --name) [ $# -ge 2 ] || fail "--name needs a value"; screen_name=$2; shift 2 ;;
        --port) [ $# -ge 2 ] || fail "--port needs a value"; port=$2; shift 2 ;;
        --deskflow) [ $# -ge 2 ] || fail "--deskflow needs a value"; deskflow_app=$2; shift 2 ;;
        --uninstall) mode=uninstall; shift ;;
        --purge) purge=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; fail "unknown option: $1" ;;
    esac
done

if [ "$(id -u)" -eq 0 ]; then
    fail "run this as the Mac desktop user, not with sudo"
fi

uid=$(id -u)

stop_agent() {
    if [ "$skip_launchctl" = 1 ]; then
        return 0
    fi
    launchctl bootout "gui/$uid/$label" >/dev/null 2>&1 || true
}

if [ "$mode" = uninstall ]; then
    stop_agent
    rm -f "$agent"
    if [ "$purge" = 1 ]; then
        rm -rf "$state_dir"
    fi
    printf '%s\n' 'Moniswitch input receiver removed.'
    exit 0
fi

[ -n "$server" ] || { usage >&2; fail "--server is required"; }
[ -n "$pin" ] || { usage >&2; fail "--pin is required"; }

# Hostnames, IPv4 and IPv6 literals only. Anything else could change the
# meaning of the generated INI or plist.
case "$server" in
    *[!A-Za-z0-9.:-]*|'') fail "--server must be a hostname or IP address" ;;
esac
case "$port" in
    *[!0-9]*|'') fail "--port must be a number" ;;
esac
if [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
    fail "--port must be between 1 and 65535"
fi

# Match Moniswitch's own screen-name sanitizer so both sides agree.
screen_name=$(printf '%s' "$screen_name" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/[^A-Za-z0-9._-]/-/g')
[ -n "$screen_name" ] || screen_name=mac-pc

# Moniswitch copies SHA256:<64 hex>. Also accept bare or colon-separated hex.
fingerprint=$(printf '%s' "$pin" | sed -e 's/^[Ss][Hh][Aa]256://' -e 's/://g' | tr 'A-F' 'a-f')
case "$fingerprint" in
    *[!0-9a-f]*) fail "--pin is not a SHA-256 fingerprint" ;;
esac
[ "${#fingerprint}" -eq 64 ] || fail "--pin is not a SHA-256 fingerprint"

if [ -z "$deskflow_app" ]; then
    for candidate in /Applications/Deskflow.app "$HOME/Applications/Deskflow.app"; do
        if [ -d "$candidate" ]; then
            deskflow_app=$candidate
            break
        fi
    done
fi
[ -n "$deskflow_app" ] || fail "Deskflow.app was not found. Install Deskflow 1.26 or newer, or pass --deskflow"
core="${deskflow_app%/}/Contents/MacOS/deskflow-core"
[ -x "$core" ] || fail "deskflow-core was not found inside $deskflow_app"

# Deskflow reads its settings with QSettings, which treats commas and quotes
# specially. Refuse paths that would need escaping rather than guess.
case "$state_dir$core" in
    *[\",\;]*) fail "home or Deskflow path contains a comma, quote or semicolon" ;;
esac

umask 077
mkdir -p "$tls_dir" "$agent_dir"
chmod 700 "$state_dir" "$tls_dir"

# The client needs its own TLS identity even though Moniswitch does not ask
# for it. It is created once and never leaves this Mac.
if [ ! -s "$certificate" ]; then
    command -v openssl >/dev/null 2>&1 || fail "openssl is required to create the TLS identity"
    key_tmp="$tls_dir/.key.$$"
    cert_tmp="$tls_dir/.cert.$$"
    trap 'rm -f "$key_tmp" "$cert_tmp"' EXIT
    openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
        -subj '/CN=Moniswitch Mac' \
        -keyout "$key_tmp" -out "$cert_tmp" >/dev/null 2>&1 \
        || fail "openssl could not create the TLS identity"
    cat "$key_tmp" "$cert_tmp" > "$certificate"
    rm -f "$key_tmp" "$cert_tmp"
    trap - EXIT
    chmod 600 "$certificate"
fi

printf 'v2:sha256:%s\n' "$fingerprint" > "$trusted_servers"
chmod 600 "$trusted_servers"

cat > "$settings_file" <<EOF
[client]
remoteHost=$server

[core]
computerName=$screen_name
coreMode=1
port=$port
processMode=1
startedBefore=true

[gui]
enableUpdateCheck=false

[log]
level=WARNING
toFile=false

[security]
certificate=$certificate
checkPeerFingerprints=true
keySize=3072
tlsEnabled=true
EOF
chmod 600 "$settings_file"

xml_escape() {
    printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
}

core_xml=$(xml_escape "$core")
settings_xml=$(xml_escape "$settings_file")

# KeepAlive restarts the client if it exits; Deskflow itself keeps retrying
# while Windows is asleep or rebooting. Output is discarded so no addresses or
# key events land in a log file.
cat > "$agent" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$label</string>
    <key>ProgramArguments</key>
    <array>
        <string>$core_xml</string>
        <string>client</string>
        <string>--settings</string>
        <string>$settings_xml</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>ThrottleInterval</key>
    <integer>5</integer>
    <key>ProcessType</key>
    <string>Interactive</string>
    <key>LimitLoadToSessionType</key>
    <string>Aqua</string>
    <key>StandardOutPath</key>
    <string>/dev/null</string>
    <key>StandardErrorPath</key>
    <string>/dev/null</string>
</dict>
</plist>
EOF
chmod 644 "$agent"

if [ "$skip_launchctl" != 1 ]; then
    if command -v plutil >/dev/null 2>&1; then
        plutil -lint "$agent" >/dev/null || fail "generated launchd agent is invalid"
    fi
    stop_agent
    launchctl bootstrap "gui/$uid" "$agent"
fi

cat <<EOF
Moniswitch input receiver installed as "$screen_name".

One permission is still needed. macOS will not let any program move the
pointer or type until it is allowed under:

  System Settings > Privacy & Security > Accessibility

Add and enable:
  $core

(In the file picker press Cmd+Shift+G and paste that path.)
Then restart the receiver:

  launchctl kickstart -k gui/$uid/$label

Quit the Deskflow app itself if it is open. This receiver replaces it.
EOF

if [ "$skip_launchctl" != 1 ]; then
    open 'x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility' >/dev/null 2>&1 || true
fi
