using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class HuntingStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "intel",
                table: "HuntingProfileStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "HuntingRegionStats",
                schema: "intel",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    Week = table.Column<double>(type: "double precision", nullable: false),
                    Month = table.Column<double>(type: "double precision", nullable: false),
                    MonthIndicator = table.Column<double>(type: "double precision", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Pilots = table.Column<int>(type: "integer", nullable: false),
                    Hours = table.Column<double[]>(type: "double precision[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuntingRegionStats", x => new { x.Profile, x.RegionId });
                });

            migrationBuilder.CreateTable(
                name: "HuntingSystemStats",
                schema: "intel",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    Week = table.Column<double>(type: "double precision", nullable: false),
                    Month = table.Column<double>(type: "double precision", nullable: false),
                    MonthIndicator = table.Column<double>(type: "double precision", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Pilots = table.Column<int>(type: "integer", nullable: false),
                    Hours = table.Column<double[]>(type: "double precision[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuntingSystemStats", x => new { x.Profile, x.SystemId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuntingRegionStats",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "HuntingSystemStats",
                schema: "intel");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "intel",
                table: "HuntingProfileStates");
        }
    }
}
