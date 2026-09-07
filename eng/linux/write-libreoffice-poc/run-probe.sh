#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: run-probe.sh <source-document> [output-document]

Environment overrides:
  LO_INSTALL_PATH   LibreOffice installation root (default /usr/lib/libreoffice)
  CXX               C++ compiler (default c++)
  BUILD_DIR         disposable build directory (default under /tmp)
EOF
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
  usage >&2
  exit 64
fi

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "FAIL: this probe must run on Linux" >&2
  exit 65
fi

source_document="$1"
output_document="${2:-}"
lo_install="${LO_INSTALL_PATH:-/usr/lib/libreoffice}"
cxx="${CXX:-c++}"
build_dir="${BUILD_DIR:-/tmp/haven-write-lok-probe}"
profile_dir="$build_dir/profile"
tile_file="$build_dir/tile.rgba"
probe_binary="$build_dir/lok_probe"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

for path in "$source_document" "$lo_install"; do
  if [[ ! -e "$path" ]]; then
    echo "FAIL: required path does not exist: $path" >&2
    exit 66
  fi
done

if ! command -v "$cxx" >/dev/null 2>&1; then
  echo "FAIL: C++ compiler '$cxx' is not available" >&2
  exit 67
fi

if [[ ! -f /usr/include/LibreOfficeKit/LibreOfficeKit.h ]]; then
  echo "FAIL: LibreOfficeKit headers are missing; expected libreofficekit-dev to provide /usr/include/LibreOfficeKit/LibreOfficeKit.h" >&2
  exit 68
fi

if [[ ! -f "$lo_install/program/libsofficeapp.so" && ! -f "$lo_install/program/libmergedlo.so" ]]; then
  echo "FAIL: LibreOfficeKit runtime library not found below $lo_install/program" >&2
  exit 69
fi

mkdir -p "$build_dir"
rm -rf "$profile_dir"
mkdir -p "$profile_dir"

"$cxx" \
  -std=c++20 \
  -Wall -Wextra -Wpedantic -Werror \
  -I/usr/include \
  "$script_dir/lok_probe.cxx" \
  -ldl \
  -o "$probe_binary"

args=("$lo_install" "$profile_dir" "$source_document")
if [[ -n "$output_document" ]]; then
  args+=("$output_document")
else
  args+=("")
fi
args+=("$tile_file")

SAL_USE_VCLPLUGIN=svp \
  "$probe_binary" "${args[@]}"

if [[ ! -s "$tile_file" ]]; then
  echo "FAIL: probe did not produce a non-empty tile buffer" >&2
  exit 70
fi

expected_bytes=$((512 * 512 * 4))
actual_bytes="$(stat -c '%s' "$tile_file")"
if [[ "$actual_bytes" -ne "$expected_bytes" ]]; then
  echo "FAIL: tile buffer size was $actual_bytes bytes; expected $expected_bytes" >&2
  exit 71
fi

printf 'PASS: tile buffer %s bytes; profile %s\n' "$actual_bytes" "$profile_dir"
