using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertifiedCommercialMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_components_source",
                table: "reference_preparation_components");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_purity",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_preparation_id",
                table: "reference_preparation_components",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "source_material_id",
                table: "reference_preparation_components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "purity_percent",
                table: "reference_materials",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,4)",
                oldPrecision: 7,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "commercial_json",
                table: "reference_materials",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_expiration_date",
                table: "reference_materials",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opened_date",
                table: "reference_materials",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "opening_required",
                table: "reference_materials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "presentation",
                table: "reference_materials",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Solid");

            migrationBuilder.CreateTable(
                name: "reference_certified_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    cas_number = table.Column<string>(type: "text", nullable: true),
                    certified_concentration = table.Column<decimal>(type: "numeric", nullable: false),
                    concentration_unit = table.Column<string>(type: "text", nullable: false),
                    expanded_uncertainty = table.Column<decimal>(type: "numeric", nullable: true),
                    purity_percent = table.Column<decimal>(type: "numeric", nullable: true),
                    lot = table.Column<string>(type: "text", nullable: true),
                    additional_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_certified_components", x => x.id);
                    table.CheckConstraint("ck_certified_component_values", "certified_concentration > 0 AND (expanded_uncertainty IS NULL OR expanded_uncertainty >= 0) AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100))");
                    table.ForeignKey(
                        name: "FK_reference_certified_components_reference_materials_material~",
                        column: x => x.material_id,
                        principalTable: "reference_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reference_material_opening_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_date = table.Column<DateOnly>(type: "date", nullable: true),
                    opened_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actor_user_id = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_material_opening_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_reference_material_opening_history_reference_materials_mate~",
                        column: x => x.material_id,
                        principalTable: "reference_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_material_opening_history_usuarios_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });


            migrationBuilder.CreateIndex(
                name: "IX_reference_preparation_components_preparation_id_source_mate~",
                table: "reference_preparation_components",
                columns: ["preparation_id", "source_material_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparation_components_source_material_id",
                table: "reference_preparation_components",
                column: "source_material_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_components_source",
                table: "reference_preparation_components",
                sql: "(source_preparation_id IS NOT NULL AND source_material_id IS NULL AND preparation_id <> source_preparation_id AND source_type IN ('Stock','Intermedia')) OR (source_preparation_id IS NULL AND source_material_id IS NOT NULL AND source_type IN ('CommercialSolution','CommercialMix'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials",
                sql: "presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND package_count = 1 AND effective_expiration_date <= expiration_date))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_purity",
                table: "reference_materials",
                sql: "(presentation = 'Solid' AND purity_percent IS NOT NULL AND purity_percent > 0 AND purity_percent <= 100) OR (presentation <> 'Solid' AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements",
                sql: "(kind = 'StockConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = 'IntermediateConsumption' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL) OR (kind = 'CommercialConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_reference_certified_components_material_id",
                table: "reference_certified_components",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_opening_history_actor_user_id",
                table: "reference_material_opening_history",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_opening_history_material_id",
                table: "reference_material_opening_history",
                column: "material_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reference_preparation_components_reference_materials_source~",
                table: "reference_preparation_components",
                column: "source_material_id",
                principalTable: "reference_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM reference_materials WHERE presentation <> 'Solid') THEN RAISE EXCEPTION 'Commercial materials exist; rollback requires an explicit retention plan.'; END IF; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_reference_preparation_components_reference_materials_source~",
                table: "reference_preparation_components");

            migrationBuilder.DropTable(
                name: "reference_certified_components");

            migrationBuilder.DropTable(
                name: "reference_material_opening_history");

            migrationBuilder.DropIndex(
                name: "IX_reference_preparation_components_preparation_id_source_mate~",
                table: "reference_preparation_components");

            migrationBuilder.DropIndex(
                name: "IX_reference_preparation_components_source_material_id",
                table: "reference_preparation_components");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_components_source",
                table: "reference_preparation_components");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_presentation",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_materials_purity",
                table: "reference_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements");

            migrationBuilder.DropColumn(
                name: "source_material_id",
                table: "reference_preparation_components");

            migrationBuilder.DropColumn(
                name: "commercial_json",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "effective_expiration_date",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "opened_date",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "opening_required",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "presentation",
                table: "reference_materials");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_preparation_id",
                table: "reference_preparation_components",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "purity_percent",
                table: "reference_materials",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,4)",
                oldPrecision: 7,
                oldScale: 4,
                oldNullable: true);


            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_components_source",
                table: "reference_preparation_components",
                sql: "preparation_id <> source_preparation_id AND source_type IN ('Stock','Intermedia')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_materials_purity",
                table: "reference_materials",
                sql: "purity_percent > 0 AND purity_percent <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements",
                sql: "(kind = 'StockConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = 'IntermediateConsumption' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL)");
        }
    }
}
