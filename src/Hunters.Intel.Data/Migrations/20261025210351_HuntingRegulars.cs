using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class HuntingRegulars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HuntingRegulars",
                schema: "intel",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    PilotId = table.Column<long>(type: "bigint", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    DaysInWindow = table.Column<int>(type: "integer", nullable: false),
                    WindowStart = table.Column<int>(type: "integer", nullable: false),
                    WindowHours = table.Column<int>(type: "integer", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Local = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuntingRegulars", x => new { x.Profile, x.SystemId, x.PilotId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuntingRegulars",
                schema: "intel");
        }
    }
}
