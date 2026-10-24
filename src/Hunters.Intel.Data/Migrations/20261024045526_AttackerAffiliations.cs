using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewEdenSoft.Hunters.Intel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttackerAffiliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long[]>(
                name: "AttackerAllianceIds",
                schema: "intel",
                table: "Kills",
                type: "bigint[]",
                nullable: false,
                defaultValue: new long[0]);

            migrationBuilder.AddColumn<long[]>(
                name: "AttackerCorporationIds",
                schema: "intel",
                table: "Kills",
                type: "bigint[]",
                nullable: false,
                defaultValue: new long[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttackerAllianceIds",
                schema: "intel",
                table: "Kills");

            migrationBuilder.DropColumn(
                name: "AttackerCorporationIds",
                schema: "intel",
                table: "Kills");
        }
    }
}
