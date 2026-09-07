using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Studio;

internal sealed class TextToSpeechHavenScene : IDisposable
{
    private bool _disposed;

    public TextToSpeechHavenScene(IReadOnlyList<CallVoice> voices, IReadOnlyList<CallAudioDevice> devices, bool available, string? unavailableReason)
    {
        Root = new Page { Name = "Studio.TTS.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("26px 30px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Accessibility.AccessibleName = "Text to Speech";

        var title = new HavenText("Text to Speech") { Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, 32d);
        title.SetValue(HavenProperties.FontWeight, 800);
        Root.Add(title);

        var subtitle = new HavenText("Turn text into spoken audio using Haven's local speech output.") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(subtitle);

        TextInput = new Input { Name = "Studio.TTS.Text", Multiline = true, SubmitOnEnter = false };
        TextInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(180));
        TextInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        TextInput.Accessibility.AccessibleName = "Text to speak";
        Root.Add(TextInput);

        var controls = new Container { Name = "Studio.TTS.Controls", Layout = HavenLayout.Wrap };
        controls.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        controls.SetValue(HavenProperties.Responsive, true);

        VoiceSelect = new Select
        {
            Name = "Studio.TTS.Voice",
            Items = voices.Count == 0 ? ["Default voice"] : voices.Select(voice => voice.Name).ToArray(),
            SelectedIndex = Math.Max(0, voices.ToList().FindIndex(voice => voice.IsDefault))
        };
        VoiceSelect.Accessibility.AccessibleName = "Voice";
        VoiceSelect.SetValue(HavenProperties.MinWidth, HavenLength.Px(180));

        DeviceSelect = new Select
        {
            Name = "Studio.TTS.Device",
            Items = devices.Count == 0 ? ["Default output"] : devices.Select(device => device.Name).ToArray(),
            SelectedIndex = Math.Max(0, devices.ToList().FindIndex(device => device.IsDefault))
        };
        DeviceSelect.Accessibility.AccessibleName = "Audio output";
        DeviceSelect.SetValue(HavenProperties.MinWidth, HavenLength.Px(180));

        SpeakButton = new HavenButton { Name = "Studio.TTS.Speak", Content = "Speak", Variant = ButtonVariant.Primary };
        SpeakButton.Accessibility.AccessibleName = "Speak text";
        StopButton = new HavenButton { Name = "Studio.TTS.Stop", Content = "Stop", Variant = ButtonVariant.Secondary };
        StopButton.Accessibility.AccessibleName = "Stop speaking";

        controls.Add(VoiceSelect); controls.Add(DeviceSelect); controls.Add(SpeakButton); controls.Add(StopButton);
        Root.Add(controls);

        StatusText = new HavenText(available ? "Ready." : unavailableReason ?? "Speech output is unavailable.") { Name = "Studio.TTS.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        StatusText.Accessibility.AccessibleName = "Text to Speech status";
        Root.Add(StatusText);

        SpeakButton.SetValue(HavenProperties.Enabled, available);
        StopButton.SetValue(HavenProperties.Enabled, available);
        SpeakButton.Invoked += (_, _) => SpeakRequested?.Invoke();
        StopButton.Invoked += (_, _) => StopRequested?.Invoke();
    }

    public Page Root { get; }
    public Input TextInput { get; }
    public Select VoiceSelect { get; }
    public Select DeviceSelect { get; }
    public HavenButton SpeakButton { get; }
    public HavenButton StopButton { get; }
    public HavenText StatusText { get; }

    public event Action? SpeakRequested;
    public event Action? StopRequested;

    public void SetBusy(bool busy)
    {
        SpeakButton.SetValue(HavenProperties.Enabled, !busy);
        VoiceSelect.SetValue(HavenProperties.Enabled, !busy);
        DeviceSelect.SetValue(HavenProperties.Enabled, !busy);
    }

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
