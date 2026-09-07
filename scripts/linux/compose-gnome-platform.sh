#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat >&2 <<'USAGE'
Usage: compose-gnome-platform.sh <ubuntu-rootfs> <overlay-output>

Builds a non-boot-critical HavenOS GNOME integration overlay after verifying
Ubuntu and GNOME session evidence in the supplied root filesystem. The input
rootfs is read-only; the script never edits GDM, systemd boot units, initramfs,
or GNOME Shell source.

Optional environment variables:
  HAVEN_GNOME_SOURCE      Path to an explicitly supplied GNOME Shell source tree.
  HAVEN_GNOME_SOURCE_SHA  Provenance SHA for that source tree (40-64 hex chars).
USAGE
    exit 2
}

[[ $# -eq 2 ]] || usage
[[ -d "$1" ]] || { echo "Ubuntu rootfs is missing: $1" >&2; exit 1; }
rootfs=$(realpath "$1")
overlay=$(realpath -m "$2")

[[ "$rootfs" != "/" ]] || { echo "Refusing to treat the live host root as an image rootfs." >&2; exit 1; }
os_release="$rootfs/etc/os-release"
[[ -f "$os_release" ]] || { echo "Ubuntu provenance check failed: /etc/os-release is missing." >&2; exit 1; }

read_os_release_value() {
    local key=$1 line value
    line=$(grep -m1 -E "^${key}=" "$os_release" || true)
    [[ -n "$line" ]] || return 1
    value=${line#*=}
    if [[ "$value" == \"*\" && "$value" == *\" ]]; then
        value=${value:1:${#value}-2}
    elif [[ "$value" == \'*\' && "$value" == *\' ]]; then
        value=${value:1:${#value}-2}
    fi
    printf '%s' "$value"
}

base_id=$(read_os_release_value ID || true)
base_version=$(read_os_release_value VERSION_ID || true)
[[ "$base_id" == "ubuntu" ]] || { echo "Unsupported base image ID '${base_id:-unknown}'; expected ubuntu." >&2; exit 1; }
[[ "$base_version" == 26.04* ]] || { echo "Unsupported Ubuntu VERSION_ID '${base_version:-unknown}'; expected 26.04.x." >&2; exit 1; }

session_evidence=""
for candidate in \
    usr/bin/gnome-shell \
    usr/share/gnome-session/sessions/gnome.session \
    usr/share/wayland-sessions/ubuntu.desktop \
    usr/share/xsessions/ubuntu.desktop; do
    if [[ -e "$rootfs/$candidate" ]]; then
        session_evidence="/$candidate"
        break
    fi
done
[[ -n "$session_evidence" ]] || { echo "GNOME provenance check failed: no stock GNOME session evidence was found." >&2; exit 1; }

if [[ -e "$overlay" ]]; then
    [[ -d "$overlay" ]] || { echo "Overlay output exists and is not a directory: $overlay" >&2; exit 1; }
    if find "$overlay" -mindepth 1 -print -quit | grep -q .; then
        echo "Refusing to reuse a non-empty overlay output: $overlay" >&2
        exit 1
    fi
fi

gnome_source_status="not-supplied"
gnome_source_sha="none"
if [[ -n "${HAVEN_GNOME_SOURCE:-}" || -n "${HAVEN_GNOME_SOURCE_SHA:-}" ]]; then
    [[ -n "${HAVEN_GNOME_SOURCE:-}" && -n "${HAVEN_GNOME_SOURCE_SHA:-}" ]] || {
        echo "GNOME source provenance is incomplete; set both HAVEN_GNOME_SOURCE and HAVEN_GNOME_SOURCE_SHA." >&2
        exit 1
    }
    [[ -d "$HAVEN_GNOME_SOURCE" ]] || { echo "GNOME source tree is missing: $HAVEN_GNOME_SOURCE" >&2; exit 1; }
    gnome_source=$(realpath "$HAVEN_GNOME_SOURCE")
    [[ -f "$gnome_source/meson.build" && -f "$gnome_source/js/ui/main.js" ]] || {
        echo "GNOME source tree does not contain the expected GNOME Shell source markers." >&2
        exit 1
    }
    [[ "$HAVEN_GNOME_SOURCE_SHA" =~ ^[0-9a-fA-F]{40,64}$ ]] || {
        echo "HAVEN_GNOME_SOURCE_SHA must be a 40-64 character hexadecimal provenance SHA." >&2
        exit 1
    }
    gnome_source_status="validated-explicit"
    gnome_source_sha="${HAVEN_GNOME_SOURCE_SHA,,}"
fi

mkdir -p "$overlay"
platform_dir="$overlay/usr/lib/haven/platform"
mkdir -p "$platform_dir"

cat > "$platform_dir/platform.env" <<EOF_ENV
HAVEN_PLATFORM_BASE_ID=ubuntu
HAVEN_PLATFORM_BASE_VERSION=${base_version}
HAVEN_PLATFORM_SESSION=gnome
HAVEN_PLATFORM_SESSION_EVIDENCE=${session_evidence}
HAVEN_GNOME_SOURCE_STATUS=${gnome_source_status}
HAVEN_GNOME_SOURCE_SHA=${gnome_source_sha}
HAVEN_INTEGRATION_MODE=optional-overlay
HAVEN_BOOT_CRITICAL=false
EOF_ENV

cat > "$platform_dir/README" <<'EOF_README'
This directory is a HavenOS platform capability seam, not a replacement for
Ubuntu boot, GDM, or the stock GNOME session. Image composition may copy this
overlay into an Ubuntu/GNOME root filesystem after the base image is built.
Nothing here is enabled at boot. A GNOME fork is not asserted by this overlay.
Explicit source evidence is recorded only when a source tree and provenance SHA
were supplied and validated.
EOF_README

for forbidden in \
    etc/gdm3 \
    etc/systemd/system \
    usr/lib/systemd/system \
    usr/share/gnome-shell/extensions \
    boot; do
    if [[ -e "$overlay/$forbidden" ]]; then
        echo "Composition boundary violation: overlay unexpectedly contains /$forbidden" >&2
        exit 1
    fi
done

echo "Prepared optional HavenOS GNOME platform overlay at $overlay"
echo "Base: ubuntu ${base_version}; session evidence: $session_evidence"
echo "GNOME source status: $gnome_source_status"
