# Haven Write LibreOffice engine boundary

Status: **experimental implementation slice; not runtime-proven**.

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

## Security boundary

The engine must fail closed. Enabling the feature is not enough to make it available: the Linux host, helper executable, LibreOffice installation, and required native libraries must all be present. A future helper process must run with an isolated profile, minimal filesystem mounts, no network by default, and no unrestricted UNO-command escape hatch exposed to generated UI or model output.

`WriteEngineCommand` is therefore a semantic application request. Platform implementations must allow-list/validate commands before translating them to LibreOffice UNO commands.

## Accessibility

Rendered tiles are not sufficient accessibility evidence. The engine contract requires a semantic accessibility snapshot in addition to pixels. The current native PoC probes LibreOfficeKit focused-paragraph and caret APIs, but a complete HUI accessibility bridge remains a release gate.

## PoC files

`eng/linux/write-libreoffice-poc/lok_probe.cxx` exercises the minimum native API surface needed to decide whether the architecture is viable:

1. initialise LibreOfficeKit with an isolated profile;
2. open a Writer document;
3. verify the required unstable API members are present;
4. obtain document dimensions;
5. render one 512x512 tile;
6. enable/access focused-paragraph and caret accessibility APIs;
7. dispatch `.uno:Bold`;
8. optionally save the document.

`eng/linux/write-libreoffice-poc/run-probe.sh` builds and runs that probe using the headless `svp` VCL backend. It deliberately does not install packages. Package installation and VM mutation are separate approved operations.

## Evidence state

As of 2026-09-07:

| Evidence | State |
| --- | --- |
| Contract source written | Implemented |
| Fail-closed configuration/readiness source written | Implemented |
| Native LibreOfficeKit probe source written | Implemented |
| Focused .NET tests written | Implemented, not executed in this pass yet |
| .NET build | Unvalidated |
| Native C++ probe build | Unvalidated |
| Ubuntu package installation | Not performed by this slice |
| LibreOfficeKit runtime initialisation | Unvalidated |
| Writer tile runtime render | Unvalidated |
| Save/reopen runtime proof | Unvalidated |
| HUI viewport integration | Deferred until native/runtime gates pass |
| HUI accessibility bridge | Deferred until native/runtime gates pass |

Do not promote this engine to the production Write route until the Linux runtime gates above have direct evidence.
