using System.Text.Json;
using Haven.Application;

namespace Haven.Connector;

public sealed record ConnectorWorkspaceDefinition(
    string Key,
    string DisplayName,
    string RootPath,
    string? ApprovedBranch = null);

public sealed record ConnectorWorkspaceSnapshot(
    string Key,
    string DisplayName,
    string RootPath,
    string? ApprovedBranch,
    string? CurrentBranch,
    string? HeadSha,
    bool GitRepository,
    bool Dirty,
    bool BranchMatches,
    string? Issue);

internal sealed record ConnectorWorkspaceConfiguration(
    IReadOnlyList<ConnectorWorkspaceDefinition> Workspaces);

public sealed class ConnectorWorkspaceRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWorkspaceToolService _tools;
    private readonly IReadOnlyDictionary<string, ConnectorWorkspaceDefinition> _workspaces;

    public ConnectorWorkspaceRegistry(IWorkspaceToolService tools)
    {
        _tools = tools;
        ConfigurationPath = ResolveConfigurationPath();
        _workspaces = Load(ConfigurationPath);
    }

    public string ConfigurationPath { get; }

    public IReadOnlyList<ConnectorWorkspaceDefinition> List() =>
        _workspaces.Values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray();

    public ConnectorWorkspaceDefinition Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A workspace key is required.", nameof(key));
        if (!_workspaces.TryGetValue(key.Trim(), out var workspace))
            throw new KeyNotFoundException($"Workspace '{key}' is not configured for the Haven connector.");
        if (!Directory.Exists(workspace.RootPath))
            throw new DirectoryNotFoundException($"Configured workspace '{workspace.Key}' does not exist: {workspace.RootPath}");
        return workspace;
    }

    public async Task<IReadOnlyList<ConnectorWorkspaceSnapshot>> InspectAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<ConnectorWorkspaceSnapshot>(_workspaces.Count);
        foreach (var workspace in List())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(workspace.RootPath))
            {
                snapshots.Add(new ConnectorWorkspaceSnapshot(
                    workspace.Key, workspace.DisplayName, workspace.RootPath, workspace.ApprovedBranch,
                    null, null, false, false, false, "Workspace root does not exist."));
                continue;
            }

            var branch = await RunGitAsync(workspace.RootPath, "branch --show-current", cancellationToken).ConfigureAwait(false);
            var head = await RunGitAsync(workspace.RootPath, "rev-parse HEAD", cancellationToken).ConfigureAwait(false);
            var status = await RunGitAsync(workspace.RootPath, "status --porcelain", cancellationToken).ConfigureAwait(false);
            var gitRepository = branch.ExitCode == 0 && head.ExitCode == 0;
            var currentBranch = gitRepository ? branch.StandardOutput.Trim() : null;
            var headSha = gitRepository ? head.StandardOutput.Trim() : null;
            var dirty = gitRepository && !string.IsNullOrWhiteSpace(status.StandardOutput);
            var branchMatches = string.IsNullOrWhiteSpace(workspace.ApprovedBranch) ||
                                string.Equals(workspace.ApprovedBranch, currentBranch, StringComparison.OrdinalIgnoreCase);
            var issue = !gitRepository
                ? "Workspace is not a Git repository."
                : branchMatches
                    ? null
                    : $"Configured branch is '{workspace.ApprovedBranch}' but checkout is '{currentBranch}'.";

            snapshots.Add(new ConnectorWorkspaceSnapshot(
                workspace.Key, workspace.DisplayName, workspace.RootPath, workspace.ApprovedBranch,
                currentBranch, headSha, gitRepository, dirty, branchMatches, issue));
        }

        return snapshots;
    }

    private Task<ProcessResult> RunGitAsync(string root, string arguments, CancellationToken cancellationToken) =>
        _tools.RunProcessAsync(
            new ProcessRequest("git", arguments, root, TimeSpan.FromSeconds(15)),
            cancellationToken);

    private static string ResolveConfigurationPath()
    {
        var configured = Environment.GetEnvironmentVariable("HAVEN_CONNECTOR_WORKSPACES");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "HavenConnector", "workspaces.json");
    }

    private static IReadOnlyDictionary<string, ConnectorWorkspaceDefinition> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, ConnectorWorkspaceDefinition>(StringComparer.OrdinalIgnoreCase);

        ConnectorWorkspaceConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<ConnectorWorkspaceConfiguration>(File.ReadAllText(path), Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Haven connector workspace configuration is invalid: {path}", exception);
        }

        var result = new Dictionary<string, ConnectorWorkspaceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in configuration?.Workspaces ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.RootPath))
                throw new InvalidOperationException("Every Haven connector workspace requires a key and rootPath.");

            var key = item.Key.Trim();
            var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(item.RootPath.Trim()))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? key : item.DisplayName.Trim();
            var approvedBranch = string.IsNullOrWhiteSpace(item.ApprovedBranch) ? null : item.ApprovedBranch.Trim();

            if (!result.TryAdd(key, new ConnectorWorkspaceDefinition(key, displayName, root, approvedBranch)))
                throw new InvalidOperationException($"Duplicate Haven connector workspace key '{key}'.");
        }

        return result;
    }
}
