using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Orbit.Infrastructure;
using Xunit;

namespace Orbit.Core.Tests;

public sealed class SqliteMigrationTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"orbit-migration-tests-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_directory, "orbit.db");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitialMigrationCreatesTheAuditLogSchema()
    {
        var options = new DbContextOptionsBuilder<OrbitDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;
        await using (var context = new OrbitDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                EventType = "test",
                Outcome = "completed",
                Detail = "Migration verification"
            });
            await context.SaveChangesAsync();

            Assert.Single(await context.AuditEvents.ToListAsync());
        }
    }
}
