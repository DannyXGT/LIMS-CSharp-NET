using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PausePreparationDateValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_dates",
                table: "reference_materials");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "updated_at >= created_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_dates",
                table: "reference_materials",
                sql: "presentation <> 'Solid' OR expiration_date >= received_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_dates",
                table: "reference_materials");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "(kind = 'Stock' OR expiration_date >= preparation_date) AND updated_at >= created_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_dates",
                table: "reference_materials",
                sql: "expiration_date >= received_date");
        }
    }
}
