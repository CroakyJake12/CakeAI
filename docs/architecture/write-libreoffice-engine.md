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
8. select the Writer document and verify the marker is observable through `getTextSelection()`;
9. dispatch `.uno:Bold` without claiming formatting persistence until a later formatting-state probe exists;
10. when an output path is supplied, save, destroy the original document, reopen the saved document, and require the edit marker to remain selectable.

`eng/linux/write-libreoffice-poc/run-probe.sh` builds and runs that probe using the headless `svp` VCL backend. It deliberately does not install packages. Package installation and VM mutation are separate approved operations.

`.github/workflows/validate-write-libreoffice-poc.yml` is a clean, disposable GitHub-hosted Ubuntu validation path. It installs only the narrow LibreOffice/Writer/LibreOfficeKit build/runtime package set needed by the probe, runs the focused .NET configuration tests, creates a minimal ODT fixture, and executes the native edit/save/reopen probe. A green run is useful portable Linux evidence but **does not replace runtime proof on the approved CakeOS/Ubuntu VM or packaged image**.

## Evidence state

As of 2026-09-07:

| Evidence | State |
| --- | --- |
| Contract source written | Implemented |
| Fail-closed configuration/readiness source written | Implemented |
| Native LibreOfficeKit edit/round-trip probe source written | Implemented |
| Clean-Ubuntu CI workflow written | Implemented; execution pending |
| Focused .NET tests written | Implemented; execution pending |
| .NET build | Unvalidated until CI/local execution is observed |
| Native C++ probe build | Unvalidated until CI/VM execution is observed |
| GitHub-hosted Ubuntu LibreOffice package install | Unvalidated until CI execution is observed |
| LibreOfficeKit runtime initialisation | Unvalidated until CI/VM execution is observed |
| Writer tile runtime render | Unvalidated until CI/VM execution is observed |
| Writer edit in-memory proof | Unvalidated until CI/VM execution is observed |
| Save/reopen edit round-trip proof | Unvalidated until CI/VM execution is observed |
| Approved CakeOS VM package/runtime proof | Unvalidated; separate mandatory gate |
| HUI viewport integration | Deferred until native/runtime gates pass |
| HUI accessibility bridge | Deferred until native/runtime gates pass |

Do not promote this engine to the production Write route until the Linux runtime gates above have direct evidence, and do not treat GitHub-hosted runner evidence as a substitute for the approved CakeOS VM acceptance gate.
