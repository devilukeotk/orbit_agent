using Orbit.WindowsAgent;
using Xunit;

namespace Orbit.WindowsAgent.Tests;

public sealed class AuthorizedApplicationCatalogTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"orbit-tests-{Guid.NewGuid():N}");
    private string RegistryPath => Path.Combine(_directory, "applications.json");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return File.WriteAllTextAsync(RegistryPath, """
            { "applications": [
              { "id": "notepad", "displayName": "Notepad", "executable": "notepad.exe", "arguments": [] }
            ] }
            """);
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchReturnsAuthorizedConfiguredApplication()
    {
        var catalog = new AuthorizedApplicationCatalog(RegistryPath);

        var candidates = await catalog.SearchAsync("note", CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.IsAuthorized);
        Assert.Equal("notepad", candidate.Id);
    }
}
