using System.Globalization;
using Haven.Core;
using Haven.Desktop.Services.Maps;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenImage = Haven.UI.Components.Image;
using HavenInput = Haven.UI.Components.Input;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Maps;

/// <summary>Native Maps surface: place search, a raster tile map, directions and saved places.</summary>
internal sealed class MapsHavenScene : IDisposable
{
    private static readonly GeoPoint DefaultCentre = new(25d, 10d);
    private const int DefaultZoomLevel = 3;

    private readonly HashSet<MapTileId> _loadedTiles = [];
    private readonly Dictionary<MapTileId, HavenImage> _tileImages = [];

    private GeoPoint _centre = DefaultCentre;
    private int _zoomLevel = DefaultZoomLevel;
    private ViewportState? _viewport;
    private bool _disposed;

    public MapsHavenScene()
    {
        Root = new Page { Name = "Maps.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "auto auto 1fr auto" };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("28px 32px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(14));
        Set(Root, HavenProperties.Background, "Transparent");

        BuildHeader();
        BuildSearchBar();
        BuildMainArea();
        BuildStatusBar();
        StatusText.Content = "Search for a place to begin, or pick a saved place or recent search.";
    }

    /// <summary>Raised whenever the viewport changed and tiles may need to be loaded.</summary>
    public event EventHandler? ViewportChanged;

    public Page Root { get; }
    internal HavenInput SearchInput { get; private set; } = null!;
    internal HavenButton SearchButton { get; private set; } = null!;
    internal Select ResultsSelect { get; private set; } = null!;
    internal Select ProfileSelect { get; private set; } = null!;
    internal HavenButton RouteButton { get; private set; } = null!;
    internal HavenButton SavePlaceButton { get; private set; } = null!;
    internal HavenButton CopyCoordinatesButton { get; private set; } = null!;
    internal Select SavedPlacesSelect { get; private set; } = null!;
    internal Select RecentSearchesSelect { get; private set; } = null!;
    internal HavenText StatusText { get; private set; } = null!;

    private Container MapCard { get; set; } = null!;
    private Container TileLayer { get; set; } = null!;
    private Container MarkerLayer { get; set; } = null!;
    private Container RouteLayer { get; set; } = null!;
    private HavenText ZoomLabel { get; set; } = null!;
    private HavenText AttributionText { get; set; } = null!;

    private GeoPoint? MarkerStart { get; set; }
    private GeoPoint? MarkerEnd { get; set; }
    private string? MarkerStartName { get; set; }
    private string? MarkerEndName { get; set; }
    private IReadOnlyList<GeoPoint> RoutePoints { get; set; } = [];

    /// <summary>Returns the current viewport when the map area has been laid out, otherwise null.</summary>
    internal ViewportState? CurrentViewport()
    {
        if (_disposed || MapCard is null) return null;
        var width = MapCard.Bounds.Width;
        var height = MapCard.Bounds.Height;
        return width < 32d || height < 32d ? null : new ViewportState(_centre, _zoomLevel, width, height);
    }

    /// <summary>Recomputes the viewport from current bounds, rebuilds layers and raises ViewportChanged.</summary>
    internal void RefreshFromBounds()
    {
        if (_disposed) return;
        RebuildLayers();
        RaiseViewportChanged();
    }

    /// <summary>Moves the map centre and raises ViewportChanged so missing tiles load.</summary>
    internal void CentreOn(GeoPoint point)
    {
        if (_disposed) return;
        _centre = point;
        RebuildLayers();
        RaiseViewportChanged();
    }

    /// <summary>Steps the zoom level within supported bounds.</summary>
    internal void ZoomBy(int delta)
    {
        if (_disposed) return;
        var next = Math.Clamp(_zoomLevel + delta, ViewportState.MinZoomLevel, ViewportState.MaxZoomLevel);
        if (next == _zoomLevel) return;
        _zoomLevel = next;
        ZoomLabel.Content = $"Zoom {_zoomLevel}";
        RebuildLayers();
        RaiseViewportChanged();
    }

    /// <summary>Places or replaces the two selectable route-end markers.</summary>
    internal void SetMarkers(GeoPoint? start, string? startName, GeoPoint? end, string? endName)
    {
        MarkerStart = start;
        MarkerStartName = startName;
        MarkerEnd = end;
        MarkerEndName = endName;
        RebuildLayers();
    }

    /// <summary>Shows a route polyline approximation between the marked points.</summary>
    internal void ShowRoute(IReadOnlyList<GeoPoint> points)
    {
        RoutePoints = points ?? [];
        RebuildLayers();
    }

    /// <summary>Removes any shown route polyline.</summary>
    internal void ClearRoute()
    {
        RoutePoints = [];
        RebuildLayers();
    }

    /// <summary>Records that tile bytes are cached locally; renders the tile when still visible.</summary>
    internal void NotifyTileLoaded(MapTileId tileId)
    {
        if (_disposed) return;
        _loadedTiles.Add(tileId);
        if (_viewport is not { } viewport || _tileImages.ContainsKey(tileId) || !viewport.Contains(tileId)) return;
        var image = CreateTileImage(viewport, tileId);
        _tileImages[tileId] = image;
        TileLayer.Add(image);
    }

    internal void SetResults(IReadOnlyList<string> labels)
    {
        ResultsSelect.Items = labels;
        ResultsSelect.SelectedIndex = -1;
    }

    internal void SetSavedPlaces(IReadOnlyList<string> labels)
    {
        SavedPlacesSelect.Items = labels;
        SavedPlacesSelect.SelectedIndex = -1;
    }

    internal void SetRecentSearches(IReadOnlyList<string> queries)
    {
        RecentSearchesSelect.Items = queries;
        RecentSearchesSelect.SelectedIndex = -1;
    }

    internal void SetStatus(string message) => StatusText.Content = _disposed ? StatusText.Content : message;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var child in Root.Children.ToArray()) Root.Remove(child);
        _tileImages.Clear();
        _loadedTiles.Clear();
    }

    private void BuildHeader()
    {
        var header = new Container { Name = "Maps.Header", Layout = HavenLayout.Vertical };
        Set(header, HavenProperties.Gap, HavenLength.Px(4));
        header.Add(new HavenText("Maps") { Name = "Maps.Title", Level = TextLevel.H1 });
        header.Add(Muted("Find places, view OpenStreetMap tiles, get directions and keep saved places local."));
        Root.Add(header);
    }

    private void BuildSearchBar()
    {
        var bar = new Container { Name = "Maps.SearchBar", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        Set(bar, HavenProperties.Gap, HavenLength.Px(8));
        Set(bar, HavenProperties.Row, 1);
        SearchInput = InputField("Maps.Search.Input", "Search places, addresses or landmarks");
        bar.Add(SearchInput);
        SearchButton = Button("Maps.Search.Button", "Search", ButtonVariant.Primary);
        Set(SearchButton, HavenProperties.Column, 1);
        bar.Add(SearchButton);
        Root.Add(bar);
    }

    private void BuildMainArea()
    {
        var area = new Container { Name = "Maps.Main", Layout = HavenLayout.Grid, Columns = "360px 1fr", Rows = "1fr" };
        Set(area, HavenProperties.Gap, HavenLength.Px(12));
        Set(area, HavenProperties.Row, 2);

        var sidePanel = new Container { Name = "Maps.Side", Layout = HavenLayout.Vertical };
        Set(sidePanel, HavenProperties.Gap, HavenLength.Px(9));
        Set(sidePanel, HavenProperties.Overflow, HavenOverflow.Scroll);
        BuildResultsCard(sidePanel);
        BuildDirectionsCard(sidePanel);
        BuildActionsCard(sidePanel);
        BuildSavedCard(sidePanel);
        BuildRecentCard(sidePanel);
        area.Add(sidePanel);

        MapCard = new Container { Name = "Maps.Map.Card", Layout = HavenLayout.Overlay };
        Set(MapCard, HavenProperties.Column, 1);
        Set(MapCard, HavenProperties.Background, "SurfaceRaised");
        Set(MapCard, HavenProperties.BorderColor, "Border");
        Set(MapCard, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(MapCard, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(MapCard, HavenProperties.Overflow, HavenOverflow.Clip);
        Set(MapCard, HavenProperties.Shadow, "Card");

        TileLayer = new Container { Name = "Maps.Map.Tiles", Layout = HavenLayout.Canvas };
        Set(TileLayer, HavenProperties.ZIndex, 0);
        MapCard.Add(TileLayer);

        RouteLayer = new Container { Name = "Maps.Map.Route", Layout = HavenLayout.Canvas };
        MakeOverlayChild(RouteLayer);
        Set(RouteLayer, HavenProperties.ZIndex, 2);
        MapCard.Add(RouteLayer);

        MarkerLayer = new Container { Name = "Maps.Map.Markers", Layout = HavenLayout.Canvas };
        MakeOverlayChild(MarkerLayer);
        Set(MarkerLayer, HavenProperties.ZIndex, 3);
        MapCard.Add(MarkerLayer);

        BuildZoomControls();
        BuildAttribution();
        area.Add(MapCard);
        Root.Add(area);
    }

    private void BuildZoomControls()
    {
        var controls = new Container { Name = "Maps.Map.ZoomControls", Layout = HavenLayout.Horizontal };
        MakeOverlayChild(controls);
        Set(controls, HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        Set(controls, HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        Set(controls, HavenProperties.Margin, HavenThickness.Parse("12px"));
        Set(controls, HavenProperties.ZIndex, 5);
        Set(controls, HavenProperties.Gap, HavenLength.Px(8));
        var zoomOut = Button("Maps.Map.ZoomOut", "−", ButtonVariant.Secondary);
        zoomOut.Accessibility.AccessibleName = "Zoom out";
        zoomOut.Invoked += (_, _) => ZoomBy(-1);
        var zoomIn = Button("Maps.Map.ZoomIn", "+", ButtonVariant.Secondary);
        zoomIn.Accessibility.AccessibleName = "Zoom in";
        zoomIn.Invoked += (_, _) => ZoomBy(1);
        ZoomLabel = Muted($"Zoom {_zoomLevel}");
        ZoomLabel.Name = "Maps.Map.ZoomLabel";
        ZoomLabel.Accessibility.AccessibleName = "Current map zoom level";
        controls.Add(zoomOut);
        controls.Add(zoomIn);
        controls.Add(ZoomLabel);
        MapCard.Add(controls);
    }

    private void BuildAttribution()
    {
        AttributionText = new HavenText(MapsAttribution.Text) { Name = "Maps.Map.Attribution", Level = TextLevel.Caption };
        AttributionText.Accessibility.AccessibleName = $"Map data attribution: {MapsAttribution.Text}";
        MakeOverlayChild(AttributionText);
        Set(AttributionText, HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        Set(AttributionText, HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        Set(AttributionText, HavenProperties.Margin, HavenThickness.Parse("10px"));
        Set(AttributionText, HavenProperties.ZIndex, 5);
        Set(AttributionText, HavenProperties.Background, "SurfaceRaised");
        Set(AttributionText, HavenProperties.Padding, HavenThickness.Parse("6px 10px"));
        Set(AttributionText, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        MapCard.Add(AttributionText);
    }

    private void BuildResultsCard(Container parent)
    {
        var card = Card("Maps.Card.Results");
        card.Add(Heading("Search results", TextLevel.H4));
        ResultsSelect = new Select { Name = "Maps.Results.Select" };
        ResultsSelect.Accessibility.AccessibleName = "Place search results";
        Set(ResultsSelect, HavenProperties.Width, HavenLength.Percent(100));
        card.Add(ResultsSelect);
        card.Add(Muted("Choose a result to drop a marker; the first choice marks the start, the second the destination."));
        parent.Add(card);
    }

    private void BuildDirectionsCard(Container parent)
    {
        var card = Card("Maps.Card.Directions");
        card.Add(Heading("Directions", TextLevel.H4));
        ProfileSelect = new Select { Name = "Maps.Profile.Select", Items = ["Driving", "Walking", "Cycling"], SelectedIndex = 0 };
        ProfileSelect.Accessibility.AccessibleName = "Travel profile";
        Set(ProfileSelect, HavenProperties.Width, HavenLength.Percent(100));
        card.Add(ProfileSelect);
        RouteButton = Button("Maps.Route.Button", "Get directions", ButtonVariant.Primary);
        card.Add(RouteButton);
        card.Add(Muted("Routing uses OSRM as a best-effort service; map data © OpenStreetMap contributors."));
        parent.Add(card);
    }

    private void BuildActionsCard(Container parent)
    {
        var card = Card("Maps.Card.Actions");
        card.Add(Heading("Place actions", TextLevel.H4));
        var actionsRow = Row("Auto Auto");
        SavePlaceButton = Button("Maps.Save.Button", "Save place", ButtonVariant.Secondary);
        CopyCoordinatesButton = Button("Maps.Copy.Button", "Copy coordinates", ButtonVariant.Secondary);
        actionsRow.Add(SavePlaceButton);
        actionsRow.Add(CopyCoordinatesButton);
        card.Add(actionsRow);
        card.Add(Muted("Actions apply to the most recently chosen place."));
        parent.Add(card);
    }

    private void BuildSavedCard(Container parent)
    {
        var card = Card("Maps.Card.Saved");
        card.Add(Heading("Saved places", TextLevel.H4));
        SavedPlacesSelect = new Select { Name = "Maps.Saved.Select" };
        SavedPlacesSelect.Accessibility.AccessibleName = "Saved places";
        Set(SavedPlacesSelect, HavenProperties.Width, HavenLength.Percent(100));
        card.Add(SavedPlacesSelect);
        card.Add(Muted("Saved places stay on this device."));
        parent.Add(card);
    }

    private void BuildRecentCard(Container parent)
    {
        var card = Card("Maps.Card.Recent");
        card.Add(Heading("Recent searches", TextLevel.H4));
        RecentSearchesSelect = new Select { Name = "Maps.Recent.Select" };
        RecentSearchesSelect.Accessibility.AccessibleName = "Recent map searches";
        Set(RecentSearchesSelect, HavenProperties.Width, HavenLength.Percent(100));
        card.Add(RecentSearchesSelect);
        card.Add(Muted("Pick an entry to search again."));
        parent.Add(card);
    }

    private void BuildStatusBar()
    {
        StatusText = Muted(string.Empty);
        StatusText.Name = "Maps.Status";
        StatusText.Accessibility.AccessibleName = "Maps status";
        Set(StatusText, HavenProperties.Row, 3);
        Root.Add(StatusText);
    }

    private void RebuildLayers()
    {
        _viewport = CurrentViewport();
        RebuildTileLayer();
        RebuildRouteLayer();
        RebuildMarkerLayer();
    }

    private void RebuildTileLayer()
    {
        foreach (var child in TileLayer.Children.ToArray()) TileLayer.Remove(child);
        _tileImages.Clear();
        if (_viewport is not { } viewport) return;
        foreach (var tileId in viewport.VisibleTiles())
        {
            if (!_loadedTiles.Contains(tileId)) continue;
            var image = CreateTileImage(viewport, tileId);
            _tileImages[tileId] = image;
            TileLayer.Add(image);
        }
    }

    private HavenImage CreateTileImage(ViewportState viewport, MapTileId tileId)
    {
        var origin = viewport.TileOriginOnScreen(tileId);
        var image = new HavenImage
        {
            Name = "Maps.Tile." + tileId.Key.Replace('/', '.'),
            Source = MapTilePresenter.ToSourceKey(tileId)
        };
        image.Accessibility.AccessibleName = $"OpenStreetMap tile {tileId.Key.Replace('/', ' ')}";
        Set(image, HavenProperties.Left, HavenLength.Px(origin.X));
        Set(image, HavenProperties.Top, HavenLength.Px(origin.Y));
        Set(image, HavenProperties.Width, HavenLength.Px(MapTilePresenter.PixelsPerTile));
        Set(image, HavenProperties.Height, HavenLength.Px(MapTilePresenter.PixelsPerTile));
        return image;
    }

    private void RebuildRouteLayer()
    {
        foreach (var child in RouteLayer.Children.ToArray()) RouteLayer.Remove(child);
        if (_viewport is not { } viewport || RoutePoints.Count < 2) return;
        var step = Math.Max(1, (int)Math.Ceiling(RoutePoints.Count / 220d));
        for (var index = 0; index < RoutePoints.Count; index += step)
        {
            var position = viewport.GeoPointToScreenPx(RoutePoints[index]);
            RouteLayer.Add(CreateDot(position.X, position.Y, 6d, "AccentSecondary", $"Maps.Route.Point.{index.ToString(CultureInfo.InvariantCulture)}"));
        }
        var last = viewport.GeoPointToScreenPx(RoutePoints[^1]);
        if ((RoutePoints.Count - 1) % step != 0) RouteLayer.Add(CreateDot(last.X, last.Y, 6d, "AccentSecondary", "Maps.Route.Point.Last"));
    }

    private void RebuildMarkerLayer()
    {
        foreach (var child in MarkerLayer.Children.ToArray()) MarkerLayer.Remove(child);
        if (_viewport is null) return;
        PlaceMarker(MarkerStart, MarkerStartName, "A", "Maps.Marker.Start");
        PlaceMarker(MarkerEnd, MarkerEndName, "B", "Maps.Marker.End");
    }

    private void PlaceMarker(GeoPoint? location, string? name, string letter, string elementName)
    {
        if (location is not { } point || _viewport is not { } viewport) return;
        var position = viewport.GeoPointToScreenPx(point);
        var dot = CreateDot(position.X - 7d, position.Y - 7d, 14d, "Accent", elementName + ".Dot");
        Set(dot, HavenProperties.BorderWidth, HavenLength.Px(2));
        Set(dot, HavenProperties.BorderColor, "SurfaceRaised");
        Set(dot, HavenProperties.ZIndex, 3);
        MarkerLayer.Add(dot);
        var label = new HavenText(letter) { Name = elementName + ".Label", Level = TextLevel.Caption };
        Set(label, HavenProperties.Left, HavenLength.Px(position.X + 10d));
        Set(label, HavenProperties.Top, HavenLength.Px(position.Y - 9d));
        Set(label, HavenProperties.ZIndex, 3);
        Set(label, HavenProperties.Background, "SurfaceRaised");
        Set(label, HavenProperties.Padding, HavenThickness.Parse("2px 4px"));
        Set(label, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        MarkerLayer.Add(label);
        label.Accessibility.AccessibleName = $"Marker {letter}: {(string.IsNullOrWhiteSpace(name) ? point.ToString() : name)}";
    }

    private Container CreateDot(double x, double y, double diameter, string backgroundToken, string name)
    {
        var dot = new Container { Name = name };
        Set(dot, HavenProperties.Left, HavenLength.Px(x));
        Set(dot, HavenProperties.Top, HavenLength.Px(y));
        Set(dot, HavenProperties.Width, HavenLength.Px(diameter));
        Set(dot, HavenProperties.Height, HavenLength.Px(diameter));
        Set(dot, HavenProperties.Background, backgroundToken);
        Set(dot, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(diameter / 2d)));
        return dot;
    }

    private void RaiseViewportChanged()
    {
        if (_disposed) return;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void MakeOverlayChild(HavenElement element) =>
        Set(element, HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(card, HavenProperties.Width, HavenLength.Percent(100));
        Set(card, HavenProperties.Background, "SurfaceRaised");
        Set(card, HavenProperties.BorderColor, "Border");
        Set(card, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(card, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(card, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(card, HavenProperties.Gap, HavenLength.Px(9));
        Set(card, HavenProperties.Shadow, "Card");
        return card;
    }

    private static Container Row(string columns = "1fr Auto")
    {
        var row = new Container { Layout = HavenLayout.Grid, Columns = columns, Rows = "auto" };
        Set(row, HavenProperties.Gap, HavenLength.Px(8));
        return row;
    }

    private static HavenText Heading(string content, TextLevel level = TextLevel.H3) => new(content) { Level = level };

    private static HavenText Muted(string content)
    {
        var text = new HavenText(content) { Level = TextLevel.Paragraph };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenInput InputField(string name, string placeholder)
    {
        var input = new HavenInput { Name = name, Placeholder = placeholder, SubmitOnEnter = true };
        input.Accessibility.AccessibleName = placeholder;
        Set(input, HavenProperties.Width, HavenLength.Percent(100));
        return input;
    }

    private static HavenButton Button(string name, string content, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);
}
