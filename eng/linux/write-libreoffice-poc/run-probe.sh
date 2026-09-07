#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: run-probe.sh <source-document> [output-document]

If output-document is omitted, the probe writes a disposable round-trip ODT
inside BUILD_DIR so edit persistence and reopen are still exercised.

Environment overrides:
  LO_PROGRAM_PATH   LibreOffice program directory (default /usr/lib/libreoffice/program)
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
lo_program="${LO_PROGRAM_PATH:-/usr/lib/libreoffice/program}"
cxx="${CXX:-c++}"
build_dir="${BUILD_DIR:-/tmp/haven-write-lok-probe}"
output_document="${2:-$build_dir/roundtrip.odt}"
profile_dir="$build_dir/profile"
tile_file="$build_dir/tile.rgba"
probe_binary="$build_dir/lok_probe"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

for path in "$source_document" "$lo_program"; do
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

if [[ ! -f "$lo_program/libsofficeapp.so" && ! -f "$lo_program/libmergedlo.so" ]]; then
  echo "FAIL: LibreOfficeKit runtime library not found in $lo_program" >&2
  exit 69
fi

mkdir -p "$build_dir"
rm -rf "$profile_dir"
mkdir -p "$profile_dir"
rm -f "$tile_file" "$output_document"

"$cxx" \
  -std=c++20 \
  -Wall -Wextra -Wpedantic -Werror \
  -I/usr/include \
  "$script_dir/lok_probe.cxx" \
  -ldl \
  -o "$probe_binary"

SAL_USE_VCLPLUGIN=svp \
  "$probe_binary" \
  "$lo_program" \
  "$profile_dir" \
  "$source_document" \
  "$output_document" \
  "$tile_file"

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

if [[ ! -s "$output_document" ]]; then
  echo "FAIL: probe did not produce a non-empty round-trip document" >&2
  exit 72
fi

printf 'PASS: tile buffer %s bytes; round-trip %s; profile %s\n' \
  "$actual_bytes" "$output_document" "$profile_dir"
