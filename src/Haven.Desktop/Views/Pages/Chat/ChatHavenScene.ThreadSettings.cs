using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private HavenButton? _threadSettingsButton;
    private bool _threadSettingsEnabled;

    private void EnsureThreadSettingsEnabled()
    {
        if (_threadSettingsEnabled) return;
        _threadSettingsEnabled = true;
        _threadSettingsButton = Chatbox.GetComponent<HavenButton>("ChatSettings");
        _threadSettingsButton.Accessibility.AccessibleName = "Manage chat response settings";
        _threadSettingsButton.Invoked += OnThreadSettingsInvoked;
    }

    private void OnThreadSettingsInvoked(object? sender, EventArgs e)
    {
        if (_threadSettingsButton is null) return;

        IReadOnlyList<PopupMenuItem> items =
        [
            new PopupMenuItem("Actions: All", () => SelectThreadSetting(AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowAllActions)),
            new PopupMenuItem("Actions: Basic", () => SelectThreadSetting(AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowBasicActions)),
            new PopupMenuItem("Actions: Just Chat", () => SelectThreadSetting(AddMenu.AddMenuAction.AllowActions, ChatActionMode.JustChat)),
            new PopupMenuItem("Responses: Auto", () => SelectThreadSetting(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.Auto)),
            new PopupMenuItem("Responses: Always Visual", () => SelectThreadSetting(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysVisual)),
            new PopupMenuItem("Responses: Prefer Visual", () => SelectThreadSetting(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferVisual)),
            new PopupMenuItem("Responses: Prefer Text", () => SelectThreadSetting(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferText)),
            new PopupMenuItem("Responses: Always Text", () => SelectThreadSetting(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysText))
        ];

        _activeMessagePopup?.Dismiss();
        var popup = new PopupMenu(_threadSettingsButton, Root, items, 300d, "Manage Chat");
        popup.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_activeMessagePopup, popup)) _activeMessagePopup = null;
        };
        _activeMessagePopup = popup;
        Root.Add(popup);
    }

    private void SelectThreadSetting(AddMenu.AddMenuAction action, object value)
    {
        CatalogItemSelected?.Invoke(this, new AddMenuSelection(action, value));
        _activeMessagePopup?.Dismiss();
    }
}
