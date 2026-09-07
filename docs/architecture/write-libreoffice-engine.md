# Haven Write LibreOffice engine boundary

Status: **experimental implementation slice; portable Linux runtime-proven, but not runtime-proven on the approved CakeOS VM**.

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

`WriteEngineCommand` is therefore a semantic application request. Platform implementations must allow-list and validate commands before translating them to LibreOffice UNO commands.

## Accessibility

Rendered tiles are not sufficient accessibility evidence. The engine contract requires a semantic accessibility snapshot in addition to pixels. The native PoCs exercise LibreOfficeKit focused-paragraph and caret APIs, but a complete HUI accessibility bridge remains a release gate.

## PoC files

`eng/linux/write-libreoffice-poc/lok_probe.cxx` exercises the baseline native API surface:

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
11. independently inspect the saved ODT payload in CI and require the edit marker in `content.xml`.

`eng/linux/write-libreoffice-poc/run-probe.sh` builds and runs that probe using the headless `svp` VCL backend.

`eng/linux/write-libreoffice-poc/lok_semantic_probe.cxx` separately exercises callback-driven semantic command completion. It deliberately uses the external LibreOfficeKit client loop rather than LibreOffice-internal scheduler helpers:

1. set `SAL_LOK_OPTIONS=unipoll` and enter the public LibreOfficeKit `runLoop`;
2. load the Writer document and register a document callback;
3. insert a unique semantic proof marker;
4. dispatch `.uno:SelectAll` with `notifyWhenFinished=true`;
5. wait for `LOK_CALLBACK_UNO_COMMAND_RESULT` for Select All before reading selected text;
6. require the completed selection to contain the proof marker;
7. dispatch `.uno:Bold` with `notifyWhenFinished=true`;
8. wait for its command-result callback;
9. save the ODT only after Bold completion;
10. independently parse the ODT and require the proof marker to resolve to a bold text style.

`eng/linux/write-libreoffice-poc/run-semantic-probe.sh` provides a disposable profile and an outer process watchdog, but command completion itself is callback-driven: there are no sleeps and no `Scheduler::ProcessEventsToIdle()` dependency. `verify-semantic-odt.py` independently resolves ODT style inheritance and verifies persisted bold formatting.

### Discarded immediate-command experiment

An earlier probe dispatched `.uno:SelectAll` and immediately queried `getTextSelection()`. LibreOffice's internal tests can drain its scheduler before observing command effects, but that internal scheduler primitive is not the normal external LibreOfficeKit C client boundary. That immediate observation experiment failed and is still deliberately excluded from evidence.

The replacement semantic probe uses the supported external `runLoop`/unipoll callback path and waits for `LOK_CALLBACK_UNO_COMMAND_RESULT` before reading command effects. The successful callback-driven proof below supersedes the old timing assumption; it does not retroactively turn the discarded experiment into evidence.

## Clean-Ubuntu validation

`.github/workflows/validate-write-libreoffice-poc.yml` provides a disposable GitHub-hosted Ubuntu validation path. Its first job installs the Writer/headless runtime plus LibreOfficeKit development headers, restores/builds the focused .NET project, runs the configuration tests, creates a minimal ODT fixture, executes both native probes, independently verifies the normal edit and bold-format persistence, and publishes standalone prebuilt probes.

Baseline packaging/runtime run: **GitHub Actions run `34156074150`**, source head `7daa8d5d45e0ad35dfa319ce44178bf3983f46f6`.

Latest semantic-command run: **GitHub Actions run `34158386834`**, exact semantic source head `8e439c7869d144547615520f2cd98e882b7f4b1a`.

Build-host evidence on Ubuntu 24.04 includes:

- focused Release .NET build: passed;
- focused configuration/readiness tests: 4 passed, 0 failed;
- both native C++ probes compiled with `-Wall -Wextra -Wpedantic -Werror`;
- LibreOfficeKit 24.2.7.2 initialisation and Writer document load: passed;
- document dimensions: positive and stable across reopen;
- 512x512x4 tile render: passed, 1,048,576 bytes;
- focused-paragraph/caret accessibility call path: executed;
- Writer `paste()` edit: accepted;
- ODT save, LOK reopen and rerender: passed;
- persisted normal edit marker: independently verified;
- `.uno:SelectAll` command-result callback: observed;
- selected text was read only after that completion callback and contained the semantic proof marker;
- `.uno:Bold` command-result callback: observed;
- a selection callback was observed;
- `.uno:Bold=true` state callback was observed;
- semantic ODT save occurred only after Bold completion;
- independent ODT inspection resolved the semantic proof marker to bold style `P1`;
- both jobs in run `34158386834` completed successfully.

This is **portable Linux runtime proof**, not CakeOS image/VM acceptance proof.

## Runtime-only packaging proof

The workflow's second job runs only after the build/probe job succeeds. It downloads the already-built native probes into a fresh `ubuntu:24.04` container and installs only:

- `libreoffice-core-nogui` `4:24.2.7-0ubuntu0.24.04.6`;
- `libreoffice-writer-nogui` `4:24.2.7-0ubuntu0.24.04.6`.

The runtime gate explicitly requires `libreofficekit-dev`, `liblibreofficekitgtk`, `gir1.2-lokdocview-0.1`, `libreoffice-gtk3`, and installed `libgtk-*` packages to be absent. `ldd` for both prebuilt probes must also show no direct GTK, LOKDocView, or LibreOfficeKit-GTK dependency.

On semantic run `34158386834`, exact source head `8e439c7869d144547615520f2cd98e882b7f4b1a`, the runtime-only job passed with:

- no compiler or LibreOfficeKit development headers installed in the container;
- forbidden GTK/LOKDocView/development package checks passing;
- both prebuilt probes resolving only ordinary C/C++ runtime dependencies directly;
- 119 newly installed packages plus one base-image package upgrade;
- about 116 MB downloaded and about 366 MB additional disk usage;
- LibreOfficeKit 24.2.7.2 initialised from the headless runtime;
- baseline render/edit/save/reopen proof passed;
- `.uno:SelectAll` command-result callback observed;
- selection read after completion: 72 bytes, `text/plain;charset=utf-8`, containing the semantic marker;
- `.uno:Bold` command-result callback observed;
- selection callback observed;
- `Bold=true` state callback observed;
- semantic ODT saved after completed Bold;
- independent verification outside the container resolved the persisted semantic marker to bold style `P1`;
- the runtime-only job completed successfully.

This closes the tested PoC question of whether `libreofficekit-dev`, GTK, LOKDocView, or GNOME packages are required at runtime: **they are not required by the tested raw-LibreOfficeKit Writer slice, including callback-driven Select All/Bold semantics**. It does not prove that the future production helper/package image has no additional dependencies.

## Packaging observation

`libreofficekit-dev` remains a build-time dependency for compiling the disposable C++ probes and pulls a larger development closure including GTK/LOKDocView development packages. Those development/UI packages are not part of the demonstrated runtime requirement.

The demonstrated headless runtime closure is still substantial: about 366 MB added to a minimal Ubuntu 24.04 container and 119 newly installed packages. Production image-size optimisation remains separate packaging work. Do not infer that only the two explicitly requested LibreOffice package names are physically present; their transitive runtime dependencies are required.

## Next blockers

The next **acceptance blocker** remains direct execution of the same native/runtime slice on the approved CakeOS/Ubuntu VM. Portable CI cannot close that gate. That environment must remain untouched without fresh explicit user authorization.

Within the isolated draft scope, callback-driven semantic completion is no longer the technical blocker. The next smallest **technical blocker** is the real helper-process boundary: turn the proven single-threaded LibreOfficeKit loop into a disposable `haven-write-engine` process with a narrow local IPC protocol, command allow-listing, explicit request/completion correlation, isolated profile ownership, deterministic shutdown, and crash containment. That slice should remain independent of the production Write route and HUI wiring until the approved CakeOS VM acceptance gate passes.

After helper/IPC viability, remaining gates include sandbox/network denial, lifecycle supervision/restart behaviour, the first HUI viewport, complete HUI accessibility exposure, and explicit migration of existing Haven Write documents.

## Evidence state

As of 2026-09-07:

| Evidence | State |
| --- | --- |
| Application engine-neutral contract | **Implemented** |
| Fail-closed Infrastructure configuration/readiness | **Implemented** |
| Baseline native LibreOfficeKit probe | **Implemented and runtime-proven on generic Ubuntu 24.04** |
| Callback-driven semantic command probe | **Implemented and runtime-proven on generic Ubuntu 24.04** |
| Dedicated clean-Linux CI workflow | **Implemented and executed** |
| Focused Release .NET build | **Passed** |
| Focused .NET tests | **Passed — 4/4** |
| Native C++ probes with strict warnings | **Built successfully** |
| LibreOfficeKit runtime initialisation | **Runtime-proven** |
| Writer document open and sizing | **Runtime-proven** |
| Writer 512x512 tile render | **Runtime-proven — 1,048,576-byte tile** |
| LOK focused-paragraph/caret accessibility calls | **Runtime-proven at native API level** |
| Writer text edit via `paste()` | **Runtime-proven** |
| ODT save + LOK reopen + rerender | **Runtime-proven** |
| Persisted edit in ODT payload | **Runtime-proven by independent `content.xml` verification** |
| Prebuilt probes on core-nogui + writer-nogui without LOK dev/GTK packages | **Runtime-proven in minimal Ubuntu 24.04 container** |
| Direct GTK/LOKDocView dependency in tested probes | **Rejected by runtime-only gate; none observed** |
| Select All asynchronous command completion | **Runtime-proven via `LOK_CALLBACK_UNO_COMMAND_RESULT`** |
| Selected text after completed Select All | **Runtime-proven; semantic marker observed** |
| Bold asynchronous command completion | **Runtime-proven via `LOK_CALLBACK_UNO_COMMAND_RESULT`** |
| Bold state callback | **Runtime-proven — `Bold=true` observed** |
| Bold-format persistence | **Runtime-proven by independent ODT style resolution (`P1`)** |
| Approved CakeOS VM package/runtime proof | **Unvalidated — next acceptance blocker; authorization required** |
| Production helper process/local IPC/sandbox | **Not implemented — next isolated technical blocker** |
| HUI document viewport integration | **Deferred until CakeOS VM gate passes** |
| Complete HUI accessibility bridge | **Deferred; release gate** |
| Existing Haven Write document migration | **Not implemented** |
| Production Write route switched to LibreOffice | **No** |

Do not promote this engine to the production Write route until the same native/runtime slice is directly proven on the approved CakeOS/Ubuntu VM and the helper-process security boundary is implemented. GitHub-hosted runner evidence is deliberately not a substitute for CakeOS VM/image acceptance.