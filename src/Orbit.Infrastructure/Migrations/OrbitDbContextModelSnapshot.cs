using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace Orbit.Infrastructure.Migrations;

[DbContext(typeof(OrbitDbContext))]
public partial class OrbitDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");

        modelBuilder.Entity("Orbit.Infrastructure.AuditEvent", entity =>
        {
            entity.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER")
                .HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<string>("Detail")
                .IsRequired()
                .HasMaxLength(2000)
                .HasColumnType("TEXT");
            entity.Property<string>("EventType")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("TEXT");
            entity.Property<DateTimeOffset>("OccurredAt").HasColumnType("TEXT");
            entity.Property<string>("Outcome")
                .IsRequired()
                .HasMaxLength(30)
                .HasColumnType("TEXT");
            entity.Property<Guid?>("TaskId").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("OccurredAt");
            entity.ToTable("AuditEvents");
        });
    }
}
