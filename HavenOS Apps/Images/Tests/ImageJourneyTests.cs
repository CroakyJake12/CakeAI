using HavenOS.Images;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class ImageJourneyTests
{
    [Theory]
    [InlineData("sample.png")]
    [InlineData("sample.JPG")]
    [InlineData("sample.jpeg")]
    [InlineData("sample.bmp")]
    [InlineData("sample.GIF")]
    [InlineData("sample.webp")]
    public void PickerPolicyAcceptsSupportedRasterExtensions(string path)
    {
        Assert.True(ImageFilePolicy.IsSupportedPath(path));
    }

    [Theory]
    [InlineData("sample.svg")]
    [InlineData("sample.tiff")]
    [InlineData("sample.txt")]
    [InlineData("")]
    public void PickerPolicyRejectsUnlistedExtensions(string path)
    {
        Assert.False(ImageFilePolicy.IsSupportedPath(path));
    }

    [Fact]
    public void NavigationUsesOnlySupportedSiblingImagesInFileNameOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"haven-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var first = Path.Combine(directory, "a.webp");
            var selected = Path.Combine(directory, "b.JPG");
            var last = Path.Combine(directory, "z.png");
            var ignored = Path.Combine(directory, "notes.txt");

            File.WriteAllBytes(first, []);
            File.WriteAllBytes(selected, []);
            File.WriteAllBytes(last, []);
            File.WriteAllText(ignored, "not an image");

            var session = ImageNavigationSession.FromSelection(selected);

            Assert.Equal(selected, session.CurrentPath);
            Assert.True(session.CanMovePrevious);
            Assert.True(session.CanMoveNext);
            Assert.Equal(first, session.MovePrevious());
            Assert.Null(session.MovePrevious());
            Assert.Equal(selected, session.MoveNext());
            Assert.Equal(last, session.MoveNext());
            Assert.Null(session.MoveNext());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
