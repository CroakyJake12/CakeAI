# HavenOS provenance and integration audit — 2026-08-30

## Scope and safety invariant

Authoritative target: `havenos-main` @ `7b2acae6175e5c380a3812b531b90ca82dbf85c3` (`docs: complete HavenOS provenance SHAs`).

Assigned QA lane: `havenos/worker/qa-provenance-integration-20260830`, created from that target. This worker instance wrote only this report path. A concurrent writer on the same assigned QA branch added `docs/HAVENOS-QA-PROVENANCE-INTEGRATION-20260830.md` at commit `77419868c15f989152bda15c71630a8f69ea2d55` between this instance's commits. That concurrent work was preserved and not rewritten or deleted. Because it contains an earlier six-lane snapshot, this refreshed report is the newer queue snapshot; the two QA reports must be reconciled before merging the QA branch wholesale.

No product branch/worktree was deleted, reset, cleaned, stashed, rebased, force-pushed, or rewritten. Dirty and otherwise unverified work remains retained.

## Provenance and licensing

`docs/HAVENOS-COMPOSITION-PROVENANCE.md` records eight source and composition anchors. All eight source SHAs resolve in `CroakyJake12/HavenAI`:

1. Write — `f038d52191b2e8558e6036a25eda8f9ce79dbf70`
2. Browse — `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b`
3. Data Spreadsheet — `8e778b70dfa3cddb0ccc3ff1b9481017ac21830a`
4. Data Database — `ef3ee8782504e64e4114a9c58605f6299aef37ca`
5. Quick Settings — `74a3866b44a7aaa34d9ff7f2e53883d6cd43193f`
6. Projector — `6bd5f5424a28e8b54bf78579f7a7f75baefac80a`
7. Shell launcher — `06614a2195f2290da5b32b207ead968dafd07827`
8. Linux packaging — `7e930d149979f80222737ffaf23e509580bcd140`

Commit-chain provenance is internally consistent, but the ledger does not identify donor repository/origin or license. The recursive `havenos-main` tree has no license-named file and root `LICENSE` is absent. Distribution/license clearance is therefore **unproven** until a first-party ownership statement or donor/license manifest is added. This is a compliance gate, not an allegation of infringement.

The current Dev, Terminal, Write, Canvas, HUI Core, and shell/taskbar slices inspected here reuse repository-local Haven contracts/surfaces rather than adding copied third-party source. Dev explicitly excludes Visual Studio extension source from its lane. The APK/Wine/llama.cpp slices expose optional host/provider seams rather than vendoring those external runtimes. These facts do not remove the repository-level licensing gap.

## Branch/commit hygiene and integration readiness

`havenos-main` is unprotected, has no required status checks, and its head commit is unsigned. A refreshed search found no open PRs matching current `havenos/worker` lanes. All 12 product heads ahead of `havenos-main` in the captured final ref snapshot have empty combined commit-status sets. Separate GitHub Actions inspection found one dedicated successful lane workflow: Linux packaging at `047795641210cb3d2c9bc993626be20bee330edf`. Every product head below was 0 commits behind the authoritative base when compared.

| Lane | Head | Ahead | Scope | Gate |
| --- | --- | ---: | --- | --- |
| `os-gnome-platform` | `61e4f6102d629758557ded95272778ab996df4fc` | 1 | `docs/`, `scripts/linux/`, `tests/linux/` | **HOLD** — caller-supplied GNOME SHA is only format-checked, not bound to source content/HEAD; smoke script not independently executed. |
| `hui-core` | `29430290758553d94fc5f7f91d03ddf429a36ffa` | 1 | `src/Haven.UI/` + tests | **HOLD-PLACEMENT/TEST** — confirm existing shared HUI tree satisfies the HavenOS-root ownership rule; independently test. |
| `os-performance-capabilities` | `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` | 2 | `src/Haven.Infrastructure/...` + tests | **HOLD-PLACEMENT/TEST** — resolve/coordinate OS-root platform-service ownership; independently test. |
| `os-wine-compatibility` | `0369902f0afb25b0eed8a03f7b88ffd3063b123d` | 1 | `src/Haven.Infrastructure/WindowsCompatibility/...` + tests | **HOLD-PLACEMENT/TEST**. |
| `os-llm-runtime` | `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` | 2 | docs + `src/Haven.Application` / `src/Haven.Infrastructure` + tests | **HOLD-PLACEMENT/TEST** — docs call this OS-root while code remains in legacy `src/`. |
| `os-apk-launcher` | `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` | 2 | `src/Haven.Application` / `src/Haven.Infrastructure` + tests | **HOLD-PLACEMENT/TEST**. |
| `os-shell-taskbar` | `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15` | 1 | shared `src/Haven.Desktop` shell + tests | **HOLD-REVIEW/TEST** — changes active shell chrome from top rail placement to bottom taskbar; shared-shell ownership/regression review required. |
| `dev-app` | `6ac2b1067c9f97694afd424a2c029d51854c2f7e` | 1 | `HavenOS Apps/Dev/` only | **HOLD-TEST** — correct app placement; no independent build/test evidence. |
| `terminal-app` | `054cea541580273711f545ba53df1c3e56ef7c51` | 1 | `HavenOS Apps/Terminal/` only | **HOLD-TEST** — correct app placement and fail-closed host capability design; independently run specs. |
| `canvas-app` | `68a3c40ae5340acf2dc66945cf8328362d05df19` | 1 | `HavenOS Apps/Canvas/` only | **HOLD-TEST** — correct isolated app placement; no independent build/CI evidence. |
| `write-app` | `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6` | 1 | `HavenOS Apps/Write/` + shared Desktop project/test wiring | **HOLD-COORD/TEST** — coordinate shared `Haven.Desktop.csproj`/test ownership, then independently test. |
| `os-linux-packaging-runtime` | `047795641210cb3d2c9bc993626be20bee330edf` | 2 | `.github/workflows/`, `eng/` | **CODE-VALIDATED / HOLD-LICENSE** — dedicated `HavenOS Linux package` run `33330099179` passed; distribution license metadata remains unproven. |

Thirteen other current worker branches remained exactly at baseline SHA `7b2acae6175e5c380a3812b531b90ca82dbf85c3` in the captured final matching-ref snapshot and therefore had no remote product slice to merge: `boards-app`, `browse-app`, `data-app`, `hui-accessibility-performance`, `hui-android`, `hui-desktop`, `images-app`, `motion-app`, `os-settings-model-picker`, `present-app`, `qa-functional-release`, `spaces-app`, and `wave-app`.

Baseline-only is **not** a deletion signal. A branch can still have active/uncommitted work.

## Exact conditional merge queue

No product branch is authorized for immediate release/integration by this audit because every current candidate still has at least one unresolved gate. After each branch's gate is cleared, integrate in this order and re-compare all remaining heads after every merge:

1. `havenos/worker/os-gnome-platform-20260830` @ `61e4f6102d629758557ded95272778ab996df4fc`
2. `havenos/worker/hui-core-20260830` @ `29430290758553d94fc5f7f91d03ddf429a36ffa`
3. `havenos/worker/os-performance-capabilities-20260830` @ `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9`
4. `havenos/worker/os-wine-compatibility-20260830` @ `0369902f0afb25b0eed8a03f7b88ffd3063b123d`
5. `havenos/worker/os-llm-runtime-20260830` @ `fcc5c9c77b5fd5dec967a3b23031a306db744dd8`
6. `havenos/worker/os-apk-launcher-20260830` @ `5cd6dcfe97be9d6fc7248efe9392e4651b03a628`
7. `havenos/worker/os-shell-taskbar-20260830` @ `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15`
8. `havenos/worker/dev-app-20260830` @ `6ac2b1067c9f97694afd424a2c029d51854c2f7e`
9. `havenos/worker/terminal-app-20260830` @ `054cea541580273711f545ba53df1c3e56ef7c51`
10. `havenos/worker/canvas-app-20260830` @ `68a3c40ae5340acf2dc66945cf8328362d05df19`
11. `havenos/worker/write-app-20260830` @ `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6`
12. `havenos/worker/os-linux-packaging-runtime-20260830` @ `047795641210cb3d2c9bc993626be20bee330edf`

Rationale: provenance/platform/HUI foundations first, shared shell next, isolated app surfaces after that, shared Write wiring late, and packaging tooling/final package validation last. Packaging's dedicated CI already passes, but release distribution remains held until licensing is explicit. Do not mechanically merge a queued SHA if its head advances, `havenos-main` moves, or changed paths begin to overlap.

## Worktree preservation gate

Sandbox preflight exposed ten dirty legacy agent worktrees. They are an explicit **do-not-delete/do-not-retire** set until captured and independently verified:

- `agent/a01-a02-hub-spaces-20260829`
- `agent/r11-build-ci-arm64-windows-portability-20260829`
- `agent/r04-hui-linux-gnome-20260829`
- `agent/r10-hardware-linux-services-20260829`
- `agent/r08-packaging-lifecycle-permissions-20260829`
- `agent/r06-native-apk-runtime-20260829`
- `agent/r02-shared-platform-sdk-ipc-20260829`
- `agent/r05-native-exe-wine-20260829`
- `agent/r03-ubuntu-gnome-base-20260829`
- `agent/r01-programme-architecture-ledger-v2-20260829`

Other active clean worktrees are retained too: clean does not mean merged or independently verified.

## Validation performed and blockers

The Sandbox refused switching the registered Haven checkout to this QA branch because active task worktrees are registered. The refusal was respected. Therefore this lane could not independently execute local `dotnet`/shell builds or tests without disturbing active work.

Completed evidence:

- verified authoritative/base and QA branch identity;
- inspected repository tree for provenance/license records;
- resolved all eight existing source provenance anchors;
- compared all 12 current product heads to `havenos-main` for ahead/behind and changed paths;
- captured matching refs for current HavenOS worker branches;
- refreshed the open-PR search (none found);
- checked combined commit statuses for all 12 current product heads (all empty);
- verified dedicated Linux packaging Actions run `33330099179` at exact head `047795641210cb3d2c9bc993626be20bee330edf`: job `publish-linux-x64` passed checkout, .NET 10 setup, publish/package, artifact verification, and upload;
- verified a representative generic GNOME workflow failure (`33329993746`) had zero jobs, so that generic failure is non-signal for lane code;
- captured active/dirty local worktree state;
- detected and preserved concurrent QA commit `77419868c15f989152bda15c71630a8f69ea2d55` rather than rewriting branch history.

Blocking actions:

1. Add repository ownership/license information plus donor manifests where imported code exists (source repo, immutable revision, SPDX/license, imported paths, attribution/notice requirements).
2. Bind GNOME source provenance to actual supplied source content/repository HEAD, not only a hex-shaped caller value.
3. Resolve or explicitly coordinate shared HUI/platform-runtime ownership/placement against the HavenOS-root rule.
4. Review the shell/taskbar shared integration and Write's shared Desktop wiring with the relevant owners.
5. Obtain independent focused build/test evidence for every queued SHA except packaging, whose dedicated package workflow already passed.
6. Add branch protection/required checks for `havenos-main` before routine multi-worker integration.
7. Reconcile the two QA reports on this branch before merging the QA branch wholesale; do not delete the concurrent report merely because it is older.
8. Preserve every dirty or unverified branch/worktree until its owner reports it captured and safe to retire.
