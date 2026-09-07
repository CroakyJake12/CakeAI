using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace HavenOS.Images;

public sealed partial class MainWindow : Window
{
    private readonly Button _openButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly TextBlock _statusText;
    private readonly TextBlock _fileNameText;
    private readonly TextBlock _metadataText;
    private readonly Image _previewImage;
    private readonly StackPanel _emptyState;

    private Bitmap? _bitmap;
    private ImageNavigationSession? _navigation;

    public MainWindow()
    {
        InitializeComponent();

        _openButton = this.FindControl<Button>("OpenButton") ?? throw new InvalidOperationException("OpenButton was not created from XAML.");
        _previousButton = this.FindControl<Button>("PreviousButton") ?? throw new InvalidOperationException("PreviousButton was not created from XAML.");
        _nextButton = this.FindControl<Button>("NextButton") ?? throw new InvalidOperationException("NextButton was not created from XAML.");
        _statusText = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("StatusText was not created from XAML.");
        _fileNameText = this.FindControl<TextBlock>("FileNameText") ?? throw new InvalidOperationException("FileNameText was not created from XAML.");
        _metadataText = this.FindControl<TextBlock>("MetadataText") ?? throw new InvalidOperationException("MetadataText was not created from XAML.");
        _previewImage = this.FindControl<Image>("PreviewImage") ?? throw new InvalidOperationException("PreviewImage was not created from XAML.");
        _emptyState = this.FindControl<StackPanel>("EmptyState") ?? throw new InvalidOperationException("EmptyState was not created from XAML.");

        _openButton.Click += OpenButton_Click;
        _previousButton.Click += PreviousButton_Click;
        _nextButton.Click += NextButton_Click;
        Closed += (_, _) => _bitmap?.Dispose();
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ImageFilePolicy.PickerPatterns,
                    },
                ],
            });

            if (files.Count == 0)
            {
                return;
            }

            var selected = files[0];
            if (!selected.Path.IsFile)
            {
                ShowError("This first Images slice opens local files only.");
                return;
            }

            LoadLocalPath(selected.Path.LocalPath);
        }
        catch (Exception exception)
        {
            ShowError($"The image picker could not be opened: {exception.Message}");
        }
    }

    private void PreviousButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = _navigation?.MovePrevious();
        if (path is not null)
        {
            LoadLocalPath(path);
        }
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = _navigation?.MoveNext();
        if (path is not null)
        {
            LoadLocalPath(path);
        }
    }

    private void LoadLocalPath(string path)
    {
        if (!ImageFilePolicy.IsSupportedPath(path))
        {
            ShowError("Choose a PNG, JPEG, BMP, GIF, or WebP image.");
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var nextBitmap = new Bitmap(stream);
            var previousBitmap = _bitmap;

            _bitmap = nextBitmap;
            _previewImage.Source = nextBitmap;
            previousBitmap?.Dispose();

            _navigation = ImageNavigationSession.FromSelection(path);
            _previewImage.IsVisible = true;
            _emptyState.IsVisible = false;
            _fileNameText.Text = Path.GetFileName(path);
            _metadataText.Text = $"{nextBitmap.PixelSize.Width} × {nextBitmap.PixelSize.Height} · {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()}";
            _statusText.Text = path;
            UpdateNavigationButtons();
        }
        catch (Exception exception)
        {
            ShowError($"Images could not decode this file: {exception.Message}");
        }
    }

    private void UpdateNavigationButtons()
    {
        _previousButton.IsEnabled = _navigation?.CanMovePrevious == true;
        _nextButton.IsEnabled = _navigation?.CanMoveNext == true;
    }

    private void ShowError(string message)
    {
        _statusText.Text = message;
        UpdateNavigationButtons();
    }
}
