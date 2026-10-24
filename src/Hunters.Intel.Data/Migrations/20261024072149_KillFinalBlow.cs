using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class KillFinalBlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinalBlow",
                schema: "intel",
                table: "Kills",
                type: "integer",
                nullable: false,
                // Kills stored before: unknown (0 would name the first attacker).
                defaultValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalBlow",
                schema: "intel",
                table: "Kills");
        }
    }
}
