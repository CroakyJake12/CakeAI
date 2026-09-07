# HavenOS Browse app surface

This directory owns the bounded standalone Browse app slice for HavenOS.

## Functional boundary

`BrowseAppRoute` owns Browse route identity (`browse`, `browser`, and `web`) and delegates domain/URL/search navigation to the existing `Haven.Browser.BrowserSessionService`. It does not create another WebView host, URL normalizer, HTTP fallback, browser store, or automation policy.

The exercised journey is:

`Browse route -> BrowseAppRoute.NavigateAsync -> BrowserSessionService.NavigateAsync -> existing IEmbeddedBrowserHost/native browser capability`

A bare domain such as `example.com` therefore keeps the existing browser behavior and is normalized to HTTPS by `BrowserSessionService` before it reaches the host.

## Provenance

The authoritative starting point is `havenos-main` at `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.

The repository's HavenOS provenance ledger records the Browse route source commit as `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b`, which added the `web` alias while retaining the existing `HavenAppRouteKind.Browse` / `HavenSurface.Browse` path. The reused browser capability remains under `src/Haven.Browser`, principally `Session/BrowserSessionService.cs` for this slice.

No external donor code is introduced by this migration slice; it reuses code already contained in the Haven repository and remains under the repository's existing licensing boundary.

## Visual boundary

This slice contains no replacement Browse visuals and makes no visual-fidelity claim. Future UI work must be checked against the canonical Browse mockup asset before fidelity is claimed.

## Focused validation

From the repository root:

```powershell
dotnet build ".\HavenOS Apps\Browse\HavenOS.Apps.Browse.csproj" -c Release
dotnet test ".\HavenOS Apps\Browse\Tests\HavenOS.Apps.Browse.Tests.csproj" -c Release
```

The tests cover the existing route aliases, reject unrelated routes before navigation, exercise bare-domain HTTPS normalization through the real `BrowserSessionService`, and verify cancellation reaches the existing embedded-browser host seam.
