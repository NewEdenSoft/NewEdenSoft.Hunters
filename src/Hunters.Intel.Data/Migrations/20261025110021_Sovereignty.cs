using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sovereignty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Local",
                schema: "intel",
                table: "Sightings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LocalPilots",
                schema: "intel",
                table: "HuntingSystemStats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "MonthLocal",
                schema: "intel",
                table: "HuntingSystemStats",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "LocalPilots",
                schema: "intel",
                table: "HuntingRegionStats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "MonthLocal",
                schema: "intel",
                table: "HuntingRegionStats",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "SystemSovereignty",
                schema: "intel",
                columns: table => new
                {
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    AllianceId = table.Column<long>(type: "bigint", nullable: false),
                    CorporationId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSovereignty", x => x.SystemId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSovereignty",
                schema: "intel");

            migrationBuilder.DropColumn(
                name: "Local",
                schema: "intel",
                table: "Sightings");

            migrationBuilder.DropColumn(
                name: "LocalPilots",
                schema: "intel",
                table: "HuntingSystemStats");

            migrationBuilder.DropColumn(
                name: "MonthLocal",
                schema: "intel",
                table: "HuntingSystemStats");

            migrationBuilder.DropColumn(
                name: "LocalPilots",
                schema: "intel",
                table: "HuntingRegionStats");

            migrationBuilder.DropColumn(
                name: "MonthLocal",
                schema: "intel",
                table: "HuntingRegionStats");
        }
    }
}
