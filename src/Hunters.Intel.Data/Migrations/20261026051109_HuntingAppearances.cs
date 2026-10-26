using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class HuntingAppearances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HuntingAppearances",
                schema: "intel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KillmailId = table.Column<long>(type: "bigint", nullable: false),
                    SystemId = table.Column<int>(type: "integer", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PilotId = table.Column<long>(type: "bigint", nullable: false),
                    IsFleet = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuntingAppearances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HuntingAppearances_Profile_KillmailId_PilotId",
                schema: "intel",
                table: "HuntingAppearances",
                columns: new[] { "Profile", "KillmailId", "PilotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuntingAppearances_Time",
                schema: "intel",
                table: "HuntingAppearances",
                column: "Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuntingAppearances",
                schema: "intel");
        }
    }
}
