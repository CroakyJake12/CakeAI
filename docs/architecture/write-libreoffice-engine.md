# Haven Write LibreOffice engine boundary

Status: **experimental implementation slice; not runtime-proven on the approved CakeOS VM**.

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

The path passed to `lok_init_2` must be the directory that directly contains `libsofficeapp.so` or `libmergedlo.so`; LibreOfficeKit appends one of those filenames to the supplied path. Ubuntu packages place these libraries below `/usr/lib/libreoffice/program`, which is therefore the default `LibreOfficeProgramPath` used by this PoC. Do not pass `/usr/lib/libreoffice` unless that package layout changes and direct runtime evidence confirms it.

## Security boundary

The engine must fail closed. Enabling the feature is not enough to make it available: the Linux host, helper executable, LibreOffice program directory, and required native libraries must all be present. A future helper process must run with an isolated profile, minimal filesystem mounts, no network by default, and no unrestricted UNO-command escape hatch exposed to generated UI or model output.

`WriteEngineCommand` is therefore a semantic application request. Platform implementations must allow-list/validate commands before translating them to LibreOffice UNO commands.

## Accessibility

Rendered tiles are not sufficient accessibility evidence. The engine contract requires a semantic accessibility snapshot in addition to pixels. The current native PoC probes LibreOfficeKit focused-paragraph and caret APIs, but a complete HUI accessibility bridge remains a release gate.

## PoC files

`eng/linux/write-libreoffice-poc/lok_probe.cxx` exercises the minimum native API surface needed to decide whether the architecture is viable:

1. initialise LibreOfficeKit from its program directory with an isolated profile;
2. open a Writer document;
3. verify the required unstable API members are present;
4. obtain document dimensions;
5. render one 512x512 tile;
6. enable/access focused-paragraph and caret accessibility APIs;
7. insert a unique text marker through LibreOfficeKit `paste()`;
8. save the edited document to ODT;
9. destroy the first document instance;
10. reopen the saved ODT through LibreOfficeKit and render it again;
11. independently inspect the saved ODT payload in CI and require the edit marker to exist in `content.xml`.

`eng/linux/write-libreoffice-poc/run-probe.sh` builds and runs the native probe using the headless `svp` VCL backend. If an output path is omitted it creates a disposable round-trip ODT under `BUILD_DIR`, so persistence/reopen proof remains mandatory rather than becoming an optional path.

### Deliberately not claimed yet

An earlier probe attempted to dispatch `.uno:SelectAll` and immediately verify the selection with `getTextSelection()`. LibreOffice's own internal tests drain its scheduler before observing such command effects; that internal scheduler primitive is not part of the normal external LibreOfficeKit C client boundary. The PoC therefore does not claim that immediate asynchronous UNO command completion, selection state, or bold-format persistence is proven.

Those behaviours belong in the next helper-process stage, where callback/event-loop handling can be implemented deliberately and tested rather than hidden behind timing assumptions.

## Clean-Ubuntu validation

`.github/workflows/validate-write-libreoffice-poc.yml` provides a disposable GitHub-hosted Ubuntu validation path. The first job installs the Writer/headless runtime plus LibreOfficeKit development headers, restores/builds the focused .NET project, runs the configuration tests, creates a minimal ODT fixture, executes the native round-trip probe, and publishes a standalone prebuilt probe artifact.

Observed successful full workflow run: **GitHub Actions run 34156074150**, branch head `7daa8d5d45e0ad35dfa319ce44178bf3983f46f6`.

Build-host results:

- pinned GitHub-hosted Ubuntu 24.04 runner;
- `libreoffice-core-nogui`, `libreoffice-writer-nogui`, and `libreofficekit-dev` from the Ubuntu LibreOffice 24.2.7 package family;
- LibreOfficeKit runtime library found below `/usr/lib/libreoffice/program`;
- focused Release build: passed;
- focused configuration/readiness tests: 4 passed, 0 failed;
- standalone native probe built and published;
- LibreOfficeKit initialisation and Writer document load: passed;
- document dimensions: positive and stable across reopen;
- 512x512x4 tile render: passed, 1,048,576 bytes;
- accessibility call path: focused-paragraph payload returned and caret API callable;
- Writer `paste()` edit: accepted;
- ODT save: passed;
- saved ODT reopen and second render through LibreOfficeKit: passed;
- independent ODT `content.xml` check: persisted edit marker present;
- process cleanup: passed.

This is **portable Linux runtime proof**, not CakeOS image/VM acceptance proof.

## Runtime-only packaging proof

The workflow now contains a second job that runs only after the build/probe job succeeds. It downloads the already-built native probe into a fresh `ubuntu:24.04` container and installs only:

- `libreoffice-core-nogui` `4:24.2.7-0ubuntu0.24.04.6`;
- `libreoffice-writer-nogui` `4:24.2.7-0ubuntu0.24.04.6`.

Observed successful job: **`Prove Writer runtime without LibreOfficeKit/GTK development packages`**, Actions run `34156074150`, exact source head `7daa8d5d45e0ad35dfa319ce44178bf3983f46f6`.

Observed runtime-only evidence:

- no compiler or LibreOfficeKit headers were installed in the runtime container;
- `libreofficekit-dev`, `liblibreofficekitgtk`, `gir1.2-lokdocview-0.1`, and `libreoffice-gtk3` were explicitly required to be absent;
- no installed package matching `libgtk-*` was permitted by the gate;
- `ldd` for the prebuilt Haven probe resolved only its ordinary C/C++ runtime dependencies and showed no direct GTK, LOKDocView, or LibreOfficeKit-GTK dependency;
- the headless runtime package transaction installed 119 packages (plus one base-image package upgrade), consuming about 366 MB in the minimal container;
- LibreOfficeKit 24.2.7.2 initialised successfully from the headless runtime;
- document size was 12808 x 16408 twips before and after reopen;
- 512x512x4 tile render produced 1,048,576 bytes;
- focused-paragraph/caret accessibility calls executed;
- the Writer edit marker was accepted via `paste()`;
- ODT save succeeded;
- the saved ODT reopened and rendered successfully;
- independent inspection outside the container confirmed the edit marker persisted in `content.xml`;
- the runtime-only job completed successfully.

This closes the question of whether this PoC needs `libreofficekit-dev`, GTK, LOKDocView, or GNOME packages at runtime: **the tested raw-LibreOfficeKit Writer slice does not**. It does not prove that the full future helper/package image has no additional dependencies, and it is not approved CakeOS VM/image evidence.

## Packaging observation

`libreofficekit-dev` remains a build-time dependency for compiling the disposable C++ probe and pulls a much larger development closure including GTK/LOKDocView development packages. The successful runtime-only job proves those development/UI packages are not required by the tested prebuilt raw-LibreOfficeKit Writer runtime slice.

The currently demonstrated headless runtime closure is still substantial (about 366 MB added to a minimal Ubuntu 24.04 container and 119 newly installed packages), so production image-size optimisation remains separate packaging work. Do not infer that only the two explicitly requested LibreOffice package names are physically present; their transitive runtime dependencies are required.

## Next blockers

The next **acceptance blocker** is direct execution of the same native/runtime slice on the approved CakeOS/Ubuntu VM. Portable CI cannot close that gate. That environment must remain untouched without fresh explicit user authorization.

Within the isolated draft scope, the next smallest **technical blocker** that can be addressed independently is asynchronous semantic-command completion. The PoC can synchronously paste, render, save, and reopen, but it does not yet provide a helper-owned LibreOffice event/callback loop that can prove semantic commands such as selection and bold have completed before state is read. A minimal next slice is therefore a disposable helper/event-loop probe that:

1. owns LibreOfficeKit on one serialized engine thread;
2. registers/drains the supported external callback/event path rather than using sleeps;
3. dispatches an allow-listed semantic command (`SelectAll` first, then `Bold`);
4. observes selection/format state only after completion evidence;
5. saves/reopens and independently verifies the formatting result;
6. remains a standalone PoC, not the production HUI route.

That technical slice does not replace the mandatory CakeOS VM acceptance gate and must not be treated as permission to modify the approved VM or production checkout.

## Evidence state

As of 2026-09-07:

| Evidence | State |
| --- | --- |
| Application engine-neutral contract | **Implemented** |
| Fail-closed Infrastructure configuration/readiness | **Implemented** |
| Native LibreOfficeKit probe | **Implemented** |
| Dedicated clean-Linux CI workflow | **Implemented and executed** |
| Focused Release .NET build | **Passed** |
| Focused .NET tests | **Passed — 4/4** |
| Native C++ probe build | **Passed on GitHub-hosted Ubuntu 24.04** |
| Ubuntu LibreOffice/Writer/LOK development package install | **Passed on build host** |
| LibreOfficeKit runtime initialisation | **Runtime-proven on GitHub-hosted Ubuntu** |
| Writer document open and sizing | **Runtime-proven on GitHub-hosted Ubuntu** |
| Writer 512x512 tile render | **Runtime-proven — 1,048,576-byte tile** |
| LOK focused-paragraph/caret accessibility calls | **Runtime-proven at native API level** |
| Writer text edit via `paste()` | **Runtime-proven** |
| ODT save + LOK reopen + rerender | **Runtime-proven** |
| Persisted edit in ODT payload | **Runtime-proven by independent `content.xml` verification** |
| Prebuilt probe on core-nogui + writer-nogui without LOK dev/GTK packages | **Runtime-proven in minimal Ubuntu 24.04 container** |
| Direct GTK/LOKDocView dependency in tested probe | **Rejected by runtime-only gate; none observed** |
| Async semantic/UNO command completion | **Unvalidated — next isolated technical blocker** |
| Selection round-trip / bold-format persistence | **Unvalidated** |
| Approved CakeOS VM package/runtime proof | **Unvalidated — next acceptance blocker; authorization required** |
| Production helper process/IPC/sandbox | **Not implemented** |
| HUI document viewport integration | **Deferred until CakeOS VM gate passes** |
| Complete HUI accessibility bridge | **Deferred; release gate** |
| Production Write route switched to LibreOffice | **No** |

Do not promote this engine to the production Write route until the same native/runtime slice is directly proven on the approved CakeOS/Ubuntu VM and the helper-process security boundary is implemented. GitHub-hosted runner evidence is deliberately not a substitute for CakeOS VM/image acceptance.