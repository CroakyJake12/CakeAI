using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Haven.Application;
using ModelContextProtocol.Server;

namespace Haven.Connector;

[McpServerToolType]
public sealed class HavenMcpTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "haven_preflight")]
    [Description("Inspect the Haven connector, its allowlisted workspaces, actual Git branches, dirty state, and current commit before source work.")]
    public static async Task<string> Preflight(
        ConnectorWorkspaceRegistry registry,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(new
        {
            status = "ok",
            contractVersion = "1",
            machine = Environment.MachineName,
            utc = DateTimeOffset.UtcNow,
            configurationPath = registry.ConfigurationPath,
            workspaces = await registry.InspectAsync(cancellationToken).ConfigureAwait(false),
            selectionRule = "Use only a configured workspace key. Do not infer or supply arbitrary filesystem roots."
        }, Json);

    [McpServerTool(Name = "haven_list_files")]
    [Description("List files and folders inside one configured Haven connector workspace. Paths are workspace-relative and cannot escape the configured root.")]
    public static Task<string> ListFiles(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string path = ".",
        int maxDepth = 5,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "list_files",
            new() { ["path"] = path, ["max_depth"] = Math.Clamp(maxDepth, 1, 10) }, cancellationToken);

    [McpServerTool(Name = "haven_read_file")]
    [Description("Read a UTF-8 text file from one configured Haven connector workspace.")]
    public static Task<string> ReadFile(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string path,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "read_file",
            new() { ["path"] = path }, cancellationToken);

    [McpServerTool(Name = "haven_search_files")]
    [Description("Search text files inside one configured Haven connector workspace for a literal query.")]
    public static Task<string> SearchFiles(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string query,
        string path = ".",
        int maxResults = 100,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "search_files",
            new() { ["query"] = query, ["path"] = path, ["max_results"] = Math.Clamp(maxResults, 1, 200) }, cancellationToken);

    [McpServerTool(Name = "haven_write_file")]
    [Description("Create or atomically replace a UTF-8 text file inside one configured Haven connector workspace.")]
    public static Task<string> WriteFile(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string path,
        string content,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "write_file",
            new() { ["path"] = path, ["content"] = content }, cancellationToken);

    [McpServerTool(Name = "haven_replace_in_file")]
    [Description("Replace exact text in a file inside one configured Haven connector workspace. Prefer this over whole-file replacement for focused edits.")]
    public static Task<string> ReplaceInFile(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string path,
        string oldText,
        string newText,
        bool replaceAll = false,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "replace_in_file",
            new()
            {
                ["path"] = path,
                ["old_text"] = oldText,
                ["new_text"] = newText,
                ["replace_all"] = replaceAll
            }, cancellationToken);

    [McpServerTool(Name = "haven_preview_change_set")]
    [Description("Preflight a transactional multi-file Haven workspace change set without writing files.")]
    public static Task<string> PreviewChangeSet(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string changesJson,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "preview_change_set",
            new() { ["changes_json"] = changesJson }, cancellationToken);

    [McpServerTool(Name = "haven_apply_change_set")]
    [Description("Apply a transactional multi-file Haven workspace change set. Haven rolls back earlier writes if a later write fails.")]
    public static Task<string> ApplyChangeSet(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string changesJson,
        CancellationToken cancellationToken = default) =>
        RunWorkspaceAsync(registry, runtime, workspace, "apply_change_set",
            new() { ["changes_json"] = changesJson }, cancellationToken);

    [McpServerTool(Name = "haven_run_tests")]
    [Description("Run tests for a configured Haven connector workspace. Optionally target one workspace-relative .sln, .slnx, or .csproj. The connector constructs dotnet test itself and never accepts an arbitrary shell command.")]
    public static async Task<string> RunTests(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        IWorkspaceToolService tools,
        string workspace,
        string? target = null,
        string configuration = "Debug",
        int timeoutSeconds = 900,
        CancellationToken cancellationToken = default)
    {
        var root = registry.Resolve(workspace).RootPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            return await RunWorkspaceAsync(registry, runtime, workspace, "run_tests",
                new() { ["command"] = string.Empty, ["timeout_seconds"] = Math.Clamp(timeoutSeconds, 1, 1800) }, cancellationToken)
                .ConfigureAwait(false);
        }

        var normalized = NormalizeConfiguration(configuration);
        var selectedTarget = ResolveDotNetTarget(tools, root, target, nameof(target));
        var result = await tools.RunProcessAsync(
            new ProcessRequest(
                "dotnet",
                $"test {Quote(selectedTarget)} --configuration {normalized}",
                root,
                TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 1800))),
            cancellationToken).ConfigureAwait(false);
        return FormatProcess(result);
    }

    [McpServerTool(Name = "haven_build")]
    [Description("Build a configured Haven connector workspace with dotnet build. Optionally target one workspace-relative .sln, .slnx, or .csproj. Configuration must be Debug or Release; no arbitrary shell command is accepted.")]
    public static async Task<string> Build(
        ConnectorWorkspaceRegistry registry,
        IWorkspaceToolService tools,
        string workspace,
        string? target = null,
        string configuration = "Debug",
        int timeoutSeconds = 900,
        CancellationToken cancellationToken = default)
    {
        var root = registry.Resolve(workspace).RootPath;
        var normalized = NormalizeConfiguration(configuration);
        string? selectedTarget;

        if (!string.IsNullOrWhiteSpace(target))
        {
            selectedTarget = ResolveDotNetTarget(tools, root, target, nameof(target));
        }
        else
        {
            var solutions = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (solutions.Length > 1)
                throw new InvalidOperationException("Multiple solution files were found. Specify the workspace-relative target to build.");

            selectedTarget = solutions.Length == 1 ? Path.GetFileName(solutions[0]) : null;
        }

        var arguments = selectedTarget is null
            ? $"build --configuration {normalized}"
            : $"build {Quote(selectedTarget)} --configuration {normalized}";

        var result = await tools.RunProcessAsync(
            new ProcessRequest("dotnet", arguments, root, TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 1800))),
            cancellationToken).ConfigureAwait(false);
        return FormatProcess(result);
    }

    [McpServerTool(Name = "haven_git_status")]
    [Description("Read Git branch and working-tree status for one configured Haven connector workspace.")]
    public static async Task<string> GitStatus(
        ConnectorWorkspaceRegistry registry,
        IWorkspaceToolService tools,
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var root = registry.Resolve(workspace).RootPath;
        var result = await tools.RunProcessAsync(
            new ProcessRequest("git", "status --short --branch", root, TimeSpan.FromSeconds(30)),
            cancellationToken).ConfigureAwait(false);
        return FormatProcess(result);
    }

    [McpServerTool(Name = "haven_git_diff")]
    [Description("Read the current unstaged or staged Git diff for one configured Haven connector workspace.")]
    public static async Task<string> GitDiff(
        ConnectorWorkspaceRegistry registry,
        IWorkspaceToolService tools,
        string workspace,
        bool staged = false,
        CancellationToken cancellationToken = default)
    {
        var root = registry.Resolve(workspace).RootPath;
        var arguments = staged ? "diff --no-ext-diff --cached" : "diff --no-ext-diff";
        var result = await tools.RunProcessAsync(
            new ProcessRequest("git", arguments, root, TimeSpan.FromSeconds(60)),
            cancellationToken).ConfigureAwait(false);
        return FormatProcess(result);
    }

    internal static async Task<string> RunWorkspaceAsync(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        string workspace,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var root = registry.Resolve(workspace).RootPath;
        var callArguments = arguments.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value, Json),
            StringComparer.Ordinal);
        var result = await runtime.ExecuteAsync(root, new OllamaToolCall(toolName, callArguments), cancellationToken)
            .ConfigureAwait(false);
        return result.Output;
    }

    internal static string FormatProcess(ProcessResult result)
    {
        var builder = new StringBuilder();
        builder.Append("Exit code: ").Append(result.ExitCode)
            .Append(" | Duration: ").Append(result.Duration.TotalSeconds.ToString("0.0")).AppendLine("s");
        if (result.TimedOut) builder.AppendLine("Process timed out.");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            builder.AppendLine("STDOUT:").AppendLine(result.StandardOutput.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            builder.AppendLine("STDERR:").AppendLine(result.StandardError.TrimEnd());
        return builder.ToString().TrimEnd();
    }

    private static string NormalizeConfiguration(string configuration)
    {
        var normalized = configuration.Trim();
        if (!normalized.Equals("Debug", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("Release", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("configuration must be Debug or Release.", nameof(configuration));

        return normalized.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
    }

    private static string ResolveDotNetTarget(
        IWorkspaceToolService tools,
        string root,
        string target,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("A target path is required.", parameterName);

        var resolved = tools.ResolveWorkspacePath(root, target.Trim());
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"Build/test target does not exist: {target}", resolved);

        var extension = Path.GetExtension(resolved);
        if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("target must reference a .sln, .slnx, or .csproj inside the configured workspace.", parameterName);

        return Path.GetRelativePath(root, resolved);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

