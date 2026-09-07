# HavenOS QA provenance/integration post-freeze addendum — 2026-08-30

This addendum supersedes the **worker count and exact merge queue only** in `docs/HAVENOS-QA-PROVENANCE-INTEGRATION-20260830-LIVE.md`. All safety, provenance, validation, and dirty-work preservation findings in that report remain in force.

Authoritative target remains `havenos-main` @ `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.

## Newly landed lanes after the live freeze

### HUI Android

Branch: `havenos/worker/hui-android-20260830`

Head: `3222d14d9930fe086138afc8d3ab707666cd2df0`

Observed scope includes `src/Haven.Android.Hui/AndroidHuiCommandAdapter.cs`, its project, Android project wiring, and focused tests. The adapter translates shared HUI commands into a bounded Android render plan, reports unsupported commands, and fails malformed canvas state instead of approximating it. It does not claim an Android OS source tree or unsupported device behaviour. No third-party Android source is vendored in the inspected diff.

Gate: **HOLD-TEST/COORD** — independently run the HUI Android tests at this exact SHA and review the existing `src/Haven.Android` project wiring because it is a shared integration point.

### Images

Branch: `havenos/worker/images-app-20260830`

Head: `b44133bafab24d1efd60749ebf71b998d5f2d236`

Placement is correct under `HavenOS Apps/Images`. `HavenOS Apps/Images/PROVENANCE.md` records:

- reference: GNOME Image Viewer (Loupe)
- reference release: 50.0
- upstream: `https://gitlab.gnome.org/GNOME/loupe`
- upstream licence: `GPL-3.0-or-later`
- use: reference-only for the local chooser/image-view/adjacent-image/basic-properties journey
- copied code/assets: none

The provenance file explicitly states that no GPL source, UI markup, icons, artwork, or other assets were copied and that the implementation is original C#/Avalonia code.

Gate: **HOLD-TEST** — donor/licence evidence is adequate for the stated reference-only use, but the standalone viewer build/tests still need independent execution at this SHA.

## Superseding exact merge queue

The captured moving snapshot now contains 19 product lanes ahead of `havenos-main`. Only Linux packaging has independent focused technical validation in this QA audit. After each merge, re-compare every remaining branch against the newly advanced `havenos-main`; any moved SHA invalidates that queue entry.

1. `havenos/worker/os-linux-packaging-runtime-20260830` @ `047795641210cb3d2c9bc993626be20bee330edf` — technical integration gate passed via `HavenOS Linux package` run `33330099179`; public distribution remains held pending repository-level licence metadata.
2. `havenos/worker/hui-core-20260830` @ `29430290758553d94fc5f7f91d03ddf429a36ffa` — after focused HUI tests.
3. `havenos/worker/hui-accessibility-performance-20260830` @ `e2c3830f61849ab6e5b6cec5e7e3619f68817ca7` — after HUI regression tests; integrate after HUI Core and re-test combined state.
4. `havenos/worker/hui-android-20260830` @ `3222d14d9930fe086138afc8d3ab707666cd2df0` — after focused HUI Android tests and shared Android project-wiring review.
5. `havenos/worker/browse-app-20260830` @ `1cf9e49c81777e35798cd40ec5c18d28072cba7a` — provenance adequate; needs focused tests.
6. `havenos/worker/canvas-app-20260830` @ `ed2391ef4cf6853edc904d3bb810a27f33947ca6` — needs focused tests at the moved head.
7. `havenos/worker/data-app-20260830` @ `a9c33ada4008cba866b984b94bd0d0506a1607ec` — needs focused tests.
8. `havenos/worker/dev-app-20260830` @ `6ac2b1067c9f97694afd424a2c029d51854c2f7e` — provenance boundary explicit; needs focused tests.
9. `havenos/worker/images-app-20260830` @ `b44133bafab24d1efd60749ebf71b998d5f2d236` — donor/licence record adequate for reference-only use; needs focused viewer build/tests.
10. `havenos/worker/spaces-app-20260830` @ `0bf7082848d43d08dbaeaa81aa535650d81d8006` — needs focused tests.
11. `havenos/worker/terminal-app-20260830` @ `3e95e890eb64c2a49f2c85461377ba3196acd5df` — needs focused specs/build.
12. `havenos/worker/write-app-20260830` @ `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6` — needs Desktop/Write tests and shared project-wiring review.
13. `havenos/worker/os-llm-runtime-20260830` @ `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` — needs focused runtime tests.
14. `havenos/worker/os-wine-compatibility-20260830` @ `0369902f0afb25b0eed8a03f7b88ffd3063b123d` — needs focused infrastructure tests.
15. `havenos/worker/os-apk-launcher-20260830` @ `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` — needs focused infrastructure tests.
16. `havenos/worker/os-gnome-platform-20260830` @ `61e4f6102d629758557ded95272778ab996df4fc` — only after source-SHA provenance semantics are corrected and smoke tests pass.
17. `havenos/worker/os-shell-taskbar-20260830` @ `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15` — needs Desktop/shell regression tests; integrate late.
18. `havenos/worker/os-performance-capabilities-20260830` @ `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` — needs capability/infrastructure regression tests; integrate last among currently substantive slices because it changes existing persistence behaviour.

`havenos/worker/wave-app-20260830` @ `03ae35b730c077e257ca0d35084cb85af470d70f` remains deliberately excluded: at this SHA it contains only the Wave project file and has no donor/reference record, licence evidence, functional journey, or focused validation.

Baseline-only remote worker branches in this capture are `boards-app`, `hui-desktop`, `motion-app`, `os-settings-model-picker`, `present-app`, and `qa-functional-release`. Baseline-only is never a deletion signal; local/uncommitted work may still exist.
