#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: run-semantic-probe.sh <source-document> [output-document]

The probe uses LibreOfficeKit's public unipoll runLoop and
LOK_CALLBACK_UNO_COMMAND_RESULT path to prove SelectAll and Bold completion.

Environment overrides:
  LO_PROGRAM_PATH         LibreOffice program directory (default /usr/lib/libreoffice/program)
  CXX                     C++ compiler (default c++)
  BUILD_DIR               disposable build directory (default under /tmp)
  SEMANTIC_PROBE_TIMEOUT  outer watchdog duration for coreutils timeout (default 30s)
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
build_dir="${BUILD_DIR:-/tmp/haven-write-lok-semantic-probe}"
output_document="${2:-$build_dir/semantic-roundtrip.odt}"
profile_dir="$build_dir/profile"
probe_binary="$build_dir/lok_semantic_probe"
watchdog="${SEMANTIC_PROBE_TIMEOUT:-30s}"
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

if ! command -v timeout >/dev/null 2>&1; then
  echo "FAIL: coreutils timeout is required as an outer watchdog" >&2
  exit 68
fi

if [[ ! -f /usr/include/LibreOfficeKit/LibreOfficeKit.h ]]; then
  echo "FAIL: LibreOfficeKit headers are missing; expected libreofficekit-dev to provide /usr/include/LibreOfficeKit/LibreOfficeKit.h" >&2
  exit 69
fi

if [[ ! -f "$lo_program/libsofficeapp.so" && ! -f "$lo_program/libmergedlo.so" ]]; then
  echo "FAIL: LibreOfficeKit runtime library not found in $lo_program" >&2
  exit 70
fi

mkdir -p "$build_dir"
rm -rf "$profile_dir"
mkdir -p "$profile_dir"
rm -f "$output_document"

"$cxx" \
  -std=c++20 \
  -Wall -Wextra -Wpedantic -Werror \
  -I/usr/include \
  "$script_dir/lok_semantic_probe.cxx" \
  -ldl \
  -o "$probe_binary"

SAL_USE_VCLPLUGIN=svp \
  timeout "$watchdog" \
  "$probe_binary" \
  "$lo_program" \
  "$profile_dir" \
  "$source_document" \
  "$output_document"

if [[ ! -s "$output_document" ]]; then
  echo "FAIL: semantic probe did not produce a non-empty ODT" >&2
  exit 71
fi

printf 'PASS: semantic command output %s; profile %s\n' \
  "$output_document" "$profile_dir"
