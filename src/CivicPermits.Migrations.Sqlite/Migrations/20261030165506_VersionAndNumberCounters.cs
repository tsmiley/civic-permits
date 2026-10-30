using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicPermits.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class VersionAndNumberCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "PermitApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "PermitNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSequence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitNumberCounters", x => x.Year);
                    table.CheckConstraint("CK_PermitNumberCounters_LastSequence", "[LastSequence] >= 0 AND [LastSequence] <= 999999");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermitNumberCounters");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PermitApplications");
        }
    }
}
