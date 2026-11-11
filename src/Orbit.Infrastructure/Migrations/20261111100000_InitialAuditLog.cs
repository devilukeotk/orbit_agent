using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbit.Infrastructure.Migrations;

[DbContext(typeof(OrbitDbContext))]
[Migration("20261111100000_InitialAuditLog")]
public partial class InitialAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Outcome = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                TaskId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AuditEvents", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_OccurredAt",
            table: "AuditEvents",
            column: "OccurredAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "AuditEvents");
}
