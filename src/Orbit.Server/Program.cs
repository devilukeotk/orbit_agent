using Orbit.Server;
using Microsoft.EntityFrameworkCore;
using Orbit.Contracts;
using Orbit.Core;
using Orbit.Infrastructure;
using Orbit.WindowsAgent;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Orbit:Port", 5080);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(port);
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

var secrets = new ConfigurationBuilder();
if (builder.Environment.IsDevelopment())
    secrets.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
secrets.AddEnvironmentVariables("ORBIT_AI_KEY_");

builder.Services.AddSingleton(new AiProfiles(
    Path.Combine(builder.Environment.ContentRootPath, "ai-providers.json"),
    secrets.Build()));
builder.Services.AddHttpClient<AiChat>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.MaxResponseContentBufferSize = 1024 * 1024;
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
var databasePath = Path.Combine(builder.Environment.ContentRootPath, "data", "orbit.db");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContextFactory<OrbitDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<IAuditLog, SqliteAuditLog>();
builder.Services.AddSingleton<AuthorizedApplicationCatalog>(_ => new AuthorizedApplicationCatalog(
    Path.Combine(builder.Environment.ContentRootPath, "authorized-applications.json")));
builder.Services.AddSingleton<IApplicationCatalog>(provider => provider.GetRequiredService<AuthorizedApplicationCatalog>());
builder.Services.AddSingleton<IApplicationLauncher, WindowsApplicationLauncher>();
builder.Services.AddSingleton<IToolRegistry>(_ => new ToolRegistry([
    new ToolDescriptor("windows.app.discover", "Find an application in the operator-managed registry.", ToolRiskLevel.Low, TimeSpan.FromSeconds(10), false),
    new ToolDescriptor("windows.app.launch", "Launch an authorized application after explicit confirmation.", ToolRiskLevel.Low, TimeSpan.FromSeconds(30), true)
]));
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrbitDbContext>>();
    await using var context = await database.CreateDbContextAsync();
    await MarkLegacyAuditDatabaseMigratedAsync(context);
    await context.Database.MigrateAsync();
}

app.MapPost("/api/chat", async (ChatRequest request, AiChat chat, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 16_000)
        return Results.BadRequest(new { error = "Send a message of 1 to 16,000 characters." });

    try
    {
        return Results.Ok(await chat.SendAsync(request.Message, cancellationToken));
    }
    catch (AiConfigurationException error)
    {
        return Results.Problem(error.Message, statusCode: 503);
    }
    catch (HttpRequestException error)
    {
        var detail = error.StatusCode is { } status
            ? $"The AI provider returned HTTP {(int)status}."
            : "The AI provider could not be reached.";
        return Results.Problem(detail, statusCode: 502);
    }
    catch (AiResponseException)
    {
        return Results.Problem("The AI provider returned no usable text.", statusCode: 502);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("The AI provider timed out.", statusCode: 504);
    }
});

app.MapGet("/api/tools", (IToolRegistry registry) => Results.Ok(registry.GetAll()));

app.MapPost("/api/tools/windows.app.discover", async (
    ApplicationSearchRequest request,
    AuthorizedApplicationCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 200)
        return Results.BadRequest(new { error = "Send an application query of 1 to 200 characters." });

    try
    {
        return Results.Ok(await catalog.DiscoverAsync(request.Query.Trim(), cancellationToken));
    }
    catch (ApplicationCatalogException error)
    {
        return Results.Problem(error.Message, statusCode: 503);
    }
});

app.MapPost("/api/agent/tasks", async (
    AgentTaskRequest request,
    IAgentOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 16_000)
        return Results.BadRequest(new { error = "Send input of 1 to 16,000 characters." });

    try
    {
        return Results.Ok(await orchestrator.PlanAsync(request.Input.Trim(), cancellationToken));
    }
    catch (ApplicationCatalogException error)
    {
        return Results.Problem(error.Message, statusCode: 503);
    }
});

app.MapPost("/api/agent/run", async (
    HttpContext context, AgentTaskRequest request, IAgentOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]") ||
        context.Request.Headers.ContainsKey("Origin"))
        return Results.StatusCode(403);

    if (string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 16_000)
        return Results.BadRequest(new { error = "Send input of 1 to 16,000 characters." });

    try
    {
        var task = await orchestrator.RunAsync(request.Input.Trim(), cancellationToken);
        return task is null ? Results.NoContent() : Results.Ok(task);
    }
    catch (ApplicationCatalogException error)
    {
        return Results.Problem(error.Message, statusCode: 503);
    }
});

app.MapPost("/api/agent/tasks/launch", async (
    ApplicationLaunchRequest request,
    IAgentOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.ApprovalToken))
        return Results.BadRequest(new { error = "An application ID and approval token are required." });

    try
    {
        return Results.Ok(await orchestrator.ExecuteLaunchAsync(request, cancellationToken));
    }
    catch (ApplicationCatalogException error)
    {
        return Results.Problem(error.Message, statusCode: 503);
    }
});

app.Run();

static async Task MarkLegacyAuditDatabaseMigratedAsync(OrbitDbContext context)
{
    const string initialMigration = "20261111100000_InitialAuditLog";
    var connection = context.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'AuditEvents');";
        var auditEventsTableExists = Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
        if (!auditEventsTableExists)
            return;

        command.CommandText = "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('{initialMigration}', '10.0.13');";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        await connection.CloseAsync();
    }
}

internal sealed record ChatRequest(string? Message);
internal sealed record ApplicationSearchRequest(string? Query);
internal sealed record AgentTaskRequest(string? Input);
