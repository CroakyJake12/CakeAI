# HavenOS Functional and Release QA — 2026-08-30

## Release verdict

**HOLD.** Exact-SHA validation reproduced functional/test failures. This QA lane changed no product or test code.

## Authority and snapshot

- Authoritative branch: `havenos-main` at `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.
- QA branch: `havenos/worker/qa-functional-release-20260830`.
- Evidence freeze: 2026-08-30 approximately 20:45 BST.

## Independent evidence

| Slice | SHA | Result | Evidence |
| --- | --- | --- | --- |
| GNOME | `61e4f6102d629758557ded95272778ab996df4fc` | PASS | Git Bash smoke job `520c6153-f899-4b0a-bbbd-1322ef653992`, exit 0. Initial bare `bash` launch failure was host PATH only. |
| Linux packaging | `047795641210cb3d2c9bc993626be20bee330edf` | HOST-LIMITED | Syntax job `5f4a7c46-4647-4dc1-a89a-4eb7aed7287f`, exit 0. Full job `26b42b44-9b65-418a-b286-ae62302d7228` exits 65 because Windows-hosted Git Bash cannot satisfy the Linux apphost executable-bit check. Workflow targets Ubuntu 24.04; native Linux validation remains required. |
| Wine EXE | `0369902f0afb25b0eed8a03f7b88ffd3063b123d` | FAIL | Focused build `aa0dff21-d675-4725-ba8b-c3e3da8ef9e6` exit 1; Release build `0a8fba3f-db4c-422c-a87a-98f9de1cbbcb` failed; targeted test job `6b5ea618-9a8a-4127-b88b-ad74ba50d279` exit 1. Runner did not expose a reliable root cause, so none is inferred. |
| Data | `a9c33ada4008cba866b984b94bd0d0506a1607ec` | FAIL | Jobs `85bf7aff-ef6d-4a09-a981-38701cc4eed8`, `846eed75-6060-4228-9d3d-be8b9d1af295`, `069d3da0-023d-4884-a0ae-10cb15f8362a` failed. Named failures cover unsupported-route storage safety, read-only query behavior, mutating-SQL rejection, and spreadsheet recalc/save. |
| llama.cpp runtime | `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` | FAIL | Focused jobs including `8347b85e-b1d4-4fe6-8039-1a45f8939f7f` and rerun `99d1d2fd-6ea5-4948-84ac-ddf85cd469b4` exit 1. Named failures include malformed environment-number handling, invalid resource-bound rejection, missing-executable fail-closed behavior, and disabled always-loaded no-op behavior. |
| Canvas | `ed2391ef4cf6853edc904d3bb810a27f33947ca6` | PASS | Clean isolated exact-head `dotnet test` job `05932e4d-6bcd-4ac9-a9d7-ef5216b75eaf`, exit 0. |
| Browse | `1cf9e49c81777e35798cd40ec5c18d28072cba7a` | FAIL / HOLD | Clean isolated exact-head focused test job `8371735a-00b8-4ae7-9a73-3b890521a243`, exit 1. Available runner evidence did not identify a trustworthy assertion/build cause. |
| Write | `49a937ebc24f3aa92df3dcb0dc1e22cf79c372d6` | RESULT UNRESOLVED AT FREEZE | Clean isolated exact-head focused `WriteAppSurfaceTests` job `9af21870-a5df-445d-82e6-1cf6e476756e` was running at the last snapshot where that job was visible; this report does not infer a result. |

## Additional frozen worker heads inspected

The following exact heads were compared with the authoritative base and their changed-path/test placement inspected, but were not all independently re-run by this QA lane before the evidence freeze:

- Dev `6ac2b1067c9f97694afd424a2c029d51854c2f7e` — `HavenOS Apps/Dev`, dedicated tests.
- HUI accessibility/performance `e2c3830f61849ab6e5b6cec5e7e3619f68817ca7` — `src/Haven.UI` plus `tests/Haven.UI.Tests`.
- HUI core `29430290758553d94fc5f7f91d03ddf429a36ffa` — `src/Haven.UI` plus `tests/Haven.UI.Tests`.
- APK launcher `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` — `src/Haven.Application`, `src/Haven.Infrastructure`, infrastructure tests.
- Performance/capabilities `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` — infrastructure capability persistence and tests.
- Shell/taskbar `e1d0a9c123211b1dcef0ac8ecc71d09349df9f15` — shared desktop shell and tests.
- Spaces `0bf7082848d43d08dbaeaa81aa535650d81d8006` — `HavenOS Apps/Spaces`, dedicated tests.
- Terminal `3e95e890eb64c2a49f2c85461377ba3196acd5df` — `HavenOS Apps/Terminal`, dedicated specs.

A later local Images worker worktree showed `b44133bafab24d1efd60749ebf71b998d5f2d236`, but it was not part of the frozen remote release snapshot and its worktree was marked invalid; it is deliberately excluded from pass/fail release evidence here.
## Release blockers and follow-up gates

1. **Data is red at `a9c33ada4008cba866b984b94bd0d0506a1607ec`.** This is a direct functional release blocker.
2. **The llama.cpp runtime is red at `fcc5c9c77b5fd5dec967a3b23031a306db744dd8`.** Contract tests fail, including disabled/fail-closed/resource-bound behavior.
3. **Wine compatibility is red at `0369902f0afb25b0eed8a03f7b88ffd3063b123d`.** Focused build/test validation failed; exact root cause still needs reliable runner log access or a fresh exact-SHA target-environment run.
4. **Browse focused validation exits 1 at `1cf9e49c81777e35798cd40ec5c18d28072cba7a`.** Root cause remains unresolved.
5. **Linux packaging needs native Linux validation.** Windows-hosted Git Bash cannot prove Unix executable-bit behavior.
6. **Shared-code placement needs integration-owner confirmation.** The lane contract places shared HUI and platform services at the OS root, while HUI/accessibility/core/APK/LLM/capability slices use existing `src/Haven.*` locations. QA did not relocate another lane's code.
7. **QA provenance `3005b530af66ca35b9c7828dd3eb8de73cc1d8ee` is stale relative to later worker heads** and cannot be the sole release-queue authority.
8. **Native runtime coverage is incomplete on this Windows host.** Real GNOME, Wine-on-Linux, APK runtime and Linux packaging execution still need their target environments.
9. **Donor provenance/license completeness remains a separate release gate** until reconciled against the final selected SHAs.

## Placement observations

Browse, Canvas, Data, Dev, Spaces and Terminal place their app surfaces under `HavenOS Apps/<App>`. Write places its surface under `HavenOS Apps/Write` but also wires shared desktop project/test files. Shared HUI/platform slices noted above remain in existing `src/Haven.*` locations pending integration-owner direction.

## QA change discipline

- Product code changed by this QA lane: **none**.
- Test code changed by this QA lane: **none**.
- Destructive Git operations: **none**.
- No other worker worktree was reset, cleaned, stashed, rebased, deleted or force-pushed.

## Recommendation

Do **not** merge or release this frozen snapshot as a single HavenOS release candidate. Fix the red exact-SHA slices, perform native Linux/Wine/APK validation, reconcile shared-code placement and provenance, then freeze and independently validate a new release snapshot.
