using System.Collections.Immutable;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

public sealed class ChatProjectionTests
{
    [Fact]
    public void Empty_projection_is_safe_and_not_running()
    {
        var state = ChatProjectionState.Empty;

        Assert.False(state.IsRunning);
        Assert.False(state.HasStarted);
        Assert.Null(state.SelectedModelName);
        Assert.Empty(state.Messages);
    }

    [Fact]
    public void Projection_message_contains_renderable_result_and_tool_summary_without_thinking_state()
    {
        var message = new ChatProjectionMessage(
            Guid.NewGuid(),
            MessageRole.Assistant,
            "The build passed.",
            "Haven",
            "qwen3",
            false,
            [new ChatProjectionToolActivity(
                Guid.NewGuid(),
                "Build",
                "Build succeeded.",
                true,
                TimeSpan.FromSeconds(2),
                DateTimeOffset.UtcNow)],
            DateTimeOffset.UtcNow);

        var state = new ChatProjectionState(
            Guid.NewGuid(),
            "Build check",
            "Chat",
            "qwen3",
            "Haven",
            false,
            null,
            [message],
            DateTimeOffset.UtcNow);

        Assert.Equal("The build passed.", state.Messages[0].Content);
        Assert.True(state.Messages[0].ToolActivities[0].Succeeded);
        Assert.Equal("qwen3", state.SelectedModelName);
        Assert.DoesNotContain(state.Messages[0].Content, "thinking", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_event_args_carries_one_point_in_time_snapshot()
    {
        var state = ChatProjectionState.Empty with
        {
            IsSending = true,
            StatusText = "Running…",
            Messages = ImmutableArray<ChatProjectionMessage>.Empty
        };

        var args = new ChatProjectionStateChangedEventArgs(state);

        Assert.Same(state, args.State);
        Assert.True(args.State.IsRunning);
        Assert.Equal("Running…", args.State.StatusText);
    }
}
