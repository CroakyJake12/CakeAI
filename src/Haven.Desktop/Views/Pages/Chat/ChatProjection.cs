using System.Collections.Immutable;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Safe, immutable presentation data for a compact consumer of the production Chat surface.
/// Thinking/chain-of-thought content is intentionally not part of this projection.
/// </summary>
public sealed record ChatProjectionToolActivity(
    Guid Id,
    string Title,
    string Detail,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    int LinesAdded = 0,
    int LinesRemoved = 0);

/// <summary>One conversation message suitable for rendering outside the full Chat page.</summary>
public sealed record ChatProjectionMessage(
    Guid Id,
    MessageRole Role,
    string Content,
    string? AgentName,
    string? ModelName,
    bool IsStreaming,
    ImmutableArray<ChatProjectionToolActivity> ToolActivities,
    DateTimeOffset CreatedAt);

/// <summary>
/// A point-in-time snapshot of the state already owned by <see cref="NewChatPage"/>.
/// Consumers must treat this as read-only and never use it to execute Chat work directly.
/// </summary>
public sealed record ChatProjectionState(
    Guid ConversationId,
    string ConversationTitle,
    string ModeName,
    string? SelectedModelName,
    string ActiveAgentName,
    bool IsSending,
    string? StatusText,
    ImmutableArray<ChatProjectionMessage> Messages,
    DateTimeOffset UpdatedAt)
{
    public bool IsRunning => IsSending;

    public bool HasStarted => !Messages.IsDefaultOrEmpty;

    public static ChatProjectionState Empty { get; } = new(
        Guid.Empty,
        "New chat",
        "Chat",
        null,
        "No Agent (Default)",
        false,
        null,
        ImmutableArray<ChatProjectionMessage>.Empty,
        DateTimeOffset.MinValue);

    internal static ChatProjectionState Create(
        Guid conversationId,
        string conversationTitle,
        string modeName,
        string? selectedModelName,
        string activeAgentName,
        bool isSending,
        string? statusText,
        IEnumerable<ChatProjectionMessage> messages) => new(
            conversationId,
            conversationTitle,
            modeName,
            selectedModelName,
            activeAgentName,
            isSending,
            string.IsNullOrWhiteSpace(statusText) ? null : statusText,
            messages.ToImmutableArray(),
            DateTimeOffset.UtcNow);
}

/// <summary>Raised after any externally observable Chat projection state change.</summary>
public sealed class ChatProjectionStateChangedEventArgs(ChatProjectionState state) : EventArgs
{
    public ChatProjectionState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}
