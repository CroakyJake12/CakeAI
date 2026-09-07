# HavenOS Images

This directory contains the first bounded standalone Images app surface for HavenOS.

## Implemented journey

1. Launch the `HavenOS.Images` Avalonia desktop app directly.
2. Choose **Open image** and select a local PNG, JPEG, BMP, GIF, or WebP file.
3. Images asks Avalonia/Skia to decode the selected file and shows the decoded bitmap plus its pixel dimensions.
4. **Previous** and **Next** browse other files in the same directory whose extensions are in the Images picker policy, ordered by file name.
5. Decode, file-system, and picker failures are shown as status text rather than being presented as successful capability.

The extension list is a picker/navigation policy, not a promise that every file carrying one of those extensions will decode. Actual decoding is delegated to the Avalonia runtime and corrupt/unsupported payloads fail closed in the UI.

## Explicit non-capabilities

This slice does **not** claim image editing, AI generation, export/conversion, metadata editing, cloud libraries, catalog persistence, or integration with the existing `imagine` creative workspace. It also does not register a new shared-shell route; the project is directly launchable so the app surface remains isolated from concurrent shell/HUI lanes.

## Focused validation

```powershell
dotnet build "HavenOS Apps/Images/HavenOS.Images.csproj" -c Release
dotnet test "HavenOS Apps/Images/Tests/HavenOS.Images.Tests.csproj" -c Release
```
