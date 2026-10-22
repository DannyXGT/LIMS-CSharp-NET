using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCommercialBottleCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials",
                sql: "presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND effective_expiration_date <= expiration_date))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials",
                sql: "presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND package_count = 1 AND effective_expiration_date <= expiration_date))");
        }
    }
}
