using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF-generated migration metadata arrays.

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntermediatePreparations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reference_material_movements_reference_preparations_prepara~",
                table: "reference_material_movements");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_reference_preparations_consumption",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_purity",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_quantities",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_units",
                table: "reference_preparations");

            migrationBuilder.DropIndex(
                name: "IX_reference_material_movements_preparation_id_source_material~",
                table: "reference_material_movements");

            migrationBuilder.DropIndex(
                name: "ux_reference_material_movements_preparation",
                table: "reference_material_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements");

            migrationBuilder.CreateSequence(
                name: "reference_intermediate_code_sequence");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_material_id",
                table: "reference_preparations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<decimal>(
                name: "available_volume",
                table: "reference_preparations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "diluted_preparation_id",
                table: "reference_preparations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "method_id",
                table: "reference_preparations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "results_json",
                table: "reference_preparations",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "updated_by_user_id",
                table: "reference_preparations",
                type: "integer",
                nullable: true);

            // Stock had no downstream volume movements before this migration.
            migrationBuilder.Sql("UPDATE reference_preparations SET available_volume = final_volume, updated_by_user_id = prepared_by_user_id WHERE kind = 'Stock'; UPDATE reference_preparations p SET method_id = m.id FROM reference_methods m WHERE p.kind = 'Stock' AND p.source_method = m.name;");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_material_id",
                table: "reference_material_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "component_id",
                table: "reference_material_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_preparation_id",
                table: "reference_material_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reference_preparation_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_preparation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    volume_taken = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    volume_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_preparation_components", x => x.id);
                    table.CheckConstraint("ck_reference_components_source", "preparation_id <> source_preparation_id AND source_type IN ('Stock','Intermedia')");
                    table.CheckConstraint("ck_reference_components_volume", "volume_taken > 0 AND volume_unit IN ('mL','L') AND position >= 0");
                    table.ForeignKey(
                        name: "FK_reference_preparation_components_reference_preparations_pre~",
                        column: x => x.preparation_id,
                        principalTable: "reference_preparations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_preparation_components_reference_preparations_sou~",
                        column: x => x.source_preparation_id,
                        principalTable: "reference_preparations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparations_diluted_preparation_id",
                table: "reference_preparations",
                column: "diluted_preparation_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparations_method_id",
                table: "reference_preparations",
                column: "method_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparations_updated_by_user_id",
                table: "reference_preparations",
                column: "updated_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "expiration_date >= preparation_date AND (kind <> 'Stock' OR preparation_date <= source_expiration_date) AND updated_at >= created_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations",
                sql: "kind IN ('Stock','Intermedia') AND status = 'Active' AND (kind <> 'Stock' OR source_material_id IS NOT NULL) AND (kind <> 'Intermedia' OR (source_material_id IS NULL AND method_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_purity",
                table: "reference_preparations",
                sql: "kind <> 'Stock' OR (purity_percent_used > 0 AND purity_percent_used <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_quantities",
                table: "reference_preparations",
                sql: "final_volume > 0 AND available_volume >= 0 AND available_volume <= final_volume AND (kind <> 'Stock' OR (target_concentration > 0 AND actual_concentration > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_units",
                table: "reference_preparations",
                sql: "final_volume_unit IN ('mL','L') AND (kind <> 'Stock' OR (concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug')))");

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_movements_component_id",
                table: "reference_material_movements",
                column: "component_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_movements_source_preparation_id",
                table: "reference_material_movements",
                column: "source_preparation_id");

            migrationBuilder.CreateIndex(
                name: "ux_reference_material_movements_preparation",
                table: "reference_material_movements",
                column: "preparation_id",
                unique: true,
                filter: "kind = 'StockConsumption'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements",
                sql: "(kind = 'StockConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = 'IntermediateConsumption' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparation_components_preparation_id_position",
                table: "reference_preparation_components",
                columns: new[] { "preparation_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparation_components_preparation_id_source_prep~",
                table: "reference_preparation_components",
                columns: new[] { "preparation_id", "source_preparation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_preparation_components_source_preparation_id",
                table: "reference_preparation_components",
                column: "source_preparation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reference_material_movements_reference_preparation_componen~",
                table: "reference_material_movements",
                column: "component_id",
                principalTable: "reference_preparation_components",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_material_movements_reference_preparations_prepara~",
                table: "reference_material_movements",
                column: "preparation_id",
                principalTable: "reference_preparations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_material_movements_reference_preparations_source_~",
                table: "reference_material_movements",
                column: "source_preparation_id",
                principalTable: "reference_preparations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_preparations_reference_methods_method_id",
                table: "reference_preparations",
                column: "method_id",
                principalTable: "reference_methods",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_preparations_reference_preparations_diluted_prepa~",
                table: "reference_preparations",
                column: "diluted_preparation_id",
                principalTable: "reference_preparations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_preparations_usuarios_updated_by_user_id",
                table: "reference_preparations",
                column: "updated_by_user_id",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            // Preserve the Stock ledger's former composite FK guarantee and validate component debits.
            migrationBuilder.Sql("""
                CREATE FUNCTION check_reference_consumption() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE p reference_preparations; c reference_preparation_components; s reference_preparations;
                BEGIN
                    SELECT * INTO STRICT p FROM reference_preparations WHERE id = NEW.preparation_id;
                    IF NEW.actor_user_id <> p.prepared_by_user_id THEN
                        RAISE EXCEPTION 'Consumption actor does not match preparation' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.kind = 'StockConsumption' THEN
                        IF p.kind <> 'Stock' OR NEW.source_material_id IS DISTINCT FROM p.source_material_id
                           OR NEW.quantity <> p.actual_weight OR NEW.unit <> p.source_unit THEN
                            RAISE EXCEPTION 'Stock consumption does not match preparation' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        SELECT * INTO STRICT c FROM reference_preparation_components WHERE id = NEW.component_id;
                        SELECT * INTO STRICT s FROM reference_preparations WHERE id = NEW.source_preparation_id;
                        IF p.kind <> 'Intermedia' OR c.preparation_id <> p.id OR c.source_preparation_id <> s.id
                           OR c.source_type <> s.kind OR NEW.unit <> s.final_volume_unit
                           OR NEW.quantity <> c.volume_taken * (CASE c.volume_unit WHEN 'L' THEN 1000 ELSE 1 END)
                              / (CASE s.final_volume_unit WHEN 'L' THEN 1000 ELSE 1 END) THEN
                            RAISE EXCEPTION 'Component consumption does not match preparation' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $body$;
                CREATE CONSTRAINT TRIGGER ck_reference_consumption_link
                AFTER INSERT OR UPDATE ON reference_material_movements DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_reference_consumption();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER ck_reference_consumption_link ON reference_material_movements; DROP FUNCTION check_reference_consumption();");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_material_movements_reference_preparation_componen~",
                table: "reference_material_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_material_movements_reference_preparations_prepara~",
                table: "reference_material_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_material_movements_reference_preparations_source_~",
                table: "reference_material_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_preparations_reference_methods_method_id",
                table: "reference_preparations");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_preparations_reference_preparations_diluted_prepa~",
                table: "reference_preparations");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_preparations_usuarios_updated_by_user_id",
                table: "reference_preparations");

            migrationBuilder.DropTable(
                name: "reference_preparation_components");

            migrationBuilder.DropIndex(
                name: "IX_reference_preparations_diluted_preparation_id",
                table: "reference_preparations");

            migrationBuilder.DropIndex(
                name: "IX_reference_preparations_method_id",
                table: "reference_preparations");

            migrationBuilder.DropIndex(
                name: "IX_reference_preparations_updated_by_user_id",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_purity",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_quantities",
                table: "reference_preparations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_units",
                table: "reference_preparations");

            migrationBuilder.DropIndex(
                name: "IX_reference_material_movements_component_id",
                table: "reference_material_movements");

            migrationBuilder.DropIndex(
                name: "IX_reference_material_movements_source_preparation_id",
                table: "reference_material_movements");

            migrationBuilder.DropIndex(
                name: "ux_reference_material_movements_preparation",
                table: "reference_material_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements");

            migrationBuilder.DropColumn(
                name: "available_volume",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "diluted_preparation_id",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "method_id",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "results_json",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "updated_by_user_id",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "component_id",
                table: "reference_material_movements");

            migrationBuilder.DropColumn(
                name: "source_preparation_id",
                table: "reference_material_movements");

            migrationBuilder.DropSequence(
                name: "reference_intermediate_code_sequence");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_material_id",
                table: "reference_preparations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "source_material_id",
                table: "reference_material_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_reference_preparations_consumption",
                table: "reference_preparations",
                columns: new[] { "id", "source_material_id", "prepared_by_user_id", "actual_weight", "source_unit" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "expiration_date >= preparation_date AND preparation_date <= source_expiration_date AND updated_at >= created_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_kind_status",
                table: "reference_preparations",
                sql: "kind = 'Stock' AND status = 'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_purity",
                table: "reference_preparations",
                sql: "purity_percent_used > 0 AND purity_percent_used <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_quantities",
                table: "reference_preparations",
                sql: "target_concentration > 0 AND actual_concentration > 0 AND final_volume > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_units",
                table: "reference_preparations",
                sql: "concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND final_volume_unit IN ('mL','L') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug')");

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_movements_preparation_id_source_material~",
                table: "reference_material_movements",
                columns: new[] { "preparation_id", "source_material_id", "actor_user_id", "quantity", "unit" });

            migrationBuilder.CreateIndex(
                name: "ux_reference_material_movements_preparation",
                table: "reference_material_movements",
                column: "preparation_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_material_movements_kind",
                table: "reference_material_movements",
                sql: "kind = 'StockConsumption'");

            migrationBuilder.AddForeignKey(
                name: "FK_reference_material_movements_reference_preparations_prepara~",
                table: "reference_material_movements",
                columns: new[] { "preparation_id", "source_material_id", "actor_user_id", "quantity", "unit" },
                principalTable: "reference_preparations",
                principalColumns: new[] { "id", "source_material_id", "prepared_by_user_id", "actual_weight", "source_unit" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
