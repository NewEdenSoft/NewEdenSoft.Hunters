using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemJumps",
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
                name: "SystemJumps");

            migrationBuilder.DropTable(
                name: "SystemKills");
        }
    }
}
