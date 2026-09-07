using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using ModelContextProtocol.Server;

namespace Haven.Connector;

[McpServerToolType]
public sealed class SandboxCompatibilityMcpTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "sandbox_preflight")]
    [Description("Compatibility alias during migration from GPTRemote Sandbox. Returns Haven connector workspace and Git preflight state.")]
    public static Task<string> Preflight(
        ConnectorWorkspaceRegistry registry,
        CancellationToken cancellationToken = default) =>
        HavenMcpTools.Preflight(registry, cancellationToken);

    [McpServerTool(Name = "sandbox_project_contexts")]
    [Description("Compatibility alias during migration from GPTRemote Sandbox. Returns configured Haven workspace branch state.")]
    public static async Task<string> ProjectContexts(
        ConnectorWorkspaceRegistry registry,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await registry.InspectAsync(cancellationToken).ConfigureAwait(false), Json);

    [McpServerTool(Name = "sandbox_operation")]
    [Description("Compatibility bridge for a small read-only subset of the former Sandbox REST surface while ChatGPT refreshes to native haven_* tools.")]
    public static async Task<string> OperationAsync(
        ConnectorWorkspaceRegistry registry,
        WorkspaceToolRuntime runtime,
        IWorkspaceToolService tools,
        [Description("GET, POST, PUT, or DELETE")] string method,
        [Description("Supported compatibility path such as /health, /api/preflight, /api/workspace/files/read, or /api/workspace/files/search")] string path,
        [Description("Optional JSON request body")] string? json = null,
        CancellationToken cancellationToken = default)
    {
        var verb = method.Trim().ToUpperInvariant();
        if (verb is not ("GET" or "POST" or "PUT" or "DELETE"))
            return Wrap(400, new { error = "Unsupported method." });

        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            return Wrap(200, new { status = "ok", contractVersion = "1", machine = Environment.MachineName, utc = DateTimeOffset.UtcNow });

        if (path.Equals("/api/preflight", StringComparison.OrdinalIgnoreCase))
        {
            var snapshots = await registry.InspectAsync(cancellationToken).ConfigureAwait(false);
            return Wrap(200, new { status = "ok", contractVersion = "1", machine = Environment.MachineName, utc = DateTimeOffset.UtcNow, projects = snapshots });
        }

        if (verb == "POST" && path.Equals("/api/workspace/files/read", StringComparison.OrdinalIgnoreCase))
        {
            var request = Deserialize<ReadRequest>(json);
            var root = registry.Resolve(request.Project).RootPath;
            var content = await tools.ReadTextAsync(root, request.Path, cancellationToken).ConfigureAwait(false);
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');
            var startLine = Math.Clamp(request.StartLine ?? 1, 1, Math.Max(1, lines.Length));
            var maxLines = Math.Clamp(request.MaxLines ?? 400, 1, 5000);
            var selected = lines.Skip(startLine - 1).Take(maxLines).ToArray();
            var sliced = string.Join('\n', selected);
            var maxCharacters = Math.Clamp(request.MaxCharacters ?? 100_000, 1, 1_000_000);
            var truncated = startLine - 1 + selected.Length < lines.Length || sliced.Length > maxCharacters;
            if (sliced.Length > maxCharacters) sliced = sliced[..maxCharacters];
            var relative = request.Path.Replace('\\', '/');
            return Wrap(200, new
            {
                project = request.Project,
                path = relative,
                startLine,
                endLine = startLine + Math.Max(0, selected.Length - 1),
                totalLines = lines.Length,
                truncated,
                sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
                content = sliced,
                worktreeId = (Guid?)null
            });
        }

        if (verb == "POST" && path.Equals("/api/workspace/files/search", StringComparison.OrdinalIgnoreCase))
        {
            var request = Deserialize<SearchRequest>(json);
            var output = await HavenMcpTools.SearchFiles(
                registry, runtime, request.Project, request.Query, request.Path ?? ".",
                request.MaxResults ?? 100, cancellationToken).ConfigureAwait(false);
            return Wrap(200, new
            {
                project = request.Project,
                query = request.Query,
                text = output,
                truncated = output.Contains("[search result limit reached]", StringComparison.Ordinal),
                nextCursor = (string?)null,
                worktreeId = (Guid?)null
            });
        }

        return Wrap(404, new
        {
            error = "This GPTRemote compatibility operation is not exposed by Haven. Use native haven_* tools for write, build, test, and Git operations."
        });
    }

    private static T Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("A JSON request body is required.");
        return JsonSerializer.Deserialize<T>(json, Json)
            ?? throw new ArgumentException("The JSON request body is invalid.");
    }

    private static string Wrap(int statusCode, object body) =>
        JsonSerializer.Serialize(new
        {
            statusCode,
            success = statusCode is >= 200 and < 300,
            body = JsonSerializer.Serialize(body, Json)
        }, Json);

    private sealed record ReadRequest(
        string Project,
        string Path,
        int? StartLine = null,
        int? MaxLines = null,
        int? MaxCharacters = null);

    private sealed record SearchRequest(
        string Project,
        string Query,
        string? Path = null,
        int? MaxResults = null);
}
