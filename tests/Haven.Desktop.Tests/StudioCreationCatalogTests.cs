using Haven.Desktop.Views.Pages.Studio;

namespace Haven.Desktop.Tests;

public sealed class StudioCreationCatalogTests
{
    [Fact]
    public void Every_launch_tile_has_a_unique_identity_and_real_destination()
    {
        Assert.NotEmpty(StudioCreationCatalog.Items);
        Assert.Equal(StudioCreationCatalog.Items.Count, StudioCreationCatalog.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var item in StudioCreationCatalog.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Category));

            switch (item.DestinationKind)
            {
                case StudioCreationDestinationKind.App:
                    Assert.False(string.IsNullOrWhiteSpace(item.AppKey));
                    break;
                case StudioCreationDestinationKind.ProjectCreator:
                    Assert.Equal("projects", item.AppKey);
                    Assert.False(string.IsNullOrWhiteSpace(item.SeedPrompt));
                    break;
                case StudioCreationDestinationKind.ExperienceBuilder:
                    Assert.Equal("experiences", item.AppKey);
                    Assert.False(string.IsNullOrWhiteSpace(item.SeedPrompt));
                    break;
                case StudioCreationDestinationKind.InHouse:
                    Assert.False(string.IsNullOrWhiteSpace(item.InHouseFlow));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported destination kind {item.DestinationKind}.");
            }
        }
    }

    [Theory]
    [InlineData("Media")]
    [InlineData("Interactive")]
    [InlineData("Tools")]
    [InlineData("Documents")]
    public void Required_sections_are_populated(string category)
    {
        Assert.NotEmpty(StudioCreationCatalog.InCategory(category));
    }

    [Fact]
    public void Historical_projects_identity_is_not_exposed_as_studio_tile_destination()
    {
        Assert.DoesNotContain(StudioCreationCatalog.Items, item => item.Name.Equals("Projects", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(StudioCreationCatalog.Items, item => item.DestinationKind == StudioCreationDestinationKind.ProjectCreator);
    }
}
