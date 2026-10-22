using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "intel");

            migrationBuilder.CreateTable(
                name: "SystemJumps",
                schema: "intel",
                columns: table => new
                {
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SystemIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ShipJumps = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemJumps", x => x.PeriodEnd);
                });

            migrationBuilder.CreateTable(
                name: "SystemKills",
                schema: "intel",
                columns: table => new
                {
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SystemIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ShipKills = table.Column<int[]>(type: "integer[]", nullable: false),
                    PodKills = table.Column<int[]>(type: "integer[]", nullable: false),
                    NpcKills = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemKills", x => x.PeriodEnd);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemJumps",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "SystemKills",
                schema: "intel");
        }
    }
}
