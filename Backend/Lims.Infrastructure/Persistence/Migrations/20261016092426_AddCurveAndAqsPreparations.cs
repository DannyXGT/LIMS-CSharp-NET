using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurveAndAqsPreparations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations");

            migrationBuilder.InsertData(
                table: "permission_catalog",
                column: "permission_code",
                values: new object[]
                {
                    "aqs.create",
                    "aqs.view",
                    "curva.create",
                    "curva.view"
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations",
                sql: "kind IN ('Stock','Intermedia','Curva','AQS') AND status = 'Active' AND (kind <> 'Stock' OR source_material_id IS NOT NULL) AND (kind = 'Stock' OR (source_material_id IS NULL AND method_id IS NOT NULL)) AND (kind NOT IN ('Curva','AQS') OR available_volume = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations");

            migrationBuilder.DeleteData(
                table: "permission_catalog",
                keyColumn: "permission_code",
                keyValue: "aqs.create");

            migrationBuilder.DeleteData(
                table: "permission_catalog",
                keyColumn: "permission_code",
                keyValue: "aqs.view");

            migrationBuilder.DeleteData(
                table: "permission_catalog",
                keyColumn: "permission_code",
                keyValue: "curva.create");

            migrationBuilder.DeleteData(
                table: "permission_catalog",
                keyColumn: "permission_code",
                keyValue: "curva.view");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations",
                sql: "kind IN ('Stock','Intermedia') AND status = 'Active' AND (kind <> 'Stock' OR source_material_id IS NOT NULL) AND (kind <> 'Intermedia' OR (source_material_id IS NULL AND method_id IS NOT NULL))");
        }
    }
}
