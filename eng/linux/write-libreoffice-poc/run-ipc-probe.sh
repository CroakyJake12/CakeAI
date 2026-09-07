#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: run-ipc-probe.sh <source-document> <output-document> [io-root]

Runs the disposable Haven Write helper and validation client across a local
AF_UNIX/SOCK_SEQPACKET socket. The helper owns LibreOfficeKit/unipoll; the
client only sees the narrow allow-listed protocol.

Environment overrides:
  LO_PROGRAM_PATH   LibreOffice program directory (default /usr/lib/libreoffice/program)
  ENGINE_BINARY     prebuilt helper executable
  CLIENT_BINARY     prebuilt validation client executable
  IPC_WORK_DIR      disposable helper work directory (default under /tmp)
  IPC_TIMEOUT       outer process watchdog duration (default 30s)
EOF
}

if [[ $# -lt 2 || $# -gt 3 ]]; then
  usage >&2
  exit 64
fi

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "FAIL: helper IPC probe must run on Linux" >&2
  exit 65
fi

source_document="$(realpath "$1")"
output_document="$2"
if [[ "$output_document" != /* ]]; then
  output_document="$(pwd)/$output_document"
fi
io_root="${3:-$(dirname "$source_document")}"
io_root="$(realpath "$io_root")"
lo_program="${LO_PROGRAM_PATH:-/usr/lib/libreoffice/program}"
work_dir="${IPC_WORK_DIR:-/tmp/haven-write-engine-ipc-poc}"
profile_dir="$work_dir/profile"
socket_path="$work_dir/engine.sock"
watchdog="${IPC_TIMEOUT:-30s}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
engine_binary="${ENGINE_BINARY:-$script_dir/haven_write_engine_poc}"
client_binary="${CLIENT_BINARY:-$script_dir/haven_write_engine_client}"
helper_log="$work_dir/helper.log"
client_log="$work_dir/client.log"
diagnostic_dir="$(dirname "$output_document")"

if [[ ! -f "$source_document" ]]; then
  echo "FAIL: source document does not exist: $source_document" >&2
  exit 66
fi
if [[ ! -d "$io_root" ]]; then
  echo "FAIL: I/O root does not exist: $io_root" >&2
  exit 67
fi
if [[ ! -x "$engine_binary" || ! -x "$client_binary" ]]; then
  echo "FAIL: helper/client binaries are not executable" >&2
  echo "helper=$engine_binary client=$client_binary" >&2
  exit 68
fi
if [[ ! -d "$lo_program" ]]; then
  echo "FAIL: LibreOffice program directory does not exist: $lo_program" >&2
  exit 69
fi
if ! command -v timeout >/dev/null 2>&1; then
  echo "FAIL: coreutils timeout is required as an outer watchdog" >&2
  exit 70
fi

mkdir -p "$work_dir" "$diagnostic_dir"
rm -rf "$profile_dir"
mkdir -p "$profile_dir"
rm -f "$socket_path" "$helper_log" "$client_log" "$output_document" \
  "$diagnostic_dir/ipc-helper.log" \
  "$diagnostic_dir/ipc-client.log" \
  "$diagnostic_dir/ipc-failure.txt"

helper_pid=''
cleanup() {
  if [[ -n "$helper_pid" ]] && kill -0 "$helper_pid" 2>/dev/null; then
    kill "$helper_pid" 2>/dev/null || true
    wait "$helper_pid" 2>/dev/null || true
  fi
  rm -f "$socket_path"
}
trap cleanup EXIT INT TERM

persist_failure_diagnostics() {
  local reason="$1"
  local client_status="${2:-n/a}"
  local helper_status="${3:-n/a}"
  local client_tail helper_tail summary

  cp "$helper_log" "$diagnostic_dir/ipc-helper.log" 2>/dev/null || true
  cp "$client_log" "$diagnostic_dir/ipc-client.log" 2>/dev/null || true

  client_tail="$(tail -n 8 "$client_log" 2>/dev/null | tr '\n' ' ' || true)"
  helper_tail="$(tail -n 12 "$helper_log" 2>/dev/null | tr '\n' ' ' || true)"
  summary="reason=$reason; client_status=$client_status; helper_status=$helper_status; client_tail=$client_tail; helper_tail=$helper_tail"
  printf '%s\n' "$summary" > "$diagnostic_dir/ipc-failure.txt"

  # GitHub Actions interprets workflow commands emitted by nested processes too.
  # Keep this deliberately compact so the check-run annotation can be queried
  # separately even when the full apt/container log is too large for tooling.
  summary="${summary:0:1800}"
  summary="${summary//'%'/'%25'}"
  summary="${summary//$'\r'/'%0D'}"
  summary="${summary//$'\n'/'%0A'}"
  printf '::error title=Writer helper IPC runtime failure::%s\n' "$summary"
}

SAL_USE_VCLPLUGIN=svp \
  timeout "$watchdog" \
  "$engine_binary" \
    "$lo_program" \
    "$profile_dir" \
    "$socket_path" \
    "$io_root" \
    >"$helper_log" 2>&1 &
helper_pid=$!

set +e
"$client_binary" "$socket_path" "$source_document" "$output_document" \
  > >(tee "$client_log") \
  2> >(tee -a "$client_log" >&2)
client_status=$?
set -e

if [[ $client_status -ne 0 ]]; then
  cat "$helper_log" >&2 || true
  persist_failure_diagnostics "validation-client-failed" "$client_status" "pending"
  echo "FAIL: helper IPC validation client exited with $client_status" >&2
  exit "$client_status"
fi

set +e
wait "$helper_pid"
helper_status=$?
set -e
helper_pid=''
cat "$helper_log"

if [[ $helper_status -ne 0 ]]; then
  persist_failure_diagnostics "helper-process-failed" "$client_status" "$helper_status"
  echo "FAIL: helper exited with status $helper_status" >&2
  exit "$helper_status"
fi
if [[ -e "$socket_path" ]]; then
  persist_failure_diagnostics "socket-not-removed" "$client_status" "$helper_status"
  echo "FAIL: helper did not remove its Unix socket during deterministic shutdown" >&2
  exit 71
fi
if [[ ! -s "$output_document" ]]; then
  persist_failure_diagnostics "output-missing" "$client_status" "$helper_status"
  echo "FAIL: helper IPC proof did not produce a non-empty ODT" >&2
  exit 72
fi

printf 'PASS: isolated helper IPC proof output=%s profile=%s\n' \
  "$output_document" "$profile_dir"
