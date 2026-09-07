using Haven.Application;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using SkiaSharp;

namespace Haven.Desktop.Tests;

public sealed class OverlayRegionCaptureServiceTests
{
    [Fact]
    public async Task Crops_exact_captured_context_without_starting_a_second_picker()
    {
        var root = NewDirectory();
        string? cropPath = null;
        try
        {
            var share = new FakeShare(CreateImage(root, 200, 100));
            var visual = new OverlayVisualContextCaptureService(share, new TestPaths(root));
            var captured = await visual.CaptureAsync(TestContext.Current.CancellationToken);
            var service = new OverlayRegionCaptureService();

            var context = await service.CreateRegionAsync(
                captured,
                new HavenRect(.25, .25, .5, .5),
                200,
                100,
                TestContext.Current.CancellationToken);
            cropPath = context.MediaReference;

            Assert.Equal(1, share.StartCount);
            Assert.Equal(1, share.StopCount);
            Assert.Equal(OverlayContextKind.Region, context.Kind);
            Assert.Equal(new OverlaySelectionBounds(.25, .25, .5, .5), context.Provenance.Bounds);
            Assert.Equal(cropPath, Assert.Single(context.Attachments).Id);
            Assert.Equal(cropPath, Assert.Single(context.SelectedItems).MediaReference);
            Assert.True(File.Exists(cropPath!));
            using var cropped = SKBitmap.Decode(cropPath);
            Assert.NotNull(cropped);
            Assert.Equal(100, cropped.Width);
            Assert.Equal(50, cropped.Height);
        }
        finally
        {
            VisionRegionCropper.DeleteTemporary(cropPath);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Invalid_selection_is_rejected_before_crop_and_existing_region_stays_intact()
    {
        var root = NewDirectory();
        string? firstPath = null;
        try
        {
            var share = new FakeShare(CreateImage(root, 120, 80));
            var visual = new OverlayVisualContextCaptureService(share, new TestPaths(root));
            var captured = await visual.CaptureAsync(TestContext.Current.CancellationToken);
            var service = new OverlayRegionCaptureService();
            var first = await service.CreateRegionAsync(captured, new HavenRect(.1, .1, .3, .3), 120, 80, TestContext.Current.CancellationToken);
            firstPath = first.MediaReference;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                service.CreateRegionAsync(captured, new HavenRect(.8, .2, .3, .2), 120, 80, TestContext.Current.CancellationToken));

            Assert.True(File.Exists(firstPath!));
            Assert.Equal(1, share.StartCount);
        }
        finally
        {
            VisionRegionCropper.DeleteTemporary(firstPath);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Square_preview_letterbox_maps_selection_back_to_full_two_to_one_source()
    {
        var root = NewDirectory();
        string? cropPath = null;
        try
        {
            var share = new FakeShare(CreateStripedImage(root, 200, 100));
            var visual = new OverlayVisualContextCaptureService(share, new TestPaths(root));
            var captured = await visual.CaptureAsync(TestContext.Current.CancellationToken);
            var service = new OverlayRegionCaptureService();

            var region = await service.CreateRegionAsync(
                captured,
                new HavenRect(0, .25, 1, .5),
                100,
                100,
                TestContext.Current.CancellationToken);
            cropPath = region.MediaReference;

            using var cropped = SKBitmap.Decode(cropPath);
            Assert.NotNull(cropped);
            Assert.Equal(200, cropped.Width);
            Assert.Equal(100, cropped.Height);
            var top = cropped.GetPixel(100, 10);
            var bottom = cropped.GetPixel(100, 90);
            Assert.True(top.Red > top.Blue);
            Assert.True(bottom.Blue > bottom.Red);
        }
        finally
        {
            VisionRegionCropper.DeleteTemporary(cropPath);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Failed_crop_does_not_delete_source_or_create_exposed_region()
    {
        var root = NewDirectory();
        try
        {
            var source = CreateImage(root, 100, 50);
            var captured = new OverlayContextEnvelope(
                OverlayContextKind.Screen,
                null,
                [new OverlayContextAttachmentReference(
                    source,
                    "image",
                    "image/jpeg",
                    "Screen",
                    "{\"Width\":100,\"Height\":50}")],
                source,
                new OverlayContextProvenance(null, "Screen", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2), OverlayContextPermissionState.Granted, "real capture"));
            var service = new OverlayRegionCaptureService();
            var missing = captured with
            {
                Attachments = [captured.Attachments[0] with { Id = Path.Combine(root, "missing.jpg") }],
                MediaReference = Path.Combine(root, "missing.jpg")
            };

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.CreateRegionAsync(missing, new HavenRect(0, 0, 1, 1), 100, 50, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(source));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task Cleanup_removes_only_owned_region_crop()
    {
        var root = NewDirectory();
        string? cropPath = null;
        var unrelated = Path.Combine(root, "unrelated.png");
        try
        {
            var share = new FakeShare(CreateImage(root, 60, 30));
            var visual = new OverlayVisualContextCaptureService(share, new TestPaths(root));
            var captured = await visual.CaptureAsync(TestContext.Current.CancellationToken);
            var service = new OverlayRegionCaptureService();
            var region = await service.CreateRegionAsync(captured, new HavenRect(0, 0, 1, 1), 60, 30, TestContext.Current.CancellationToken);
            cropPath = region.MediaReference;
            File.WriteAllBytes(unrelated, [1, 2, 3]);

            service.CleanupRegion(region);

            Assert.False(File.Exists(cropPath!));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            VisionRegionCropper.DeleteTemporary(cropPath);
            DeleteDirectory(root);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HavenOverlayRegionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { }
    }

    private static string CreateImage(string directory, int width, int height)
    {
        var path = Path.Combine(directory, "source.jpg");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static string CreateStripedImage(string directory, int width, int height)
    {
        var path = Path.Combine(directory, "striped.jpg");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        canvas.DrawRect(new SKRect(0, height / 2f, width, height), new SKPaint { Color = SKColors.Blue });
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private sealed class FakeShare : IScreenShareService
    {
        private readonly int _width;
        private readonly int _height;

        public FakeShare(string imagePath)
        {
            Payload = Convert.ToBase64String(File.ReadAllBytes(imagePath));
            using var bitmap = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("Test image did not decode.");
            _width = bitmap.Width;
            _height = bitmap.Height;
        }

        public bool IsSupported => true;
        public bool IsSharing { get; private set; }
        public string? UnavailableReason => null;
        public ScreenShareSource? CurrentSource { get; private set; }
        public string Payload { get; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public event EventHandler? SourceClosed { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable { add { } remove { } }

        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            IsSharing = true;
            CurrentSource = new ScreenShareSource("source", "Picked screen", ScreenShareSourceKind.Screen);
            return Task.FromResult(CurrentSource);
        }

        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ScreenShareSnapshot?>(new ScreenShareSnapshot(Payload, _width, _height, DateTimeOffset.UtcNow));

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsSharing = false;
            CurrentSource = null;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "db.sqlite");
        public string BrowserProfileDirectory => root;
        public string AttachmentsDirectory => root;
        public string LogsDirectory => root;
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
