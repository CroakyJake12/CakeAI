using Avalonia.Controls;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Studio;

public sealed partial class TextToSpeechPage : UserControl, IDisposable
{
    private readonly ISpeechOutputService _speech;
    private readonly TextToSpeechHavenScene _route;
    private CancellationTokenSource? _playback;
    private bool _disposed;

    public TextToSpeechPage(ISpeechOutputService speech)
    {
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        InitializeComponent();
        _route = new TextToSpeechHavenScene(_speech.Voices, _speech.Devices, _speech.IsAvailable, _speech.UnavailableReason);
        Scene.Root = _route.Root;
        _route.SpeakRequested += OnSpeakRequested;
        _route.StopRequested += OnStopRequested;
    }

    internal TextToSpeechHavenScene Route => _route;

    private async void OnSpeakRequested()
    {
        if (_disposed) return;
        var text = _route.TextInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _route.SetStatus("Enter some text first.");
            return;
        }
        if (!_speech.IsAvailable)
        {
            _route.SetStatus(_speech.UnavailableReason ?? "Speech output is unavailable.");
            return;
        }

        await StopPlaybackAsync(updateStatus: false);
        var playback = new CancellationTokenSource();
        _playback = playback;
        var voices = _speech.Voices;
        var devices = _speech.Devices;
        var voice = _route.VoiceSelect.SelectedIndex >= 0 && _route.VoiceSelect.SelectedIndex < voices.Count
            ? voices[_route.VoiceSelect.SelectedIndex].Id
            : null;
        var device = _route.DeviceSelect.SelectedIndex >= 0 && _route.DeviceSelect.SelectedIndex < devices.Count
            ? devices[_route.DeviceSelect.SelectedIndex].Id
            : null;

        _route.SetBusy(true);
        _route.SetStatus("Speaking…");
        try
        {
            await _speech.SpeakAsync(text, voice, device, playback.Token);
            if (!_disposed && !playback.IsCancellationRequested) _route.SetStatus("Finished.");
        }
        catch (OperationCanceledException) when (playback.IsCancellationRequested)
        {
            if (!_disposed) _route.SetStatus("Stopped.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            if (!_disposed) _route.SetStatus("Could not play speech: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_playback, playback))
            {
                _playback = null;
                playback.Dispose();
            }
            if (!_disposed) _route.SetBusy(false);
        }
    }

    private async void OnStopRequested() => await StopPlaybackAsync(updateStatus: true);

    private async Task StopPlaybackAsync(bool updateStatus)
    {
        var playback = Interlocked.Exchange(ref _playback, null);
        playback?.Cancel();
        playback?.Dispose();
        try
        {
            await _speech.StopAsync(CancellationToken.None);
            if (updateStatus && !_disposed) _route.SetStatus("Stopped.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            if (updateStatus && !_disposed) _route.SetStatus("Could not stop speech: " + exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _route.SpeakRequested -= OnSpeakRequested;
        _route.StopRequested -= OnStopRequested;
        var playback = Interlocked.Exchange(ref _playback, null);
        playback?.Cancel();
        playback?.Dispose();
        _ = _speech.StopAsync(CancellationToken.None);
        _route.Dispose();
    }
}
