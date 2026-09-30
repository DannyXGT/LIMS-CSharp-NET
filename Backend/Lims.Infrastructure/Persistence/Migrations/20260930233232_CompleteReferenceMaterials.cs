using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteReferenceMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "available_quantity",
                table: "reference_materials",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "legacy_id",
                table: "reference_materials",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_material_id",
                table: "reference_materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE reference_materials SET available_quantity = presentation_quantity * package_count");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_replaced_by",
                table: "reference_materials",
                column: "replaced_by_material_id");

            migrationBuilder.CreateIndex(
                name: "ux_reference_materials_legacy_id",
                table: "reference_materials",
                column: "legacy_id",
                unique: true,
                filter: "legacy_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_available_quantity",
                table: "reference_materials",
                sql: "available_quantity >= 0 AND available_quantity <= presentation_quantity * package_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_replacement",
                table: "reference_materials",
                sql: "(status = 'Replaced' AND replaced_by_material_id IS NOT NULL) OR (status <> 'Replaced' AND replaced_by_material_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_status",
                table: "reference_materials",
                sql: "status IN ('Active','Depleted','Expired','Blocked','Replaced','Archived','Retired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_unit",
                table: "reference_materials",
                sql: "unit IN ('Microgram','Milligram','Gram','Kilogram','Milliliter','Liter')");

            migrationBuilder.AddForeignKey(
                name: "FK_reference_materials_reference_materials_replaced_by_materia~",
                table: "reference_materials",
                column: "replaced_by_material_id",
                principalTable: "reference_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reference_materials_reference_materials_replaced_by_materia~",
                table: "reference_materials");

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_replaced_by",
                table: "reference_materials");

            migrationBuilder.DropIndex(
                name: "ux_reference_materials_legacy_id",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_available_quantity",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_replacement",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_status",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_unit",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "available_quantity",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "legacy_id",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "replaced_by_material_id",
                table: "reference_materials");
        }
    }
}
