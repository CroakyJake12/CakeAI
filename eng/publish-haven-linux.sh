#!/usr/bin/env bash
# FILE DOCUMENTATION
# Where: eng/publish-haven-linux.sh in the repository build/support layer.
# What: Publishes the existing Haven.Desktop net10.0 host for Ubuntu-compatible linux-x64 and packages only the resulting publish artifacts.
# Why: Keeps Linux packaging explicit, reproducible, non-boot-critical, and separate from the existing Windows target.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/src/Haven.Desktop/Haven.Desktop.csproj"
rid="${HAVEN_LINUX_RID:-linux-x64}"
configuration="${HAVEN_CONFIGURATION:-Release}"
artifacts_root="${HAVEN_ARTIFACTS_DIR:-$repo_root/artifacts/linux/$rid}"
publish_dir="$artifacts_root/publish"
package_path="$artifacts_root/haven-linux-$rid.tar.gz"

if [[ "$rid" != "linux-x64" ]]; then
  printf 'Unsupported Haven Linux RID: %s (supported: linux-x64)\n' "$rid" >&2
  exit 64
fi

if [[ ! -f "$project" ]]; then
  printf 'Haven desktop project not found: %s\n' "$project" >&2
  exit 66
fi

# This directory contains generated build output only. Removing it prevents stale files
# from being mixed into a package that must contain actual output from this publish run.
rm -rf "$artifacts_root"
mkdir -p "$publish_dir"

dotnet publish "$project" \
  --configuration "$configuration" \
  --framework net10.0 \
  --runtime "$rid" \
  --self-contained false \
  -p:TargetFrameworks=net10.0 \
  -p:UseAppHost=true \
  --output "$publish_dir"

required_artifacts=(
  "$publish_dir/Haven"
  "$publish_dir/Haven.dll"
  "$publish_dir/Haven.runtimeconfig.json"
)

for artifact in "${required_artifacts[@]}"; do
  if [[ ! -f "$artifact" ]]; then
    printf 'Required publish artifact missing: %s\n' "$artifact" >&2
    exit 65
  fi
done

if [[ ! -x "$publish_dir/Haven" ]]; then
  printf 'Published Linux apphost is not executable: %s\n' "$publish_dir/Haven" >&2
  exit 65
fi

tar -C "$publish_dir" -czf "$package_path" .

if [[ ! -s "$package_path" ]]; then
  printf 'Linux package was not created: %s\n' "$package_path" >&2
  exit 65
fi

printf 'Publish directory: %s\n' "$publish_dir"
printf 'Linux package: %s\n' "$package_path"
