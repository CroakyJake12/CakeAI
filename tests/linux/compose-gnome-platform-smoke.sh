#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
compose="$repo_root/scripts/linux/compose-gnome-platform.sh"

[[ -x "$compose" ]] || { echo "Compose script is not executable: $compose" >&2; exit 1; }

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

make_rootfs() {
    local root=$1
    local id=${2:-ubuntu}
    local version=${3:-26.04}
    mkdir -p "$root/etc" "$root/usr/bin"
    cat > "$root/etc/os-release" <<EOF_OS
ID=$id
VERSION_ID="$version"
EOF_OS
    : > "$root/usr/bin/gnome-shell"
}

valid_root="$tmp/valid-root"
make_rootfs "$valid_root"
"$compose" "$valid_root" "$tmp/overlay"

grep -qx 'HAVEN_PLATFORM_BASE_ID=ubuntu' "$tmp/overlay/usr/lib/haven/platform/platform.env"
grep -qx 'HAVEN_PLATFORM_SESSION=gnome' "$tmp/overlay/usr/lib/haven/platform/platform.env"
grep -qx 'HAVEN_GNOME_SOURCE_STATUS=not-supplied' "$tmp/overlay/usr/lib/haven/platform/platform.env"
grep -qx 'HAVEN_BOOT_CRITICAL=false' "$tmp/overlay/usr/lib/haven/platform/platform.env"
[[ ! -e "$tmp/overlay/etc/gdm3" ]]
[[ ! -e "$tmp/overlay/etc/systemd/system" ]]
[[ ! -e "$tmp/overlay/usr/share/gnome-shell/extensions" ]]

if "$compose" "$valid_root" "$tmp/overlay" >/dev/null 2>&1; then
    echo "Expected non-empty overlay reuse to fail closed." >&2
    exit 1
fi

non_ubuntu="$tmp/non-ubuntu"
make_rootfs "$non_ubuntu" debian 13
if "$compose" "$non_ubuntu" "$tmp/non-ubuntu-overlay" >/dev/null 2>&1; then
    echo "Expected non-Ubuntu rootfs to be rejected." >&2
    exit 1
fi

malicious_root="$tmp/malicious-root"
mkdir -p "$malicious_root/etc" "$malicious_root/usr/bin"
cat > "$malicious_root/etc/os-release" <<EOF_OS
ID=\$(touch "$tmp/os-release-executed")
VERSION_ID="26.04"
EOF_OS
: > "$malicious_root/usr/bin/gnome-shell"
if "$compose" "$malicious_root" "$tmp/malicious-overlay" >/dev/null 2>&1; then
    echo "Expected executable os-release content to be rejected as data." >&2
    exit 1
fi
[[ ! -e "$tmp/os-release-executed" ]] || { echo "os-release content was executed." >&2; exit 1; }

no_gnome="$tmp/no-gnome"
mkdir -p "$no_gnome/etc"
cat > "$no_gnome/etc/os-release" <<'EOF_OS'
ID=ubuntu
VERSION_ID="26.04"
EOF_OS
if "$compose" "$no_gnome" "$tmp/no-gnome-overlay" >/dev/null 2>&1; then
    echo "Expected Ubuntu rootfs without GNOME evidence to be rejected." >&2
    exit 1
fi

fake_source="$tmp/fake-gnome-source"
mkdir -p "$fake_source"
if HAVEN_GNOME_SOURCE="$fake_source" HAVEN_GNOME_SOURCE_SHA="0123456789012345678901234567890123456789" \
    "$compose" "$valid_root" "$tmp/fake-source-overlay" >/dev/null 2>&1; then
    echo "Expected unverified GNOME source claim to be rejected." >&2
    exit 1
fi

verified_source="$tmp/verified-gnome-source"
mkdir -p "$verified_source/js/ui"
: > "$verified_source/meson.build"
: > "$verified_source/js/ui/main.js"
HAVEN_GNOME_SOURCE="$verified_source" HAVEN_GNOME_SOURCE_SHA="0123456789012345678901234567890123456789" \
    "$compose" "$valid_root" "$tmp/verified-source-overlay"
grep -qx 'HAVEN_GNOME_SOURCE_STATUS=validated-explicit' "$tmp/verified-source-overlay/usr/lib/haven/platform/platform.env"
grep -qx 'HAVEN_GNOME_SOURCE_SHA=0123456789012345678901234567890123456789' "$tmp/verified-source-overlay/usr/lib/haven/platform/platform.env"

echo "compose-gnome-platform smoke tests passed"
