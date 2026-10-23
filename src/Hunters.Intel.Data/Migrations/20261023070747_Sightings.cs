using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sightings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HuntingProfileStates",
                schema: "intel",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    BackfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuntingProfileStates", x => x.Profile);
                });

            migrationBuilder.CreateTable(
                name: "Sightings",
                schema: "intel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KillmailId = table.Column<long>(type: "bigint", nullable: false),
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Hour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PilotId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    TypeId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Indicator = table.Column<bool>(type: "boolean", nullable: false),
                    IsFleet = table.Column<bool>(type: "boolean", nullable: false),
                    AttackerCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sightings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sightings_Profile_KillmailId_PilotId",
                schema: "intel",
                table: "Sightings",
                columns: new[] { "Profile", "KillmailId", "PilotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sightings_Profile_PilotId_SystemId_Hour",
                schema: "intel",
                table: "Sightings",
                columns: new[] { "Profile", "PilotId", "SystemId", "Hour" },
                unique: true,
                filter: "\"PilotId\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_Sightings_SystemId_Time",
                schema: "intel",
                table: "Sightings",
                columns: new[] { "SystemId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_Sightings_Time",
                schema: "intel",
                table: "Sightings",
                column: "Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuntingProfileStates",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "Sightings",
                schema: "intel");
        }
    }
}
