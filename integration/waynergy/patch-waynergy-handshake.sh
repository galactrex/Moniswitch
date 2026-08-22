#!/bin/sh
set -eu

source_file=${1:-src/uSynergy.c}

if [ ! -f "$source_file" ]; then
    printf 'Waynergy source was not found: %s\n' "$source_file" >&2
    exit 1
fi

if grep -q 'keep the update queued until then' "$source_file"; then
    printf '%s\n' 'Waynergy handshake fix is already applied.'
    exit 0
fi

if [ "$(grep -c 'memmove(buf, data, len);' "$source_file")" -ne 1 ]; then
    printf '%s\n' 'Waynergy clipboard function did not match the supported source.' >&2
    exit 1
fi

# Waynergy can receive clipboard notifications before the Synergy/Barrier
# handshake. Preserve the source file's LF or CRLF line endings while making
# HelloBack the first packet sent to strict Deskflow servers.
perl -0pi -e '
    s#(\tmemmove\(buf, data, len\);)(\r?\n)#$1$2
\t/* Deskflow requires HelloBack to be the first client packet.  Clipboard$2
\t * watchers can fire while the transport is connected but before the$2
\t * protocol handshake finishes, so keep the update queued until then. */$2
\tif (!context->m_hasReceivedHello)$2
\t\treturn;$2$2# or die "Waynergy handshake patch target not found\n"
' "$source_file"

printf '%s\n' 'Waynergy handshake fix applied.'
