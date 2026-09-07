# HavenOS Present

This surface is a standalone HavenOS host for the existing Present implementation.

It deliberately reuses:

- `Haven.Desktop.Views.Pages.Present.PresentPage` for the real Present HUI surface.
- `IPresentRepository`, `IPresentExportService`, and `IPresentImportService` from the existing infrastructure registration.
- `PresentEditor` and `PresentPlaybackSession` from the existing presentation engine.

No presentation/document engine is duplicated in this app surface.

## Focused validation

```powershell
dotnet build "HavenOS Apps/Present/HavenOS.Present.csproj" -c Debug
dotnet test "HavenOS Apps/Present/Tests/HavenOS.Present.Tests.csproj" -c Debug
```
