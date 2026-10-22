using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Applied",
                schema: "intel",
                table: "SystemKills",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Applied",
                schema: "intel",
                table: "SystemJumps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ActivityWeekSamples",
                schema: "intel",
                columns: table => new
                {
                    HourOfWeek = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KillHours = table.Column<int>(type: "integer", nullable: false),
                    JumpHours = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityWeekSamples", x => x.HourOfWeek);
                });

            migrationBuilder.CreateTable(
                name: "RegionActivityCurrent",
                schema: "intel",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    KillsPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JumpsPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KillsDayHours = table.Column<short>(type: "smallint", nullable: false),
                    JumpsDayHours = table.Column<short>(type: "smallint", nullable: false),
                    NpcKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    NpcKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    NpcKills_Day = table.Column<int>(type: "integer", nullable: false),
                    NpcKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    NpcKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    NpcKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    NpcKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    ShipKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    ShipKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    ShipKills_Day = table.Column<int>(type: "integer", nullable: false),
                    ShipKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    ShipKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    ShipKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    ShipKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    PodKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    PodKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    PodKills_Day = table.Column<int>(type: "integer", nullable: false),
                    PodKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    PodKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    PodKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    PodKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    ShipJumps_LastHour = table.Column<int>(type: "integer", nullable: false),
                    ShipJumps_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    ShipJumps_Day = table.Column<int>(type: "integer", nullable: false),
                    ShipJumps_Typical = table.Column<double>(type: "double precision", nullable: true),
                    ShipJumps_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    ShipJumps_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    ShipJumps_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionActivityCurrent", x => x.RegionId);
                });

            migrationBuilder.CreateTable(
                name: "RegionActivityWeek",
                schema: "intel",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    HourOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    NpcKills = table.Column<long>(type: "bigint", nullable: false),
                    ShipKills = table.Column<long>(type: "bigint", nullable: false),
                    PodKills = table.Column<long>(type: "bigint", nullable: false),
                    ShipJumps = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionActivityWeek", x => new { x.RegionId, x.HourOfWeek });
                });

            migrationBuilder.CreateTable(
                name: "SystemActivityCurrent",
                schema: "intel",
                columns: table => new
                {
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    KillsPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JumpsPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KillsDayHours = table.Column<short>(type: "smallint", nullable: false),
                    JumpsDayHours = table.Column<short>(type: "smallint", nullable: false),
                    NpcKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    NpcKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    NpcKills_Day = table.Column<int>(type: "integer", nullable: false),
                    NpcKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    NpcKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    NpcKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    NpcKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    ShipKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    ShipKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    ShipKills_Day = table.Column<int>(type: "integer", nullable: false),
                    ShipKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    ShipKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    ShipKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    ShipKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    PodKills_LastHour = table.Column<int>(type: "integer", nullable: false),
                    PodKills_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    PodKills_Day = table.Column<int>(type: "integer", nullable: false),
                    PodKills_Typical = table.Column<double>(type: "double precision", nullable: true),
                    PodKills_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    PodKills_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    PodKills_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true),
                    ShipJumps_LastHour = table.Column<int>(type: "integer", nullable: false),
                    ShipJumps_PreviousHour = table.Column<int>(type: "integer", nullable: true),
                    ShipJumps_Day = table.Column<int>(type: "integer", nullable: false),
                    ShipJumps_Typical = table.Column<double>(type: "double precision", nullable: true),
                    ShipJumps_PeakRatio = table.Column<double>(type: "double precision", nullable: true),
                    ShipJumps_HoursToPeak = table.Column<short>(type: "smallint", nullable: true),
                    ShipJumps_PeakHourOfWeek = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemActivityCurrent", x => x.SystemId);
                });

            migrationBuilder.CreateTable(
                name: "SystemActivityWeek",
                schema: "intel",
                columns: table => new
                {
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    HourOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    NpcKills = table.Column<long>(type: "bigint", nullable: false),
                    ShipKills = table.Column<long>(type: "bigint", nullable: false),
                    PodKills = table.Column<long>(type: "bigint", nullable: false),
                    ShipJumps = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemActivityWeek", x => new { x.SystemId, x.HourOfWeek });
                });

            migrationBuilder.CreateTable(
                name: "Versions",
                schema: "intel",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Versions", x => x.Dataset);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityWeekSamples",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "RegionActivityCurrent",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "RegionActivityWeek",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "SystemActivityCurrent",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "SystemActivityWeek",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "Versions",
                schema: "intel");

            migrationBuilder.DropColumn(
                name: "Applied",
                schema: "intel",
                table: "SystemKills");

            migrationBuilder.DropColumn(
                name: "Applied",
                schema: "intel",
                table: "SystemJumps");
        }
    }
}
