using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialSolventComposition : Migration
    {
        private static readonly string[] MaterialPosition = ["material_id", "position"];
        private static readonly string[] MaterialSolvent = ["material_id", "solvent_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "preparation_id",
                table: "stock_solvents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "material_id",
                table: "stock_solvents",
                type: "uuid",
                nullable: true);



            migrationBuilder.CreateIndex(
                name: "IX_stock_solvents_material_id_position",
                table: "stock_solvents",
                columns: MaterialPosition,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_solvents_material_id_solvent_id",
                table: "stock_solvents",
                columns: MaterialSolvent,
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_solvents_owner",
                table: "stock_solvents",
                sql: "(preparation_id IS NOT NULL AND material_id IS NULL) OR (preparation_id IS NULL AND material_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_solvents_reference_materials_material_id",
                table: "stock_solvents",
                column: "material_id",
                principalTable: "reference_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
DO $catalog$
DECLARE
    item record;
    catalog_id integer;
BEGIN
    IF to_regclass('reference_solvents') IS NULL THEN
        RAISE EXCEPTION 'Falta reference_solvents. Aplique primero AddStockSolventComposition; no se creará un catálogo alternativo.';
    END IF;
    LOCK TABLE reference_solvents IN SHARE ROW EXCLUSIVE MODE;
    FOR item IN SELECT * FROM (VALUES
        ('Tetrahidrofurano', 'THF', 0), ('Hexano', 'Hxn', 1),
        ('Acetonitrilo', 'ACN', 2), ('Metanol', 'MeOH', 3), ('Tolueno', 'Tolueno', 4),
        ('Éter metil tert-butílico', 'MTBE', 5), ('Ciclohexano', 'Ciclohexano', 6),
        ('Agua', 'H2O', 7), ('Ácido nítrico', 'HNO3', 8), ('Ácido clorhídrico', 'HCl', 9),
        ('Acetona', 'Acetona', 10), ('Dimetilsulfóxido', 'DMSO', 11)
    ) AS expected(name, abbreviation, display_order)
    LOOP
        SELECT id INTO catalog_id FROM reference_solvents
        WHERE lower(trim(abbreviation)) = lower(item.abbreviation)
           OR lower(trim(name)) = lower(item.name)
           OR (item.abbreviation = 'Ciclohexano' AND lower(trim(abbreviation)) = 'cyclohexano')
        ORDER BY (lower(trim(abbreviation)) = lower(item.abbreviation)) DESC, id
        LIMIT 1;
        IF catalog_id IS NULL THEN
            SELECT coalesce(max(id), 0) + 1 INTO catalog_id FROM reference_solvents;
            INSERT INTO reference_solvents(id, name, abbreviation, is_active, display_order)
            VALUES(catalog_id, item.name, item.abbreviation, true, item.display_order);
        ELSE
            UPDATE reference_solvents SET name = item.name, abbreviation = item.abbreviation,
                is_active = true, display_order = item.display_order WHERE id = catalog_id;
        END IF;
    END LOOP;
END $catalog$;
""");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM stock_solvents WHERE material_id IS NOT NULL) THEN RAISE EXCEPTION 'La reversión eliminaría composiciones comerciales; operación cancelada.'; END IF; END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_solvents_reference_materials_material_id",
                table: "stock_solvents");

            migrationBuilder.DropIndex(
                name: "IX_stock_solvents_material_id_position",
                table: "stock_solvents");

            migrationBuilder.DropIndex(
                name: "IX_stock_solvents_material_id_solvent_id",
                table: "stock_solvents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_solvents_owner",
                table: "stock_solvents");

            migrationBuilder.DropColumn(
                name: "material_id",
                table: "stock_solvents");

            migrationBuilder.AlterColumn<Guid>(
                name: "preparation_id",
                table: "stock_solvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);


        }
    }
}
