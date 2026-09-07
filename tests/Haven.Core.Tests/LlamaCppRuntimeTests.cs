using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

public sealed class LlamaCppRuntimeTests
{
    [Fact]
    public void Runtime_is_disabled_and_not_always_loaded_by_default()
    {
        var options = new LlamaCppRuntimeOptions();

        Assert.False(options.Enabled);
        Assert.False(options.AlwaysLoaded);
    }

    [Fact]
    public void Launch_plan_is_loopback_only_and_preserves_paths_as_arguments()
    {
        var executable = Path.GetFullPath(Path.Combine("llama cpp", "llama-server"));
        var model = Path.GetFullPath(Path.Combine("models", "Qwen Test.gguf"));
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: executable,
            ModelPath: model,
            Port: 18081,
            ContextSize: 65536,
            ParallelRequests: 2,
            AlwaysLoaded: true);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Equal(executable, plan.FileName);
        Assert.Equal("127.0.0.1", plan.Endpoint.Host);
        Assert.Equal(18081, plan.Endpoint.Port);
        Assert.Equal("/v1/", plan.Endpoint.AbsolutePath);
        Assert.True(plan.AlwaysLoaded);
        Assert.Equal(
            new[] { "--model", model, "--host", "127.0.0.1", "--port", "18081", "--ctx-size", "65536", "--parallel", "2", "--no-webui" },
            plan.Arguments);
    }

    [Fact]
    public void Launch_plan_supports_current_unified_llama_cli_without_shell_parsing()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama"),
            ModelPath: Path.GetFullPath("model.gguf"),
            UseUnifiedCli: true);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Equal("serve", plan.Arguments[0]);
        Assert.Equal("--model", plan.Arguments[1]);
    }

    [Theory]
    [InlineData(80, 32768, 1)]
    [InlineData(18080, 256, 1)]
    [InlineData(18080, 32768, 0)]
    public void Launch_plan_rejects_unsafe_or_invalid_resource_bounds(int port, int contextSize, int parallelRequests)
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"),
            Port: port,
            ContextSize: contextSize,
            ParallelRequests: parallelRequests);

        Assert.ThrowsAny<ArgumentException>(() => LlamaCppLaunchPlanner.Create(options));
    }

    [Fact]
    public void Malformed_environment_numbers_remain_invalid_instead_of_falling_back_to_a_runnable_default()
    {
        var values = new Dictionary<string, string?>
        {
            ["HAVEN_LLAMA_CPP_ENABLED"] = "true",
            ["HAVEN_LLAMA_CPP_EXECUTABLE"] = Path.GetFullPath("llama-server"),
            ["HAVEN_LLAMA_CPP_MODEL"] = Path.GetFullPath("model.gguf"),
            ["HAVEN_LLAMA_CPP_PORT"] = "not-a-number"
        };
        var options = LlamaCppRuntimeConfiguration.FromEnvironment(key => values.GetValueOrDefault(key));

        Assert.True(options.Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => LlamaCppLaunchPlanner.Create(options));
    }

    [Fact]
    public async Task Always_loaded_hook_fails_closed_when_the_executable_is_missing()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath($"missing-{Guid.NewGuid():N}"),
            ModelPath: Path.GetFullPath("model.gguf"),
            AlwaysLoaded: true);
        await using var runtime = new LlamaCppRuntime(options);

        var status = await runtime.StartIfAlwaysLoadedAsync(CancellationToken.None);

        Assert.False(status.Running);
        Assert.True(status.AlwaysLoaded);
        Assert.NotNull(status.Detail);
        Assert.Contains("does not exist", status.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Always_loaded_hook_is_a_no_op_when_not_enabled()
    {
        await using var runtime = new LlamaCppRuntime(new LlamaCppRuntimeOptions());

        var status = await runtime.StartIfAlwaysLoadedAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.False(status.Running);
        Assert.Null(status.ProcessId);
    }
}
