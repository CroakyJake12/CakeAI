# HavenOS Ubuntu/GNOME platform boundary

HavenOS currently integrates with an Ubuntu 26.04 GNOME base through an optional image overlay. This repository does **not** treat stock GNOME packages as a Haven-maintained GNOME fork, and no GNOME fork should be claimed unless its source tree and provenance SHA are explicitly supplied and validated.

`scripts/linux/compose-gnome-platform.sh` verifies the base root filesystem before producing an overlay. It requires Ubuntu 26.04.x identity plus concrete GNOME session evidence, records the evidence in `usr/lib/haven/platform/platform.env`, and fails closed when provenance is missing or contradictory.

The compositor deliberately does not mutate the input root filesystem. Its output may contain Haven platform metadata only; it must not write GDM configuration, boot files, system-level systemd units, or GNOME Shell extensions. This keeps Haven integration outside the boot/login critical path and preserves the stock Ubuntu/GNOME session if Haven components are unavailable.

GNOME source provenance is opt-in. When `HAVEN_GNOME_SOURCE` and `HAVEN_GNOME_SOURCE_SHA` are omitted, the generated metadata records `HAVEN_GNOME_SOURCE_STATUS=not-supplied`. When they are supplied, the script requires GNOME Shell source markers (`meson.build` and `js/ui/main.js`) and a hexadecimal provenance SHA before recording `validated-explicit`. This records source evidence only; it does not assert that the supplied tree is a Haven-maintained fork.

Focused validation lives in `tests/linux/compose-gnome-platform-smoke.sh` and covers the success path, non-Ubuntu rejection, missing-GNOME rejection, overlay reuse refusal, false fork-source rejection, and explicit source-provenance acceptance.
