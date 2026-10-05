#!/bin/sh
set -eu

source_file=${1:-src/uSynergy.c}

if [ ! -f "$source_file" ]; then
    printf 'Waynergy source was not found: %s\n' "$source_file" >&2
    exit 1
fi

# Waynergy can receive clipboard notifications before the Synergy/Barrier
# handshake. Preserve the source file's LF or CRLF line endings while making
# HelloBack the first packet sent to strict Deskflow servers.
if grep -q 'keep the update queued until then' "$source_file"; then
    printf '%s\n' 'Waynergy handshake fix is already applied.'
else
    if [ "$(grep -c 'memmove(buf, data, len);' "$source_file")" -ne 1 ]; then
        printf '%s\n' 'Waynergy clipboard function did not match the supported source.' >&2
        exit 1
    fi

    perl -0pi -e '
        $nl = index($_, "\r\n") >= 0 ? "\r\n" : "\n";
        $guard = join($nl,
            "\t/* Deskflow requires HelloBack to be the first client packet.  Clipboard",
            "\t * watchers can fire while the transport is connected but before the",
            "\t * protocol handshake finishes, so keep the update queued until then. */",
            "\tif (!context->m_hasReceivedHello)",
            "\t\treturn;",
            ""
        );
        s#\tmemmove\(buf, data, len\);\r?\n#"\tmemmove(buf, data, len);" . $nl . $guard#e
            or die "Waynergy handshake patch target not found\n";
    ' "$source_file"

    printf '%s\n' 'Waynergy handshake fix applied.'
fi

# Deskflow can advertise more than one clipboard format. Waynergy 0.0.17
# compares the first payload but forgets to advance over it, so a later local
# clipboard update (Ctrl+C/Ctrl+X) parses payload bytes as the next header.
if grep -q 'Validate and advance past this clipboard format payload' "$source_file"; then
    printf '%s\n' 'Waynergy multi-format clipboard fix is already applied.'
else
    perl -0pi -e '
        $nl = index($_, "\r\n") >= 0 ? "\r\n" : "\n";
        $fixed = join($nl,
            "\t\t/* Validate and advance past this clipboard format payload before",
            "\t\t * parsing the next format header. */",
            "\t\tif (buf.pos > buf.len || flen > buf.len - buf.pos) {",
            "\t\t\tlogErr(\"Clipboard payload parse error\");",
            "\t\t\treturn false;",
            "\t\t}",
            "\t\tif (flen == len) {",
            "\t\t\tif (!memcmp(data, buf.data + buf.pos, len)) {",
            "\t\t\t\treturn true;",
            "\t\t\t}",
            "\t\t}",
            "\t\tbuf.pos += flen;"
        );
        $compare = qr{\t\tif \(flen == len\) \{(?:\r?\n)+\t\t\tif \(!memcmp\(data, buf\.data \+ buf\.pos, len\)\) \{(?:\r?\n)+\t\t\t\treturn true;(?:\r?\n)+\t\t\t\}(?:\r?\n)+\t\t\}};
        if (/Advance past this clipboard format payload/) {
            s#$compare(?:\r?\n)+\t\t/\* Advance past this clipboard format payload before parsing the next(?:\r?\n)+\t\t \* format header\. \*/(?:\r?\n)+\t\tif \(buf\.pos > buf\.len \|\| flen > buf\.len - buf\.pos\) \{(?:\r?\n)+\t\t\tlogErr\("Clipboard payload parse error"\);(?:\r?\n)+\t\t\treturn false;(?:\r?\n)+\t\t\}(?:\r?\n)+\t\tbuf\.pos \+= flen;#$fixed#
                or die "Waynergy clipboard payload upgrade target not found\n";
        }
        else {
            s#$compare#$fixed# or die "Waynergy clipboard payload patch target not found\n";
        }
    ' "$source_file"

    printf '%s\n' 'Waynergy multi-format clipboard fix applied.'
fi
