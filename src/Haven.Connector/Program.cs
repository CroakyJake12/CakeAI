using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Connector;
using Haven.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var listenUrl = Environment.GetEnvironmentVariable("HAVEN_CONNECTOR_URLS");
builder.WebHost.UseUrls(string.IsNullOrWhiteSpace(listenUrl) ? "http://127.0.0.1:7148" : listenUrl);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<IWorkspaceToolService, WorkspaceToolService>();
builder.Services.AddSingleton<WorkspaceToolRuntime>();
builder.Services.AddSingleton<ConnectorWorkspaceRegistry>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<HavenMcpTools>()
    .WithTools<SandboxCompatibilityMcpTools>();

var app = builder.Build();

var apiKey = Environment.GetEnvironmentVariable("HAVEN_CONNECTOR_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    apiKey = Environment.GetEnvironmentVariable("VS_BUILD_AGENT_KEY");
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 24)
{
    Console.Error.WriteLine(
        "Haven Connector did not start: set HAVEN_CONNECTOR_KEY or VS_BUILD_AGENT_KEY to a random secret of at least 24 characters.");
    Environment.ExitCode = 2;
    return;
}

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-Haven-Connector-Key"].ToString();
    if (string.IsNullOrWhiteSpace(supplied))
        supplied = context.Request.Headers["X-VS-Build-Agent-Key"].ToString();

    if (!FixedTimeEquals(apiKey, supplied))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing Haven connector API key." });
        return;
    }

    await next();
});

static IResult ProtectedResourceMetadata(HttpContext context)
{
    var resource = $"{context.Request.Scheme}://{context.Request.Host}/mcp";
    return Results.Ok(new
    {
        resource,
        authorization_servers = Array.Empty<string>(),
        scopes_supported = Array.Empty<string>(),
        bearer_methods_supported = Array.Empty<string>()
    });
}

// OpenAI Secure MCP Tunnel checks RFC 9728 discovery metadata even for a
// ChatGPT app configured as No Auth. The tunnel injects the loopback API key.
app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResourceMetadata);
app.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadata);

app.MapGet("/health", async (ConnectorWorkspaceRegistry registry, CancellationToken cancellationToken) =>
{
    var workspaces = await registry.InspectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = "ok",
        contractVersion = "1",
        machine = Environment.MachineName,
        utc = DateTimeOffset.UtcNow,
        workspaceCount = workspaces.Count,
        capabilities = new
        {
            workspaceRead = true,
            workspaceEdits = true,
            transactionalChangeSets = true,
            build = true,
            tests = true,
            gitRead = true,
            arbitraryRootAccess = false,
            arbitraryShell = false
        },
        workspaces
    });
});

app.MapMcp("/mcp");
await app.RunAsync();

static bool FixedTimeEquals(string expected, string actual)
{
    if (string.IsNullOrEmpty(actual)) return false;
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var actualBytes = Encoding.UTF8.GetBytes(actual);
    return expectedBytes.Length == actualBytes.Length &&
           CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
}
