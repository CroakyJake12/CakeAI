# HavenOS QA provenance/integration live gate — 2026-08-30

This document supersedes earlier queue snapshots for merge ordering only. It preserves the earlier audit records rather than rewriting or deleting them.

Authoritative target at capture: `havenos-main` @ `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.

Assigned QA lane: `havenos/worker/qa-provenance-integration-20260830`.

## Safety and branch-hygiene invariant

No product branch/worktree was deleted, reset, cleaned, stashed, rebased, force-pushed, or retired by this QA lane. Dirty and unsigned/unverified branches are preservation-only until explicitly resolved.

The Sandbox registered checkout could not be switched to this QA branch because active task worktrees exist. Exact refusal: `Retire every active task worktree before changing the registered project's approved base branch.` Retiring them would violate this audit's preservation requirement, so they were left untouched.

A second documentation commit appeared on the assigned QA branch after this audit's first commit: `3005b530af66ca35b9c7828dd3eb8de73cc1d8ee`, parent `77419868c15f989152bda15c71630a8f69ea2d55`, message `docs(qa): refresh HavenOS integration queue`. It was preserved exactly; no reset/force-push was attempted. This concurrent movement is itself an integration-coordination warning for an otherwise exclusive lane.

## Provenance baseline

`docs/HAVENOS-COMPOSITION-PROVENANCE.md` records eight historical source anchors. All eight source SHAs were individually resolved during this audit:

- Write `f038d52191b2e8558e6036a25eda8f9ce79dbf70`
- Browse `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b`
- Data Spreadsheet `8e778b70dfa3cddb0ccc3ff1b9481017ac21830a`
- Data Database `ef3ee8782504e64e4114a9c58605f6299aef37ca`
- Quick Settings `74a3866b44a7aaa34d9ff7f2e53883d6cd43193f`
- Projector `6bd5f5424a28e8b54bf78579f7a7f75baefac80a`
- Shell launcher `06614a2195f2290da5b32b207ead968dafd07827`
- Linux packaging `7e930d149979f80222737ffaf23e509580bcd140`

The ledger's final composed checkpoint `d20636a4bab264e13915f833d50f4fb47512150a` also resolves. GitHub reports the authoritative base, sampled historical anchors, and inspected worker heads as unsigned (`verified=false`, `reason=unsigned`). Unsigned is treated as an unverified signature state, not as evidence that a commit is missing or may be deleted.

No repository-level donor/license manifest or root `LICENSE*` entry was identified in the audited `havenos-main` tree/listing. Therefore public distribution/licensing clearance remains unproven at repository level. This is a compliance blocker for distribution, not an allegation of infringement.

External donor code/assets must have a durable record before merge: source repository/URL, exact tag/ref/SHA, licence identifier and notice path, copied/adapted HavenOS paths, and whether the source was copied or used only as a behavioural/reference donor.

## Live worker-head snapshot

The following 17 product lanes were ahead of `havenos-main` in the captured ref snapshot. A moved SHA invalidates its row until re-audited.

| Lane | Captured head | Placement/provenance finding | Gate |
| --- | --- | --- | --- |
| Browse | `1cf9e49c81777e35798cd40ec5c18d28072cba7a` | Correct under `HavenOS Apps/Browse`. README records base `7b2acae...`, historical Browse source `ec48a80...`, reused `src/Haven.Browser`, and states no external donor code is introduced. | **HOLD-TEST** — focused build/test not independently observed. |
| Canvas | `ed2391ef4cf6853edc904d3bb810a27f33947ca6` | Correct under `HavenOS Apps/Canvas`; reuses existing Haven Canvas/Notes engines. Latest commit isolates tests from app compilation. No external donor source observed in inspected diff. | **HOLD-TEST** — focused build/test required at this new head. |
| Data | `a9c33ada4008cba866b984b94bd0d0506a1607ec` | Correct under `HavenOS Apps/Data`; delegates to existing Haven workbook/query/formula contracts. | **HOLD-TEST** — run focused Data app tests. |
| Dev | `6ac2b1067c9f97694afd424a2c029d51854c2f7e` | Correct under `HavenOS Apps/Dev`. README explicitly excludes copied Visual Studio SDK/VSIX/extension source and delegates to existing Haven code-intelligence contracts. | **HOLD-TEST** — run focused Dev build/tests. |
| HUI accessibility/performance | `e2c3830f61849ab6e5b6cec5e7e3619f68817ca7` | Shared HUI change under root `src/Haven.UI`; modifies motion, keyboard/accessibility state and value lookup with focused tests added. | **HOLD-TEST** — shared runtime regression gate; run full focused HUI tests. |
| HUI Core | `29430290758553d94fc5f7f91d03ddf429a36ffa` | Shared HUI contracts/tokens under root `src/Haven.UI`; internal semantic-token migration only in inspected diff. | **HOLD-TEST** — run `Haven.UI.Tests` at exact SHA. |
| APK launcher | `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` | Shared application/infrastructure seam; no Android runtime/source vendored and no Android OS ownership claimed. | **HOLD-TEST** — focused infrastructure tests required. |
| GNOME platform | `61e4f6102d629758557ded95272778ab996df4fc` | Optional Ubuntu/GNOME overlay; no GNOME Shell source vendored. | **HOLD-PROVENANCE** — `HAVEN_GNOME_SOURCE_SHA` is only format-checked; an arbitrary matching source tree + arbitrary hex SHA is currently labelled `validated-explicit`. Bind the SHA to real source content/HEAD or rename the state to caller-supplied/unverified, then run smoke tests. |
| Linux packaging runtime | `047795641210cb3d2c9bc993626be20bee330edf` | Root packaging/workflow files only. | **READY-TO-INTEGRATE / RELEASE-HOLD** — dedicated `HavenOS Linux package` Actions run `33330099179` passed at this exact SHA; repository-level distribution licence metadata remains unproven. |
| llama.cpp runtime | `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` | Shared process seam; documentation explicitly says llama.cpp is not installed/vendored and Ollama remains default. | **HOLD-TEST** — focused runtime tests required; no replacement-provider claim is made. |
| Performance capabilities | `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` | Shared persistence change in `src/Haven.Infrastructure`; changes existing connection/transaction behaviour and adds count coverage. | **HOLD-TEST** — highest regression sensitivity; run capability registry plus relevant infrastructure tests. |
| Shell/taskbar | `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15` | Shared Desktop shell change at OS root, including existing shell XAML/scene files. | **HOLD-TEST** — run shell/Desktop tests; merge late due existing-shell blast radius. |
| Wine compatibility | `0369902f0afb25b0eed8a03f7b88ffd3063b123d` | Shared fail-closed host adapter; Wine source is not vendored. | **HOLD-TEST** — focused infrastructure tests required. |
| Spaces | `0bf7082848d43d08dbaeaa81aa535650d81d8006` | Correct under `HavenOS Apps/Spaces`; delegates navigation to existing SpaceRegistry/Haven modes and shell host. | **HOLD-TEST** — run focused Spaces tests. |
| Terminal | `3e95e890eb64c2a49f2c85461377ba3196acd5df` | Correct under `HavenOS Apps/Terminal`; internal wrapper over existing terminal-session/permission contracts; explicitly does not fall back to direct process launch. Latest commit isolates specs from app compile. | **HOLD-TEST** — run Terminal specs/build at this exact head. |
| Wave | `03ae35b730c077e257ca0d35084cb85af470d70f` | Correct directory, but current remote slice contains only `HavenOS Apps/Wave/HavenOS.Wave.csproj`. | **HARD HOLD** — no donor/reference record, licence evidence, functional journey, or focused test exists at this SHA. Do not merge this incomplete slice. |
| Write | `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6` | App contract under `HavenOS Apps/Write`, wired to existing Desktop editor; historical Write source anchor is `f038d521...`. | **HOLD-TEST** — run Desktop/Write tests; wiring touches existing `Haven.Desktop.csproj`. |

Baseline-only worker branches in this snapshot are not deletion candidates: `boards-app`, `hui-android`, `hui-desktop`, `images-app`, `motion-app`, `os-settings-model-picker`, `present-app`, and `qa-functional-release`. Local work may exist even when a remote branch is still at the common base.

## Exact merge queue

Only one current product head has independent focused technical validation in this QA audit. Merge by exact SHA and re-compare every remaining lane after each integration:

1. `havenos/worker/os-linux-packaging-runtime-20260830` @ `047795641210cb3d2c9bc993626be20bee330edf` — technical integration gate passed; do not treat the produced package as distribution-cleared until repository licence metadata is established.
2. `havenos/worker/hui-core-20260830` @ `29430290758553d94fc5f7f91d03ddf429a36ffa` — after focused HUI tests pass.
3. `havenos/worker/hui-accessibility-performance-20260830` @ `e2c3830f61849ab6e5b6cec5e7e3619f68817ca7` — after focused/full HUI regression tests pass; integrate after HUI Core and re-test on combined base.
4. `havenos/worker/browse-app-20260830` @ `1cf9e49c81777e35798cd40ec5c18d28072cba7a` — provenance record is adequate; needs focused tests.
5. `havenos/worker/canvas-app-20260830` @ `ed2391ef4cf6853edc904d3bb810a27f33947ca6` — needs focused tests at moved head.
6. `havenos/worker/data-app-20260830` @ `a9c33ada4008cba866b984b94bd0d0506a1607ec` — needs focused tests.
7. `havenos/worker/dev-app-20260830` @ `6ac2b1067c9f97694afd424a2c029d51854c2f7e` — provenance boundary is explicit; needs focused tests.
8. `havenos/worker/spaces-app-20260830` @ `0bf7082848d43d08dbaeaa81aa535650d81d8006` — needs focused tests.
9. `havenos/worker/terminal-app-20260830` @ `3e95e890eb64c2a49f2c85461377ba3196acd5df` — needs focused specs/build.
10. `havenos/worker/write-app-20260830` @ `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6` — needs focused Desktop/Write tests.
11. `havenos/worker/os-llm-runtime-20260830` @ `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` — needs focused runtime tests.
12. `havenos/worker/os-wine-compatibility-20260830` @ `0369902f0afb25b0eed8a03f7b88ffd3063b123d` — needs focused infrastructure tests.
13. `havenos/worker/os-apk-launcher-20260830` @ `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` — needs focused infrastructure tests.
14. `havenos/worker/os-gnome-platform-20260830` @ `61e4f6102d629758557ded95272778ab996df4fc` — only after the source-SHA provenance semantics are corrected and smoke test passes.
15. `havenos/worker/os-shell-taskbar-20260830` @ `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15` — needs Desktop/shell regression tests; merge late.
16. `havenos/worker/os-performance-capabilities-20260830` @ `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` — needs capability/infrastructure regression tests; merge last among currently substantive slices because it mutates existing persistence behaviour.

`havenos/worker/wave-app-20260830` @ `03ae35b730c077e257ca0d35084cb85af470d70f` is deliberately excluded from the merge queue until its donor/licence record, functional journey, and focused validation exist.

## Validation evidence and blockers

- PASS: GitHub Actions `HavenOS Linux package`, run `33330099179`, exact head `047795641210cb3d2c9bc993626be20bee330edf`.
- NON-SIGNAL: generic continuation/notes workflows on sampled worker pushes fail before scheduling jobs (`jobs.total_count=0` on a representative run); they do not prove a worker compile/test failure.
- BLOCKER: active Sandbox worktrees prevent safe checkout switching, and this QA lane will not retire them merely to gain a clean checkout.
- BLOCKER: the available secondary execution environment has no .NET SDK, so it cannot independently execute the C# project tests.
- BLOCKER: worker heads are actively moving; every exact SHA must be rechecked immediately before merge.
- BLOCKER: the assigned QA branch itself received a concurrent fast-forward documentation commit, so exclusive-lane coordination is not fully reliable at present.
- RELEASE BLOCKER: repository-level distribution licence metadata was not identified in the audited tree.

## Dirty-work preservation

Sandbox preflight exposed dirty historical task worktrees including `agent/a01-a02-hub-spaces-20260829`, `agent/r11-build-ci-arm64-windows-portability-20260829`, `agent/r04-hui-linux-gnome-20260829`, `agent/r10-hardware-linux-services-20260829`, `agent/r08-packaging-lifecycle-permissions-20260829`, `agent/r06-native-apk-runtime-20260829`, `agent/r02-shared-platform-sdk-ipc-20260829`, `agent/r05-native-exe-wine-20260829`, `agent/r03-ubuntu-gnome-base-20260829`, and `agent/r01-programme-architecture-ledger-v2-20260829`. They remain untouched and must not be deleted as part of integration cleanup.
