using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Kills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedPositions",
                schema: "intel",
                columns: table => new
                {
                    Feed = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedPositions", x => x.Feed);
                });

            migrationBuilder.CreateTable(
                name: "Kills",
                schema: "intel",
                columns: table => new
                {
                    KillmailId = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    Npc = table.Column<bool>(type: "boolean", nullable: false),
                    Solo = table.Column<bool>(type: "boolean", nullable: false),
                    TotalValue = table.Column<double>(type: "double precision", nullable: false),
                    VictimCharacterId = table.Column<long>(type: "bigint", nullable: true),
                    VictimCorporationId = table.Column<long>(type: "bigint", nullable: true),
                    VictimAllianceId = table.Column<long>(type: "bigint", nullable: true),
                    VictimShipTypeId = table.Column<int>(type: "integer", nullable: false),
                    VictimFittedTypeIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    AttackerCount = table.Column<int>(type: "integer", nullable: false),
                    AttackerCharacterIds = table.Column<long[]>(type: "bigint[]", nullable: false),
                    AttackerShipTypeIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    AttackerWeaponTypeIds = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kills", x => x.KillmailId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kills_SystemId_Time",
                schema: "intel",
                table: "Kills",
                columns: new[] { "SystemId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_Kills_Time",
                schema: "intel",
                table: "Kills",
                column: "Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedPositions",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "Kills",
                schema: "intel");
        }
    }
}
