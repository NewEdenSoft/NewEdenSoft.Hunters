using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class KillFittedCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "VictimFittedCounts",
                schema: "intel",
                table: "Kills",
                type: "integer[]",
                nullable: false,
                // Kills stored before: unknown, until fetched again from ESI (KillRefetchService).
                defaultValue: new int[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VictimFittedCounts",
                schema: "intel",
                table: "Kills");
        }
    }
}
