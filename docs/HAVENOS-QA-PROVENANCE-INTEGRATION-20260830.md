# HavenOS QA provenance and integration audit — 2026-08-30

Assigned branch: `havenos/worker/qa-provenance-integration-20260830`

Authoritative base audited: `havenos-main` at `7b2acae6175e5c380a3812b531b90ca82dbf85c3` (`docs: complete HavenOS provenance SHAs`).

This report is a point-in-time integration gate. A worker head that moves after this snapshot must be re-audited at the new SHA before merge.

## Executive result

- No branch, worktree, commit, or uncommitted change was deleted, reset, cleaned, stashed, rebased, force-pushed, or retired during this audit.
- The requested QA branch was created directly from the authoritative `havenos-main` SHA.
- The registered Sandbox checkout could not be switched to the QA branch because active task worktrees exist. Sandbox correctly returned: `Retire every active task worktree before changing the registered project's approved base branch.` Those worktrees were preserved.
- The historical HavenOS composition ledger is present at `docs/HAVENOS-COMPOSITION-PROVENANCE.md`. All eight recorded source commit SHAs resolve in this repository, and the final composition checkpoint `d20636a4bab264e13915f833d50f4fb47512150a` resolves.
- GitHub reports the authoritative base, all eight sampled historical source anchors, and every current changed worker head inspected below as unsigned (`verification.verified=false`, `reason=unsigned`). This is an unverified signature state, not evidence that a commit is missing. Unsigned/unverified branches are preservation-only until their merge decision is explicit; they are never deletion candidates.
- No open pull request targeting `havenos-main` was found at this snapshot.
- GitHub reports `havenos-main` and the current worker branches as unprotected. Integration safety therefore depends on the explicit queue and SHA pinning below.
- Exactly six current HavenOS worker branches were ahead of `havenos-main` in the captured branch inventory. Their changed paths do not overlap with one another.

## Historical migration provenance

The authoritative composition ledger records these source anchors:

| Order | Lane | Source SHA | Composition SHA |
| ---: | --- | --- | --- |
| 1 | Write | `f038d52191b2e8558e6036a25eda8f9ce79dbf70` | `dfbdc92f037bbc158759e994c3fa2c95ccb1a49d` |
| 2 | Browse | `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b` | `66415bea3f9da2de5efc8c00404934ba3a347853` |
| 3 | Data Spreadsheet | `8e778b70dfa3cddb0ccc3ff1b9481017ac21830a` | `b2d55208be5c081de116f0fd9044bc91a0da7151` |
| 4 | Data Database | `ef3ee8782504e64e4114a9c58605f6299aef37ca` | `3eccd1f5401bb21dff26c6474b42ac847b9f9e9c` |
| 5 | Quick Settings | `74a3866b44a7aaa34d9ff7f2e53883d6cd43193f` | `54e9772d89acce9d2abd5d5a22e32c4451d29592` |
| 6 | Projector | `6bd5f5424a28e8b54bf78579f7a7f75baefac80a` | `23a503242a78379a5f73c5ddb161bed86b6d5281` |
| 7 | Shell launcher | `06614a2195f2290da5b32b207ead968dafd07827` | `62703571a9df713b1819388104681bd3373753e7` |
| 8 | Linux packaging | `7e930d149979f80222737ffaf23e509580bcd140` | `3b9e4da3223a745fe813b04e15fb930ab9a95207` |

Final composed checkpoint: `d20636a4bab264e13915f833d50f4fb47512150a`.

The ledger itself states that source SHAs remain the provenance anchors and that the old composition commits were local integration history rather than merges to legacy `main`.

## Donor and licence disposition

No dedicated external donor/licence ledger was identified in the inspected `havenos-main` root or `docs` listing, and no root `LICENSE*` entry appeared in that inspected root listing. Therefore an external-source migration must not be treated as provenance-complete merely because its code builds.

For every future HavenOS app or platform migration that copies or adapts third-party code/assets, the integration gate requires a durable record containing: donor repository/source URL, exact donor tag/ref/SHA, licence identifier and licence/notice path, copied/adapted HavenOS paths, and a statement of whether source or assets were copied versus only used as a behavioural/reference donor.

Current changed-lane disposition:

| Branch | Donor/licence audit |
| --- | --- |
| `havenos/worker/canvas-app-20260830` | Internal migration only in the inspected diff. `HavenOS Apps/Canvas/README.md` identifies the existing Haven engines `src/Haven.Application/Canvas` and `src/Haven.Core/Notes`; no external donor code/assets were observed in the five added paths. Central provenance anchor for that internal donor is the audited base `7b2acae6175e5c380a3812b531b90ca82dbf85c3`. |
| `havenos/worker/os-apk-launcher-20260830` | No Android runtime/source is vendored. The slice is a capability-gated provider seam and explicitly does not claim an Android OS/runtime. No external donor licence record is triggered by the inspected diff. |
| `havenos/worker/os-gnome-platform-20260830` | No GNOME Shell source is vendored. The composition script records `HAVEN_GNOME_SOURCE_STATUS` and requires a 40–64 hex `HAVEN_GNOME_SOURCE_SHA` when an explicit GNOME source tree is supplied. It explicitly refuses to claim a fork when source is not supplied. |
| `havenos/worker/os-linux-packaging-runtime-20260830` | Packaging/workflow additions only; no donor code/assets observed. |
| `havenos/worker/os-performance-capabilities-20260830` | Internal Haven persistence refactor/tests only; no donor code/assets observed. |
| `havenos/worker/os-wine-compatibility-20260830` | Wine is detected/started as an optional host executable; Wine code is not vendored in the inspected diff. No external donor licence record is triggered by the adapter itself. |

Branches whose task explicitly requires a donor/reference record (for example new standalone surfaces that have not landed yet) remain non-mergeable until that record appears with the exact donor and licence evidence.

## Current changed worker heads

All six were ahead of `havenos-main` with `behind_by=0` when compared.

### 1. Linux packaging runtime

Branch: `havenos/worker/os-linux-packaging-runtime-20260830`

Head: `047795641210cb3d2c9bc993626be20bee330edf` (ahead 2)

Changed paths:
- `.github/workflows/havenos-linux-package.yml`
- `eng/publish-haven-linux.sh`

Focused evidence: GitHub Actions run `HavenOS Linux package` run `33330099179` completed successfully at this exact head SHA. The unrelated repository-wide compatibility/continuation workflows fail before scheduling jobs and are not counted as a lane-code failure.

Disposition: **READY first**, subject to the integrator pinning this exact SHA and accepting the repository's unsigned-commit policy.

### 2. Canvas app

Branch: `havenos/worker/canvas-app-20260830`

Head: `68a3c40ae5340acf2dc66945cf8328362d05df19` (ahead 1)

Changed paths:
- `HavenOS Apps/Canvas/CanvasAppSurface.cs`
- `HavenOS Apps/Canvas/HavenOS.Canvas.csproj`
- `HavenOS Apps/Canvas/README.md`
- `HavenOS Apps/Canvas/Tests/CanvasAppSurfaceTests.cs`
- `HavenOS Apps/Canvas/Tests/HavenOS.Canvas.Tests.csproj`

Placement is correct under `HavenOS Apps/Canvas`. The app delegates to the existing Haven Canvas/Notes engine rather than duplicating it. The branch documents focused build/test commands, but this QA lane could not independently execute .NET tests because the registered Sandbox checkout could not be switched while active task worktrees exist, and no branch-specific successful GitHub test run was present.

Disposition: **HOLD** pending independent focused build/test at this exact SHA.

### 3. GNOME platform composition

Branch: `havenos/worker/os-gnome-platform-20260830`

Head: `61e4f6102d629758557ded95272778ab996df4fc` (ahead 1)

Changed paths:
- `docs/HAVENOS-GNOME-PLATFORM.md`
- `scripts/linux/compose-gnome-platform.sh`
- `tests/linux/compose-gnome-platform-smoke.sh`

The script is bounded to an optional Ubuntu 26.04/GNOME overlay, refuses the live `/` root, treats `/etc/os-release` as data, rejects non-Ubuntu/missing-GNOME evidence, rejects non-empty overlay reuse, records optional GNOME source provenance, and forbids boot-critical/GDM/systemd/GNOME-extension outputs. The included smoke test covers those fail-closed paths. This QA lane inspected the exact script/test but did not obtain an independent successful execution at this SHA.

Disposition: **HOLD** pending independent execution of `tests/linux/compose-gnome-platform-smoke.sh` at this exact SHA.

### 4. Wine compatibility

Branch: `havenos/worker/os-wine-compatibility-20260830`

Head: `0369902f0afb25b0eed8a03f7b88ffd3063b123d` (ahead 1)

Changed paths:
- `src/Haven.Infrastructure/WindowsCompatibility/WindowsCompatibilityServiceCollectionExtensions.cs`
- `src/Haven.Infrastructure/WindowsCompatibility/WineWindowsExeCompatibilityService.cs`
- `tests/Haven.Infrastructure.Tests/WineWindowsExeCompatibilityServiceTests.cs`

The adapter is opt-in/fail-closed, Linux-only, validates `.exe` input, does not use shell command composition, and catches launch failures. No branch-specific successful focused test run was independently observed.

Disposition: **HOLD** pending focused infrastructure tests at this exact SHA.

### 5. APK launcher

Branch: `havenos/worker/os-apk-launcher-20260830`

Head: `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` (ahead 2)

Changed paths:
- `src/Haven.Application/ApkLaunchAbstractions.cs`
- `src/Haven.Infrastructure/Platform/ApkLaunchService.cs`
- `tests/Haven.Infrastructure.Tests/ApkLaunchServiceTests.cs`

The implementation is an optional provider seam, validates a fully qualified existing `.apk`, fails closed when no runtime is registered, and does not embed/claim an Android runtime. No branch-specific successful focused test run was independently observed.

Disposition: **HOLD** pending focused infrastructure tests at this exact SHA.

### 6. Performance capabilities

Branch: `havenos/worker/os-performance-capabilities-20260830`

Head: `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` (ahead 2)

Changed paths:
- `src/Haven.Infrastructure/Persistence/SQLite/CapabilityRepository.cs`
- `tests/Haven.Infrastructure.Tests/CapabilityRegistryTests.cs`

The refactor bounds built-in capability seeding to the existing read connection/transaction and adds a connection-count assertion. Because this changes existing persistence/concurrency behaviour rather than adding an isolated surface, it carries the highest regression sensitivity of the six current candidates. No branch-specific successful focused test run was independently observed.

Disposition: **HOLD** pending focused capability-registry tests plus the relevant infrastructure test project at this exact SHA.

## Exact merge queue

### Ready queue now

1. `havenos/worker/os-linux-packaging-runtime-20260830` @ `047795641210cb3d2c9bc993626be20bee330edf`

No other current changed worker branch is independently release-gated by this QA audit yet.

### Held queue after its blocker clears

The following order minimises blast radius and keeps the existing shared persistence mutation last. Re-check every head SHA and re-compare against the newly advanced `havenos-main` after each merge; do not rebase or force-push worker branches.

2. `havenos/worker/canvas-app-20260830` @ `68a3c40ae5340acf2dc66945cf8328362d05df19` — needs focused app build/test.
3. `havenos/worker/os-gnome-platform-20260830` @ `61e4f6102d629758557ded95272778ab996df4fc` — needs smoke script execution.
4. `havenos/worker/os-wine-compatibility-20260830` @ `0369902f0afb25b0eed8a03f7b88ffd3063b123d` — needs focused infrastructure tests.
5. `havenos/worker/os-apk-launcher-20260830` @ `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` — needs focused infrastructure tests.
6. `havenos/worker/os-performance-capabilities-20260830` @ `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` — needs capability-registry/infrastructure tests; merge last because it alters existing persistence behaviour.

If any held branch changes SHA, its place in the queue is invalid until re-audited.

## Branch/worktree preservation gate

Sandbox preflight found active historical task worktrees. The following dirty worktrees were observed and deliberately left untouched:

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

Additional clean task worktrees with commits ahead of their recorded bases also remain preserved. No cleanup or retirement action is authorised by this audit.

## Test/build evidence and blockers

- PASS: dedicated `HavenOS Linux package` GitHub Actions run `33330099179` at `047795641210cb3d2c9bc993626be20bee330edf`.
- NON-SIGNAL: repository-wide continuation/notes workflows on current worker pushes terminate with failure before creating jobs (`jobs.total_count=0` was confirmed on a representative run). They do not establish a compile/test failure in the worker diff.
- BLOCKER: registered Sandbox checkout cannot switch branches while active task worktrees exist. Exact response: `Retire every active task worktree before changing the registered project's approved base branch.` Retiring those worktrees would violate this lane's preservation requirement, so the audit intentionally did not do it.
- BLOCKER: the available secondary execution environment does not have the .NET SDK installed, so it cannot substitute for the protected Sandbox checkout for C# build/test validation.

## Integration rules from this audit

1. Merge only an explicitly queued branch at the exact audited SHA.
2. After every merge, refresh `havenos-main`, re-compare every remaining worker head, and update the queue.
3. Never delete a dirty worktree/branch, an unverified/unsigned branch, a branch whose SHA moved during review, or a branch lacking provenance/licence evidence required by its migration.
4. Do not rebase, reset, clean, stash, force-push, or overwrite another lane to make the queue easier to merge.
5. A branch-specific successful focused build/test outranks the repository's current zero-job generic workflow failures; absence of focused evidence remains a hold.
6. External donor code/assets require exact source/ref/licence records before merge. A capability adapter that does not vendor the external project must say so explicitly and must not claim unsupported platform/runtime ownership.
