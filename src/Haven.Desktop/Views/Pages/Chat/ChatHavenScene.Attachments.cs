using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed record ChatAttachmentChip(string Id, string Label, string IconKey, bool Invokable = false);

internal sealed partial class ChatHavenScene
{
    private EventHandler<string>? _attachmentRemoveRequested;

    public event EventHandler<string>? AttachmentRemoveRequested
    {
        add
        {
            // NewChatPage subscribes while wiring the canonical Chat scene. Use that guaranteed
            // initialization point to install the composer QoL hooks without weakening or
            // duplicating the current ChatSessionService send pipeline.
            EnsureQueuedSendingEnabled();
            EnsureThreadSettingsEnabled();
            _attachmentRemoveRequested += value;
        }
        remove => _attachmentRemoveRequested -= value;
    }

    public event EventHandler<string>? AttachmentInvoked;

    public void SetAttachmentChips(IReadOnlyList<ChatAttachmentChip> chips)
    {
        ArgumentNullException.ThrowIfNull(chips);
        foreach (var child in AttachmentChips.Children.ToArray()) AttachmentChips.Remove(child);
        AttachmentChips.SetValue(
            HavenProperties.Visibility,
            chips.Count == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);

        foreach (var item in chips)
        {
            var chip = new Container { Layout = HavenLayout.Horizontal, Name = "ChatAttachmentChip" };
            chip.SetValue(HavenProperties.Gap, HavenLength.Px(4));
            chip.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 6px 4px 9px"));
            chip.SetValue(HavenProperties.Background, "SurfaceOverlay");
            chip.SetValue(HavenProperties.BorderColor, "Border");
            chip.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            chip.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
            chip.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));

            if (item.Invokable)
            {
                var body = new HavenButton { Content = item.Label, IconKey = item.IconKey, Variant = ButtonVariant.Ghost };
                body.SetValue(HavenProperties.MinHeight, HavenLength.Px(26));
                body.SetValue(HavenProperties.FontSize, 11d);
                body.Accessibility.AccessibleName = item.Label;
                body.Invoked += (_, _) => AttachmentInvoked?.Invoke(this, item.Id);
                chip.Add(body);
            }
            else
            {
                var icon = new Icon { Key = item.IconKey };
                icon.SetValue(HavenProperties.Width, HavenLength.Px(15));
                icon.SetValue(HavenProperties.Height, HavenLength.Px(15));
                icon.SetValue(HavenProperties.Foreground, "TextSecondary");
                chip.Add(icon);
                var label = new HavenText { Content = item.Label };
                label.SetValue(HavenProperties.FontSize, 11d);
                label.SetValue(HavenProperties.FontWeight, 600);
                label.SetValue(HavenProperties.Foreground, "TextPrimary");
                chip.Add(label);
            }

            var remove = new HavenButton { IconKey = "close", Variant = ButtonVariant.Icon };
            remove.SetValue(HavenProperties.Width, HavenLength.Px(24));
            remove.SetValue(HavenProperties.Height, HavenLength.Px(24));
            remove.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
            remove.Accessibility.AccessibleName = "Remove " + item.Label;
            remove.Invoked += (_, _) => _attachmentRemoveRequested?.Invoke(this, item.Id);
            chip.Add(remove);
            AttachmentChips.Add(chip);
        }
    }
}
