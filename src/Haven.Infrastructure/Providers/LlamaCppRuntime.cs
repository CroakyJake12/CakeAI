using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>
/// A validated, shell-free launch description for one local llama.cpp server.
/// </summary>
public sealed record LlamaCppLaunchPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    Uri Endpoint,
    bool AlwaysLoaded);

/// <summary>
/// Builds the smallest supported llama.cpp server invocation without touching global system state.
/// </summary>
public static class LlamaCppLaunchPlanner
{
    private const int MinimumPort = 1024;
    private const int MaximumPort = 65535;
    private const int MinimumContextSize = 512;
    private const int MaximumContextSize = 262144;
    private const int MaximumParallelRequests = 16;

    public static LlamaCppLaunchPlan Create(LlamaCppRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
            throw new InvalidOperationException("The llama.cpp runtime is disabled.");

        var executablePath = RequireAbsolutePath(options.ExecutablePath, nameof(options.ExecutablePath));
        var modelPath = RequireAbsolutePath(options.ModelPath, nameof(options.ModelPath));
        if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The configured llama.cpp model must be a GGUF file.", nameof(options.ModelPath));
        if (options.Port is < MinimumPort or > MaximumPort)
            throw new ArgumentOutOfRangeException(nameof(options.Port), $"The llama.cpp port must be between {MinimumPort} and {MaximumPort}.");
        if (options.ContextSize is < MinimumContextSize or > MaximumContextSize)
            throw new ArgumentOutOfRangeException(nameof(options.ContextSize), $"The llama.cpp context size must be between {MinimumContextSize} and {MaximumContextSize} tokens.");
        if (options.ParallelRequests is < 1 or > MaximumParallelRequests)
            throw new ArgumentOutOfRangeException(nameof(options.ParallelRequests), $"The llama.cpp parallel-request count must be between 1 and {MaximumParallelRequests}.");

        var arguments = new List<string>();
        if (options.UseUnifiedCli) arguments.Add("serve");
        arguments.AddRange([
            "--model", modelPath,
            "--host", "127.0.0.1",
            "--port", options.Port.ToString(CultureInfo.InvariantCulture),
            "--ctx-size", options.ContextSize.ToString(CultureInfo.InvariantCulture),
            "--parallel", options.ParallelRequests.ToString(CultureInfo.InvariantCulture),
            "--no-webui"
        ]);

        return new LlamaCppLaunchPlan(
            executablePath,
            arguments,
            new Uri($"http://127.0.0.1:{options.Port}/v1/", UriKind.Absolute),
            options.AlwaysLoaded);
    }

    private static string RequireAbsolutePath(string? value, string parameterName)
    {
        var path = value?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An explicit path is required.", parameterName);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The path must be fully qualified so Haven cannot resolve an unintended executable or model.", parameterName);
        return path;
    }
}

/// <summary>
/// Reads process-local Haven configuration. Malformed numeric values remain invalid so startup fails closed.
/// </summary>
public static class LlamaCppRuntimeConfiguration
{
    public static LlamaCppRuntimeOptions FromEnvironment(Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        return new LlamaCppRuntimeOptions(
            Enabled: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_ENABLED")),
            ExecutablePath: Normalize(readVariable("HAVEN_LLAMA_CPP_EXECUTABLE")),
            ModelPath: Normalize(readVariable("HAVEN_LLAMA_CPP_MODEL")),
            UseUnifiedCli: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_UNIFIED_CLI")),
            Port: ReadInteger(readVariable("HAVEN_LLAMA_CPP_PORT"), 18080),
            ContextSize: ReadInteger(readVariable("HAVEN_LLAMA_CPP_CONTEXT_SIZE"), 32768),
            ParallelRequests: ReadInteger(readVariable("HAVEN_LLAMA_CPP_PARALLEL"), 1),
            AlwaysLoaded: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_ALWAYS_LOADED")));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ReadBoolean(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "on" or "enabled" => true,
        _ => false
    };

    private static int ReadInteger(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.MinValue;
    }
}

/// <summary>
/// Owns exactly one llama.cpp child process. Missing or invalid configuration returns a stopped status
/// instead of making the runtime boot-critical.
/// </summary>
public sealed class LlamaCppRuntime(LlamaCppRuntimeOptions options) : ILlamaCppRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;

    public LlamaCppRuntimeOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public LlamaCppRuntimeStatus Status => GetStatus();

    public Task<LlamaCppRuntimeStatus> StartIfAlwaysLoadedAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
            return Task.FromResult(GetStatus("The llama.cpp runtime is disabled."));
        if (!Options.AlwaysLoaded)
            return Task.FromResult(GetStatus("Always-loaded mode is disabled."));
        return EnsureStartedAsync(cancellationToken);
    }

    public async Task<LlamaCppRuntimeStatus> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
            return GetStatus("The llama.cpp runtime is disabled.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning(_process)) return GetStatus();
            DisposeExitedProcess();

            LlamaCppLaunchPlan plan;
            try
            {
                plan = LlamaCppLaunchPlanner.Create(Options);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return GetStatus(exception.Message);
            }

            if (!File.Exists(plan.FileName))
                return GetStatus("The configured llama.cpp executable does not exist.");
            if (!File.Exists(Options.ModelPath))
                return GetStatus("The configured GGUF model does not exist.");

            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(plan.FileName) ?? Environment.CurrentDirectory
            };
            foreach (var argument in plan.Arguments) startInfo.ArgumentList.Add(argument);

            try
            {
                _process = Process.Start(startInfo);
                return _process is null
                    ? GetStatus("llama.cpp did not return a process handle.")
                    : GetStatus();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _process = null;
                return GetStatus($"llama.cpp could not start: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            _process = null;
            if (process is null) return;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
                // The owned process exited between the state check and the stop request.
            }
            finally
            {
                process.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
    }

    private LlamaCppRuntimeStatus GetStatus(string? detail = null)
    {
        var running = IsRunning(_process);
        int? processId = null;
        if (running)
        {
            try { processId = _process?.Id; }
            catch (InvalidOperationException) { running = false; }
        }

        Uri? endpoint = Options.Port is >= 1024 and <= 65535
            ? new Uri($"http://127.0.0.1:{Options.Port}/v1/", UriKind.Absolute)
            : null;
        return new LlamaCppRuntimeStatus(Options.Enabled, running, Options.AlwaysLoaded, endpoint, processId, detail);
    }

    private static bool IsRunning(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private void DisposeExitedProcess()
    {
        if (_process is null || IsRunning(_process)) return;
        _process.Dispose();
        _process = null;
    }
}

/// <summary>
/// Explicit composition hook. Haven does not call this automatically, keeping llama.cpp non-boot-critical.
/// </summary>
public static class LlamaCppRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddHavenLlamaCppRuntime(
        this IServiceCollection services,
        LlamaCppRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<LlamaCppRuntimeOptions>(options ?? LlamaCppRuntimeConfiguration.FromEnvironment());
        services.AddSingleton<LlamaCppRuntime>();
        services.AddSingleton<ILlamaCppRuntime>(provider => provider.GetRequiredService<LlamaCppRuntime>());
        return services;
    }
}
