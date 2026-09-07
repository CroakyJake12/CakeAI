# Studio + Experiences — CORE Integration Handoff

Worker product commit: `2d09f07808dd93a125cb46e82ab5a389e81326ca`
Base: authoritative `main` at `856b29b57881ebe1f195428fcec7ef483f68cf0d`
Worker branch: `worker08/studio-experiences-launch-20260825`

## Identity rule

Do not repurpose `HavenMode.Studio`. It is still the historical Projects runtime internally. The visible product currently opened from `OpenModeHomeAsync()` already renders that surface as **Projects**. Keep that runtime and move its visible app registration/key to `projects`. The new creation hub gets visible key `studio`.

## Global files CORE owns

The worker intentionally did not edit these convergence hotspots:

- `src/Haven.Application/Modes/BuiltInModeSeed.cs`
- `src/Haven.Desktop/Views/Shell/HavenAppRoutePolicy.cs`
- `src/Haven.Desktop/Interface/Shell/MainView.axaml.cs`

## Built-in mode seed

Current conflicting built-in entry is GUID `a0000000-0000-0000-0000-000000000004`, key `studio`, name `Studio`, base mode `HavenMode.Studio`. CORE should preserve that GUID/runtime identity but expose it as **Projects** with key `projects`.

Add a new built-in definition for visible **Studio** with a fresh stable GUID and key `studio`. Add **Experiences** with another fresh stable GUID and key `experiences`. These can use a neutral base mode because their routing must be explicit in `HavenAppRoutePolicy`/`LaunchAppAsync`; do not let either fall through to historical `HavenMode.Studio`.

## Shell route additions

Add explicit route kinds for `Studio` and `Experiences` in `HavenAppRoutePolicy` (and an explicit `Projects` route if the current `ModeWorkspace` fallback would otherwise depend on the old `studio` key). `MainView.LaunchAppAsync` is the single launch-routing contract.

Suggested launch branches:

```csharp
else if (route.Kind == HavenAppRouteKind.Studio)
{
    if (openInNewTab) AddFallbackTab();
    OpenStudioHub();
}
else if (route.Kind == HavenAppRouteKind.Experiences)
{
    if (openInNewTab) AddFallbackTab();
    OpenExperiences();
}
```

Do not route the new Studio through `NavigateModeAsync(HavenMode.Studio, ...)`; that opens Projects.

## New Studio page construction

Worker type: `Haven.Desktop.Views.Pages.Studio.StudioHubPage`.

Constructor requires one delegate:

```csharp
new StudioHubPage(HandleStudioCreationIntentAsync)
```

The handler should switch on `StudioCreationIntent.DestinationKind`:

### `App`

Resolve `intent.AppKey` through `_modeRegistry.GetModeByKeyAsync(...)` and call the existing `LaunchAppAsync(registered, openInNewTab: false)`. This keeps Imagine/Write/Present/Data/Canvas/Boards on the canonical app routing contract instead of duplicating page construction.

Current Studio catalog app keys are:

- `imagine` — Images, Videos, Audio
- `write` — Text Document, PDF, Note
- `present` — Presentation
- `data` — Spreadsheet, Database
- `canvas` — Canvas
- `boards` — Board

The optional `SeedPrompt` on these intents is a creation hint (`image`, `video`, `audio`, `document`, `pdf`, `note`, `spreadsheet`, `database`). If the destination already has a typed/new-project entry point, use it; otherwise launch the real destination honestly rather than inventing a parallel editor.

### `ProjectCreator`

Use the existing `OpenProjectCreatorAsync()` seam at `MainView.axaml.cs` around the current project-home code. It currently constructs:

```csharp
var page = new ProjectCreatorPageViewModel(_projectCreator, OpenCreatedProjectAsync);
AddOrSelectTab("new-project", "New project", page, true, HavenSurface.Studio);
```

For Studio, construct the same view model and set `page.Prompt = intent.SeedPrompt ?? string.Empty` before adding the tab. Do not add another project creator.

### `ExperienceBuilder`

Call the existing GenUI generation flow with `intent.SeedPrompt`. Use the same generation path already used by Haven's generated UI creation; do not create another runtime or store.

### `InHouse` / `text-to-speech`

Open `Haven.Desktop.Views.Pages.Studio.TextToSpeechPage`, passing the existing singleton `ISpeechOutputService`. Resolve the same service instance already used by Call/voice preview/read-aloud; do not instantiate a new speech implementation.

## Experiences page construction

Worker type: `Haven.Desktop.Views.Pages.Experiences.ExperiencesPage`.

Constructor:

```csharp
new ExperiencesPage(
    _genUiApps,
    _genUiSessions,
    _generativeUiEventRouter,
    _genUiInstances,
    seedPrompt => OpenGenUiGenerationAsync(seedPrompt))
```

Use the actual existing field names in `MainView`; the important dependency types are:

- `IGenUiAppRepository`
- `GenUiAppSessionService`
- `GenerativeUiEventRouter`
- `GenUiInstanceStore`

`MainView` already receives/owns these dependencies, so no new GenUI DI registration is required. The page reads pinned/recent definitions from `IGenUiAppRepository`, opens through `GenUiAppSessionService`, and renders through `HavenGenUiSceneSurface`.

## Tab helpers

Suggested wrappers:

```csharp
private void OpenStudioHub()
{
    var page = new StudioHubPage(HandleStudioCreationIntentAsync);
    AddOrSelectTab("studio-home", "Studio", page, false, HavenSurface.Studio);
}

private void OpenExperiences()
{
    var page = new ExperiencesPage(/* existing GenUI dependencies */, OpenGenUiGenerationAsync);
    AddOrSelectTab("experiences-home", "Experiences", page, false, HavenSurface.Studio);
}
```

Avoid reusing `studio-home` for Projects. The current Projects home uses that tab key in `OpenModeHomeAsync`; rename that historical tab key to something like `projects-home` during CORE convergence so Studio and Projects cannot alias the same tab.

## Current Projects seam

`MainView.OpenModeHomeAsync()` currently detects `CurrentMode == HavenMode.Studio`, builds `WorkspaceHomePageViewModel`, then calls `CreateNativeProjectsPage(source)` and labels the tab `Projects`. Keep this logic for Projects. Its current tab key is `studio-home` and should be changed to `projects-home` when the new Studio tab is introduced.

`OpenNewContainer()` also treats `HavenMode.Studio` as project creation. That remains correct for the historical Projects runtime.

## Back navigation

Studio destination launches should create/select the normal destination tab and leave the Studio hub tab present. Experiences should likewise remain a tab so returning to its library is ordinary tab navigation. Do not inject custom back-stack state unless the current shell already requires it.

## Validation evidence from worker

- Debug build after Studio, Experiences, and TTS: exit code `0`, zero diagnostics.
- Final full test run: `657/658` passed.
- Sole remaining failure was unrelated: `Haven.Desktop.Tests.NotesMediaAiViewTests.SelectedMediaShowsEvidenceBoundProposalControlsAndReviewCard`.
- New TTS tests compiled and passed; test count increased from 656 to 658.
- Worker repository was clean immediately after product commit.
- Mockup Reference Lab had no registered Haven mockups (`/api/mockups/list` returned `[]`), so automated `studio-hub` visual comparison was unavailable.

## Required CORE smoke checks after integration

1. Launcher shows **Projects**, **Studio**, and **Experiences** as distinct entries.
2. Projects opens the existing project library/workspace, not Studio hub.
3. Studio opens `StudioHubPage`.
4. Studio Images/Video/Audio route to Imagine.
5. Studio Windows App/Android App/Haven App/Game/Website/Agent/Plugin/Skill/Widget open the existing Project Creator with a meaningful seed prompt.
6. Studio Experiences opens the existing GenUI creation flow.
7. Studio Text to Speech opens the real TTS page; unavailable speech provider produces an honest disabled state.
8. Text Document/PDF/Note/Presentation/Spreadsheet/Database/Canvas/Board route to their existing real apps.
9. Experiences lists persisted recent/pinned GenUI definitions, opens one through the existing renderer, and pin/unpin survives refresh.
10. Leaving one Experience and opening another persists the current session state.
11. No Studio tile silently does nothing.
12. `studio-home`/`projects-home` tab identities are distinct.
