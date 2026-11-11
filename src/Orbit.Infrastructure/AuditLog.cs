using Microsoft.EntityFrameworkCore;
using Orbit.Core;

namespace Orbit.Infrastructure;

public sealed class OrbitDbContext(DbContextOptions<OrbitDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(100);
            entity.Property(item => item.Outcome).HasMaxLength(30);
            entity.Property(item => item.Detail).HasMaxLength(2_000);
            entity.HasIndex(item => item.OccurredAt);
        });
    }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
}

public sealed class SqliteAuditLog(IDbContextFactory<OrbitDbContext> contextFactory) : IAuditLog
{
    public async Task WriteAsync(string eventType, string outcome, string detail, Guid? taskId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = eventType,
            Outcome = outcome,
            Detail = detail,
            TaskId = taskId
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
