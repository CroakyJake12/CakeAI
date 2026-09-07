# Haven Write LibreOffice engine boundary

Status: **portable Linux proof-of-concept runtime-proven; not yet runtime-proven on the approved CakeOS VM or wired into production HUI**.

Haven Write keeps HUI as the complete visible application. LibreOffice is treated as an optional Linux document engine behind the platform-neutral `IWriteDocumentEngine` contract in `src/Haven.Application/Write/IWriteDocumentEngine.cs`.

```text
HUI Write scene
    |
    v
IWriteDocumentEngine
    |
    v
Linux Haven Write helper (planned process boundary)
    |
    v
LibreOfficeKit
    |
    v
LibreOffice Writer/core-nogui runtime
```

## Ownership

- `Haven.Application` owns the engine-neutral contract and request/result types.
- `Haven.Infrastructure` owns configuration/readiness checks for external engine implementations.
- HUI/Desktop owns visible controls, accessibility exposure, input routing, themes, GenUI, AI review flows, and local product state.
- LibreOffice owns authoritative Writer layout/editing state only after a document is opened through this engine.
- The Linux helper is intended to own the native LibreOfficeKit ABI and an isolated LibreOffice user profile so an engine crash or ABI change does not become a HUI-process crash.

The current `NotesDocument` editor remains the production path. Do not make Writer and `NotesDocument` simultaneous equal authorities. A later migration must choose one canonical Writer payload plus Haven-only sidecar metadata and provide an explicit importer from existing Haven documents.

## LibreOfficeKit program path

The path passed to `lok_init_2` must be the directory that directly contains `libsofficeapp.so` or `libmergedlo.so`; LibreOfficeKit appends one of those filenames to the supplied path. Ubuntu packages place these libraries below `/usr/lib/libreoffice/program`, which is therefore the default `LibreOfficeProgramPath` used by this PoC.

The clean Ubuntu 24.04 runtime proof directly observed `/usr/lib/libreoffice/program/libmergedlo.so`, confirming this boundary for that package layout.

## Security boundary

The engine must fail closed. Enabling the feature is not enough to make it available: the Linux host, helper executable, LibreOffice program directory, and required native libraries must all be present. A future helper process must run with an isolated profile, minimal filesystem mounts, no network by default, and no unrestricted UNO-command escape hatch exposed to generated UI or model output.

`WriteEngineCommand` is therefore a semantic application request. Platform implementations must allow-list and validate commands before translating them to LibreOffice-native operations.

The Application contract intentionally does not expose the LibreOffice profile directory and does not name UNO as a capability. Provider-specific profile placement, ABI management, event-loop behaviour, and native command mapping stay behind the Infrastructure/helper boundary.

## Accessibility

Rendered tiles are not sufficient accessibility evidence. The engine contract requires a semantic accessibility snapshot in addition to pixels.

The portable Linux PoC proved that LibreOfficeKit could enable its accessibility state and return both a focused-paragraph payload and caret position on a Writer document. That proves the native data path exists; it does **not** prove a complete HUI accessibility tree, screen-reader behaviour, keyboard navigation announcements, selection semantics, or focus restoration. Those remain release gates.

## PoC files

`eng/linux/write-libreoffice-poc/lok_probe.cxx` now exercises externally observable operations that a normal LibreOfficeKit client can prove without relying on LibreOffice-internal scheduler helpers:

1. initialise LibreOfficeKit from its program directory with an isolated profile;
2. open a Writer document;
3. verify required tiled-rendering, editing, persistence, and accessibility API members are present;
4. obtain document dimensions;
5. render one 512x512 tile;
6. enable/read focused-paragraph and caret accessibility state;
7. insert a unique text marker through LibreOfficeKit `paste()`;
8. save the edited document as ODT;
9. destroy the first document instance;
10. reopen the saved ODT through LibreOfficeKit and render it again;
11. independently inspect the saved ODT payload in CI and require the edit marker to exist in `content.xml`.

`eng/linux/write-libreoffice-poc/run-probe.sh` builds and runs the native probe using the headless `svp` VCL backend. If an output path is omitted it creates a disposable round-trip ODT under `BUILD_DIR`, so persistence/reopen proof remains mandatory rather than becoming an optional path.

### Deliberately not claimed yet

An earlier probe attempted to dispatch `.uno:SelectAll` and immediately verify the selection with `getTextSelection()`. LibreOffice's own internal tests drain its scheduler before observing such command effects; that internal scheduler primitive is not part of the normal external LibreOfficeKit C client boundary. The PoC therefore does not claim that immediate asynchronous UNO command completion, selection state, or bold-format persistence is proven.

Those behaviours belong in the next helper-process stage, where callback/event-loop handling can be implemented deliberately and tested rather than hidden behind timing assumptions.

## Clean-Ubuntu validation

`.github/workflows/validate-write-libreoffice-poc.yml` provides a disposable GitHub-hosted Ubuntu validation path. It installs the Writer/headless runtime plus the LibreOfficeKit development headers, restores/builds the focused .NET project, runs the configuration tests, creates a minimal ODT fixture, executes the native round-trip probe, and independently checks the resulting ODT payload.

Observed successful run: **GitHub Actions run 34145146782**, branch head `ee56800fb66e1a6a1bf2f2451becb6910784dd0a`.

Observed environment and results:

- Ubuntu 24.04-class GitHub-hosted Linux runner;
- `libreoffice-core-nogui`, `libreoffice-writer-nogui`, and `libreofficekit-dev` from the Ubuntu LibreOffice 24.2.7 package family;
- LibreOfficeKit runtime library found at `/usr/lib/libreoffice/program/libmergedlo.so`;
- focused Release build: passed with 0 warnings and 0 errors;
- focused configuration/readiness tests: 4 passed, 0 failed;
- LibreOfficeKit initialisation and Writer document load: passed;
- document dimensions: positive and stable across reopen;
- 512x512x4 tile render: passed, 1,048,576 bytes;
- accessibility call path: focused-paragraph payload returned and caret API callable;
- Writer `paste()` edit: accepted;
- ODT save: passed;
- saved ODT reopen and second render through LibreOfficeKit: passed;
- independent ODT `content.xml` check: persisted edit marker present;
- process cleanup: passed without the cleanup failure seen in the discarded asynchronous-selection experiment.

This is **portable Linux runtime proof**, not CakeOS image/VM acceptance proof.

## Packaging observation

The Ubuntu 24.04 `libreofficekit-dev` package used to compile the disposable C++ probe pulled a large development dependency closure, including GTK/LOKDocView development packages, despite the PoC itself using raw LibreOfficeKit plus the headless `svp` VCL backend.

That dependency closure belongs to the **build environment only**. It is not evidence that the production Haven Write runtime requires GTK, `LOKDocView`, or GNOME. Production packaging must separately prove the smallest runtime closure for the prebuilt Haven helper plus `libreoffice-core-nogui`/`libreoffice-writer-nogui`, without shipping `libreofficekit-dev` merely to obtain headers.

## Evidence state

As of 2026-09-07:

| Evidence | State |
| --- | --- |
| Application engine-neutral contract | **Implemented** |
| Fail-closed Infrastructure configuration/readiness | **Implemented** |
| Native LibreOfficeKit probe | **Implemented** |
| Dedicated clean-Linux CI workflow | **Implemented and executed** |
| Focused Release .NET build | **Passed — 0 warnings, 0 errors** |
| Focused .NET tests | **Passed — 4/4** |
| Native C++ probe build | **Passed on GitHub-hosted Ubuntu** |
| Ubuntu LibreOffice/Writer/LOK development package install | **Passed on GitHub-hosted Ubuntu** |
| LibreOfficeKit runtime initialisation | **Runtime-proven on GitHub-hosted Ubuntu** |
| Writer document open and sizing | **Runtime-proven on GitHub-hosted Ubuntu** |
| Writer 512x512 tile render | **Runtime-proven — 1,048,576-byte tile** |
| LOK focused-paragraph/caret accessibility calls | **Runtime-proven at native API level** |
| Writer text edit via `paste()` | **Runtime-proven** |
| ODT save + LOK reopen + rerender | **Runtime-proven** |
| Persisted edit in ODT payload | **Runtime-proven by independent `content.xml` verification** |
| Async semantic/UNO command completion | **Unvalidated** |
| Selection round-trip / bold-format persistence | **Unvalidated** |
| Approved CakeOS VM package/runtime proof | **Unvalidated — separate mandatory gate** |
| Production helper process/IPC/sandbox | **Not implemented** |
| HUI document viewport integration | **Deferred until CakeOS VM gate passes** |
| Complete HUI accessibility bridge | **Deferred; release gate** |
| Production Write route switched to LibreOffice | **No** |

Do not promote this engine to the production Write route until the same native/runtime slice is directly proven on the approved CakeOS/Ubuntu VM and the helper-process security boundary is implemented. GitHub-hosted runner evidence is deliberately not a substitute for CakeOS VM/image acceptance.
