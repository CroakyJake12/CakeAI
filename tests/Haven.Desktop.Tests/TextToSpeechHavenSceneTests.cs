using Haven.Core;
using Haven.Desktop.Views.Pages.Studio;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class TextToSpeechHavenSceneTests
{
    [Fact]
    public void Available_surface_exposes_real_voice_device_and_actions()
    {
        using var scene = new TextToSpeechHavenScene(
            [new CallVoice("voice-1", "Local Voice", IsDefault: true)],
            [new CallAudioDevice("device-1", "Speakers", true)],
            true,
            null);

        Assert.Equal("Local Voice", scene.VoiceSelect.Items[0]);
        Assert.Equal("Speakers", scene.DeviceSelect.Items[0]);
        Assert.Equal(0, scene.VoiceSelect.SelectedIndex);
        Assert.Equal(0, scene.DeviceSelect.SelectedIndex);
        Assert.True(scene.SpeakButton.GetValue(HavenProperties.Enabled));
    }

    [Fact]
    public void Unavailable_surface_is_honest_and_disables_playback()
    {
        using var scene = new TextToSpeechHavenScene([], [], false, "No local speech provider.");

        Assert.Equal("No local speech provider.", scene.StatusText.Content);
        Assert.False(scene.SpeakButton.GetValue(HavenProperties.Enabled));
        Assert.False(scene.StopButton.GetValue(HavenProperties.Enabled));
    }
}
