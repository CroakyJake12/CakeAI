using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Present.Tests;

public sealed class PresentAppSurfaceTests
{
    [AvaloniaFact]
    public async Task Launches_existing_surface_and_reuses_editing_and_playback_engines()
    {
        var repository = new InMemoryPresentRepository();
        using var host = PresentAppHost.Create(repository, new NoOpPresentExporter());


            await host.InitializeAsync();



            PresentDocument document = Assert.IsType<PresentDocument>(host.Document);

            Assert.Single(document.Slides);

            var editor = new PresentEditor(document);
            PresentSlide firstSlide = editor.SelectedSlide;

            Assert.True(editor.SetDocumentTitle("HavenOS Present"));
            Assert.True(editor.SetSlideTitle(firstSlide.Id, "Opening"));
            PresentSlide secondSlide = editor.AddSlide(firstSlide.Id);
            Assert.True(editor.SetSlideTitle(secondSlide.Id, "Playback"));

            var playback = new PresentPlaybackSession(document);
            Assert.Equal("Opening", playback.CurrentSlide.Title);
            Assert.True(playback.Next());
            Assert.Equal("Playback", playback.CurrentSlide.Title);
            Assert.Equal(2, playback.Frame.SlideCount);


    }

    private sealed class NoOpPresentExporter : IPresentExportService
    {
        public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];

        public Task<string> ExportAsync(
            PresentDocument document,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(destinationPath);
        }
    }

    private sealed class InMemoryPresentRepository : IPresentRepository
    {
        private readonly Dictionary<Guid, PresentDocument> _documents = [];

        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<PresentDocumentSummary> summaries = _documents.Values
                .OrderByDescending(document => document.UpdatedAt)
                .Select(document => new PresentDocumentSummary(
                    document.Id,
                    document.Title,
                    document.UpdatedAt,
                    document.Version,
                    document.Slides.Count,
                    RecoveredFromBackup: false))
                .ToArray();
            return Task.FromResult(summaries);
        }

        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.TryGetValue(documentId, out PresentDocument? document);
            return Task.FromResult(document);
        }

        public Task<PresentSaveResult> SaveAsync(
            PresentDocument document,
            string reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            int version = document.Version + 1;
            _documents[document.Id] = document;
            return Task.FromResult(new PresentSaveResult(
                document.Id,
                version,
                DateTimeOffset.UtcNow,
                "memory://present/current",
                "memory://present/backup"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.Remove(documentId);
            return Task.CompletedTask;
        }
    }
}
