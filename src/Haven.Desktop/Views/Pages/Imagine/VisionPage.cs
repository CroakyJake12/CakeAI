using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>Dedicated Haven-native visual understanding and OCR workspace.</summary>
public sealed partial class VisionPage : UserControl, IDisposable
{
    private readonly VisionAnalysisService _analysis; private readonly VisionScene _scene = new(); private readonly HavenSceneControl _host; private CancellationTokenSource? _analysisCancellation; private string? _imagePath; private bool _disposed;
    public VisionPage(IProviderModelClient models, VisionAnalysisService? analysis = null)
    {
        _analysis = analysis ?? new VisionAnalysisService(models ?? throw new ArgumentNullException(nameof(models))); _host = new HavenSceneControl { Root = _scene.Root }; Content = _host; _host.InputSubmitted += input => { if (ReferenceEquals(input, _scene.Question)) _ = AnalyseAsync(_scene.Question.Text); };
        _scene.ImportRequested += async (_, _) => await PickImageAsync(); _scene.AnalyseRequested += prompt => _ = AnalyseAsync(prompt); _scene.OcrRequested += (_, _) => _ = AnalyseAsync("Read and transcribe all visible text in this image. Preserve line breaks and clearly mark uncertain characters."); _scene.StopRequested += (_, _) => _analysisCancellation?.Cancel(); _scene.OpenImagineRequested += (_, _) => { if (!string.IsNullOrWhiteSpace(_imagePath)) OpenInImagineRequested?.Invoke(_imagePath); else _scene.SetStatus("Import an image before opening it in Imagine."); }; SizeChanged += (_, e) => _scene.SetViewportWidth(e.NewSize.Width);
        WirePlatformImageInput();
    }
    public event Action<string>? OpenInImagineRequested; public string? ImagePath => _imagePath;
    public async Task LoadImageAsync(string path) { if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { _scene.SetStatus("The requested image is unavailable."); return; } _imagePath = await PrepareLoadedImageAsync(path); _scene.SetImage(_imagePath); _scene.SetStatus("Image ready for visual analysis."); }
    private async Task PickImageAsync() { var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) { _scene.SetStatus("The platform file picker is unavailable."); return; } var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open image in Vision", AllowMultiple = false }); var path = files.FirstOrDefault()?.TryGetLocalPath(); if (!string.IsNullOrWhiteSpace(path)) await LoadImageAsync(path); }
    private async Task AnalyseAsync(string prompt)
    {
        var sourcePath = _imagePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) { _scene.SetStatus("Import an image before asking Vision."); return; }
        if (string.IsNullOrWhiteSpace(prompt)) prompt = "Describe this image carefully, including important objects, visible text, layout and uncertainty.";
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _analysisCancellation = cancellation;
        _scene.SetBusy(true);
        _scene.SetStatus("Vision is analysing the image…");
        string? regionCropPath = null;
        try
        {
            var analysisPath = sourcePath;
            var regionAnalysis = false;
            if (VisionRegionCropper.IsRegionPrompt(prompt) && _scene.Preview.SelectedRegion is HavenRect selectedRegion)
            {
                regionAnalysis = true;
                var viewportWidth = Math.Max(1, _scene.Preview.Bounds.Width - 24);
                var viewportHeight = Math.Max(1, _scene.Preview.Bounds.Height - 24);
                regionCropPath = await VisionRegionCropper.CreateCropAsync(sourcePath, selectedRegion, viewportWidth, viewportHeight, cancellation.Token);
                analysisPath = regionCropPath;
                prompt = VisionRegionCropper.GetRegionQuestion(prompt);
                _scene.SetStatus("Vision is analysing the selected image region…");
            }

            var model = await _analysis.GetVisionModelAsync(cancellation.Token);
            if (model is null) { _scene.SetStatus("No compatible vision model is available. The image was not sent to a text-only model."); return; }
            var normalizedPrompt = prompt.Trim();
            var analysisKey = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(analysisPath, model.Name, normalizedPrompt, cancellation.Token);
            if (TryUseCachedAnalysis(analysisKey, regionAnalysis)) return;
            var result = await _analysis.AnalyzeResolvedAsync(new VisionAnalysisRequest(analysisPath, normalizedPrompt), model, cancellation.Token);
            _scene.SetResponse(result.Response, result.Model);
            _scene.Question.Text = string.Empty;
            await StoreAnalysisAsync(sourcePath, normalizedPrompt, result.Response, result.Model, analysisKey);
            _scene.SetStatus(regionAnalysis ? "Region analysis complete." : "Analysis complete.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { _scene.SetStatus("Vision analysis stopped."); }
        catch (TimeoutException exception) { _scene.SetStatus("Vision analysis timed out: " + exception.Message); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException) { _scene.SetStatus("Vision analysis failed: " + exception.Message); }
        finally
        {
            VisionRegionCropper.DeleteTemporary(regionCropPath);
            if (ReferenceEquals(_analysisCancellation, cancellation)) _analysisCancellation = null;
            cancellation.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetBusy(false));
        }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _analysisCancellation?.Cancel(); _analysisCancellation?.Dispose(); DeletePreviousClipboardImage(); }
}

internal enum VisionInteractionMode { Pan, SelectRegion }

internal sealed class VisionScene
{
    public VisionScene()
    {
        Root = new HavenPage { Name = "Vision.Root", Layout = HavenLayout.Vertical }; Root.SetValue(HavenProperties.Width, HavenLength.Percent(100)); Root.SetValue(HavenProperties.Height, HavenLength.Percent(100)); Root.SetValue(HavenProperties.Gap, HavenLength.Px(10)); Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 20px")); Root.Accessibility.AccessibleName = "Vision visual understanding workspace";
        var header = new HavenContainer { Name = "Vision.Header", Layout = HavenLayout.Wrap }; header.SetValue(HavenProperties.Gap, HavenLength.Px(7)); Title = new HavenText { Content = "Vision", Level = TextLevel.H1 }; header.Add(Title); Import = Action("Vision.Import", "Open image", "plus"); header.Add(Import); Analyse = Action("Vision.Analyse", "Analyse", "vision"); header.Add(Analyse); Ocr = Action("Vision.Ocr", "Read text", "file"); header.Add(Ocr); Pan = Action("Vision.Pan", "Pan", "move"); header.Add(Pan); SelectRegion = Action("Vision.SelectRegion", "Select region", "select"); header.Add(SelectRegion); AskRegion = Action("Vision.AskRegion", "Ask region", "vision"); header.Add(AskRegion); Fit = Action("Vision.Fit", "Fit", "expand"); header.Add(Fit); ClearRegion = Action("Vision.ClearRegion", "Clear region", "delete"); header.Add(ClearRegion); Stop = Action("Vision.Stop", "Stop", "delete"); Stop.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); header.Add(Stop); OpenImagine = Action("Vision.OpenImagine", "Edit in Imagine", "palette"); header.Add(OpenImagine); Zoom = Muted("Vision.Zoom", "100%"); header.Add(Zoom); Root.Add(header);
        Body = new HavenContainer { Name = "Vision.Body", Layout = HavenLayout.Grid, Columns = "1.15fr .85fr", Rows = "1fr" }; Body.SetValue(HavenProperties.Width, HavenLength.Percent(100)); Body.SetValue(HavenProperties.MinHeight, HavenLength.Px(500)); Body.SetValue(HavenProperties.Gap, HavenLength.Px(14)); Root.Add(Body); Preview = new VisionPreviewElement { Name = "Vision.Preview" }; Preview.SetValue(HavenProperties.Column, 0); Preview.SetValue(HavenProperties.Width, HavenLength.Percent(100)); Preview.SetValue(HavenProperties.Height, HavenLength.Percent(100)); Body.Add(Preview); var panel = new HavenContainer { Name = "Vision.Panel", Layout = HavenLayout.Vertical }; panel.SetValue(HavenProperties.Column, 1); panel.SetValue(HavenProperties.Gap, HavenLength.Px(8)); panel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); Body.Add(panel); Question = new Input { Name = "Vision.Question", Placeholder = "Ask about the image", Multiline = true, SubmitOnEnter = true }; Question.SetValue(HavenProperties.MinHeight, HavenLength.Px(90)); panel.Add(Question); Response = new HavenText { Name = "Vision.Response", Content = "Import an image, then ask a question or choose Read text." }; panel.Add(Response); RegionStatus = Muted("Vision.RegionStatus", "No region selected."); panel.Add(RegionStatus); Model = Muted("Vision.Model", string.Empty); panel.Add(Model); Status = Muted("Vision.Status", "No image loaded."); panel.Add(Status);
        Import.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty); Analyse.Invoked += (_, _) => AnalyseRequested?.Invoke(Question.Text.Trim()); Ocr.Invoked += (_, _) => OcrRequested?.Invoke(this, EventArgs.Empty); Pan.Invoked += (_, _) => { Preview.SetMode(VisionInteractionMode.Pan); SetStatus("Pan mode. Drag the image to move it; use the mouse wheel to zoom."); }; SelectRegion.Invoked += (_, _) => { Preview.SetMode(VisionInteractionMode.SelectRegion); SetStatus("Region mode. Drag over the image, then choose Ask region."); }; AskRegion.Invoked += (_, _) => AskSelectedRegion(); Fit.Invoked += (_, _) => Preview.Fit(); ClearRegion.Invoked += (_, _) => Preview.ClearRegion(); Stop.Invoked += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty); OpenImagine.Invoked += (_, _) => OpenImagineRequested?.Invoke(this, EventArgs.Empty); Preview.ViewChanged += (_, _) => Zoom.Content = $"{Preview.ZoomPercent:0}%"; Preview.RegionChanged += (_, _) => UpdateRegionStatus(); SetViewportWidth(1200);
    }
    public HavenPage Root { get; } public HavenContainer Body { get; } public HavenText Title { get; } public VisionPreviewElement Preview { get; } public Input Question { get; } public HavenText Response { get; } public HavenText RegionStatus { get; } public HavenText Model { get; } public HavenText Status { get; } public HavenText Zoom { get; } public HavenButton Import { get; } public HavenButton Analyse { get; } public HavenButton Ocr { get; } public HavenButton Pan { get; } public HavenButton SelectRegion { get; } public HavenButton AskRegion { get; } public HavenButton Fit { get; } public HavenButton ClearRegion { get; } public HavenButton Stop { get; } public HavenButton OpenImagine { get; }
    public event EventHandler? ImportRequested; public event Action<string>? AnalyseRequested; public event EventHandler? OcrRequested; public event EventHandler? StopRequested; public event EventHandler? OpenImagineRequested; public void SetImage(string path) { Preview.Source = path; Preview.Fit(); Preview.ClearRegion(); } public void SetStatus(string value) => Status.Content = value; public void SetResponse(string value, string model) { Response.Content = value; Model.Content = "Model: " + model; } public void SetBusy(bool busy) { Analyse.SetValue(HavenProperties.Enabled, !busy); Ocr.SetValue(HavenProperties.Enabled, !busy); AskRegion.SetValue(HavenProperties.Enabled, !busy); Stop.SetValue(HavenProperties.Visibility, busy ? HavenVisibility.Visible : HavenVisibility.Collapsed); } public void SetViewportWidth(double width) { Body.Columns = width < 760 ? "1fr" : "1.15fr .85fr"; Body.Rows = width < 760 ? "Auto Auto" : "1fr"; Preview.SetValue(HavenProperties.Column, 0); Body.Children[1].SetValue(HavenProperties.Column, width < 760 ? 0 : 1); Body.Children[1].SetValue(HavenProperties.Row, width < 760 ? 1 : 0); }
    private void AskSelectedRegion() { if (Preview.SelectedRegion is not HavenRect region) { SetStatus("Select a region before asking about it."); return; } var question = string.IsNullOrWhiteSpace(Question.Text) ? "Describe what is visible in the selected region." : Question.Text.Trim(); AnalyseRequested?.Invoke($"{question}\n\nThe user selected an approximate display region of the attached image: left {region.X:P1}, top {region.Y:P1}, width {region.Width:P1}, height {region.Height:P1}. Focus on that region. The full original image is attached; these coordinates are relative to the displayed image bounds and may include small letterbox margins."); }
    private void UpdateRegionStatus() { if (Preview.SelectedRegion is not HavenRect region) { RegionStatus.Content = "No region selected."; return; } RegionStatus.Content = $"Selected region · left {region.X:P0} · top {region.Y:P0} · {region.Width:P0} × {region.Height:P0}"; }
    private static HavenButton Action(string name, string content, string icon) => new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost }; private static HavenText Muted(string name, string content) { var value = new HavenText { Name = name, Content = content }; value.SetValue(HavenProperties.Foreground, "TextSecondary"); value.SetValue(HavenProperties.FontSize, 11d); return value; }
}

internal sealed partial class VisionPreviewElement : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget
{
    private string? _source; private VisionInteractionMode _mode = VisionInteractionMode.Pan; private HavenPoint _pan; private double _zoom = 1; private bool _dragging; private HavenPoint _dragStart; private HavenPoint _dragCurrent; private HavenRect? _selectedRegion;
    public string? Source { get => _source; set { if (_source == value) return; _source = value; Fit(); ClearRegion(); } } public VisionInteractionMode Mode => _mode; public HavenRect? SelectedRegion => _selectedRegion; public double ZoomPercent => _zoom * 100; public event EventHandler? ViewChanged; public event EventHandler? RegionChanged;
    public VisionPreviewElement() { Accessibility.Role = HavenAccessibleRole.Image; Accessibility.Focusable = true; Accessibility.AccessibleName = "Vision interactive image preview"; SetValue(HavenProperties.Background, "SurfaceRaised"); SetValue(HavenProperties.BorderColor, "Border"); SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); SetValue(HavenProperties.Clip, true); }
    public void SetMode(VisionInteractionMode mode) { _mode = mode; _dragging = false; Invalidate(); } public void Fit() { _zoom = 1; _pan = new HavenPoint(0, 0); _dragging = false; ViewChanged?.Invoke(this, EventArgs.Empty); Invalidate(); } public void ClearRegion() { if (_selectedRegion is null && !_dragging) return; _selectedRegion = null; _dragging = false; RegionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); } public void ZoomBy(double factor) { if (!double.IsFinite(factor) || factor <= 0) return; _zoom = Math.Clamp(_zoom * factor, .25, 8); ViewChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
    public bool PointerPressed(HavenPointerInput input) { if (string.IsNullOrWhiteSpace(_source)) return false; if (_mode == VisionInteractionMode.Pan) { _dragging = true; _dragStart = input.LocalPosition; _dragCurrent = input.LocalPosition; return true; } _dragging = true; _dragStart = ClampToImage(input.LocalPosition); _dragCurrent = _dragStart; Invalidate(); return true; }
    public bool PointerMoved(HavenPointerInput input) { if (!_dragging || string.IsNullOrWhiteSpace(_source)) return false; if (_mode == VisionInteractionMode.Pan) { var dx = input.LocalPosition.X - _dragCurrent.X; var dy = input.LocalPosition.Y - _dragCurrent.Y; _pan = new HavenPoint(_pan.X + dx, _pan.Y + dy); _dragCurrent = input.LocalPosition; ViewChanged?.Invoke(this, EventArgs.Empty); } else _dragCurrent = ClampToImage(input.LocalPosition); Invalidate(); return true; }
    public bool PointerReleased(HavenPointerInput input) { if (!_dragging) return false; if (_mode == VisionInteractionMode.SelectRegion) { _dragCurrent = ClampToImage(input.LocalPosition); var rect = NormalizedDragRect(); _selectedRegion = rect.Width >= .005 && rect.Height >= .005 ? rect : null; RegionChanged?.Invoke(this, EventArgs.Empty); } _dragging = false; Invalidate(); return true; }
    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY) { if (string.IsNullOrWhiteSpace(_source) || Math.Abs(deltaY) < .001) return false; ZoomBy(deltaY < 0 ? 1.1 : .9); return true; }
    public void Draw(HavenDrawingContext context, double opacity) { context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenTokenBrush("SurfaceRaised"), 16, opacity)); if (!string.IsNullOrWhiteSpace(_source)) { var image = ImageRect(); context.Add(new HavenImageCommand(image, new HavenImage(_source), HavenImageLayout.Contain, opacity)); if (_selectedRegion is HavenRect region) DrawRegion(context, region, opacity); if (_dragging && _mode == VisionInteractionMode.SelectRegion) DrawRegion(context, NormalizedDragRect(), opacity); } else context.Add(new HavenTextCommand(new HavenRect(Bounds.X + 24, Bounds.Y + 24, Math.Max(1, Bounds.Width - 48), 60), new HavenTextLayout("Open an image to inspect it", "Segoe UI", 18, 600, Math.Max(1, Bounds.Width - 48)), new HavenTokenBrush("TextSecondary"), opacity)); }
    private HavenRect LocalImageRect() { var innerWidth = Math.Max(1, Bounds.Width - 24); var innerHeight = Math.Max(1, Bounds.Height - 24); var width = innerWidth * _zoom; var height = innerHeight * _zoom; return new HavenRect(12 + (innerWidth - width) / 2 + _pan.X, 12 + (innerHeight - height) / 2 + _pan.Y, width, height); } private HavenRect ImageRect() { var local = LocalImageRect(); return new HavenRect(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height); } private HavenPoint ClampToImage(HavenPoint local) { var rect = LocalImageRect(); return new HavenPoint(Math.Clamp(local.X, rect.X, rect.Right), Math.Clamp(local.Y, rect.Y, rect.Bottom)); } private HavenRect NormalizedDragRect() { var rect = LocalImageRect(); var left = Math.Min(_dragStart.X, _dragCurrent.X); var top = Math.Min(_dragStart.Y, _dragCurrent.Y); var right = Math.Max(_dragStart.X, _dragCurrent.X); var bottom = Math.Max(_dragStart.Y, _dragCurrent.Y); return new HavenRect(Math.Clamp((left - rect.X) / Math.Max(1, rect.Width), 0, 1), Math.Clamp((top - rect.Y) / Math.Max(1, rect.Height), 0, 1), Math.Clamp((right - left) / Math.Max(1, rect.Width), 0, 1), Math.Clamp((bottom - top) / Math.Max(1, rect.Height), 0, 1)); } private void DrawRegion(HavenDrawingContext context, HavenRect region, double opacity) { var image = ImageRect(); var screen = new HavenRect(image.X + region.X * image.Width, image.Y + region.Y * image.Height, region.Width * image.Width, region.Height * image.Height); context.Add(new HavenFillRoundedRectCommand(screen, new HavenSolidBrush(45, 30, 136, 229), 5, opacity)); context.Add(new HavenStrokeRoundedRectCommand(screen, new HavenPen(new HavenSolidBrush(255, 30, 136, 229), 2), 5, opacity)); }
}
