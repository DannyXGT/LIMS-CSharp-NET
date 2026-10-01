using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Generated migration uses constant arrays for metadata and seed values

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeReferenceMaterialCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM reference_materials) THEN
                        RAISE EXCEPTION 'reference_materials contains rows. This migration does not import or remap operational data.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_method",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "method",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "storage_conditions",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "storage_location",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "reference_materials");

            migrationBuilder.AddColumn<decimal>(
                name: "available_quantity",
                table: "reference_materials",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "location_id",
                table: "reference_materials",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "method_id",
                table: "reference_materials",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_material_id",
                table: "reference_materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "unit_id",
                table: "reference_materials",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "storage_temperature",
                table: "reference_materials",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false);

            migrationBuilder.CreateTable(
                name: "reference_locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_locations", x => x.id);
                    table.CheckConstraint("ck_reference_locations_name", "name = btrim(name) AND name <> ''");
                });

            migrationBuilder.CreateTable(
                name: "reference_methods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_methods", x => x.id);
                    table.CheckConstraint("ck_reference_methods_name", "name = btrim(name) AND name <> ''");
                });

            migrationBuilder.CreateTable(
                name: "reference_units",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_units", x => x.id);
                    table.CheckConstraint("ck_reference_units_name", "name = btrim(name) AND name <> ''");
                    table.CheckConstraint("ck_reference_units_symbol", "symbol = btrim(symbol) AND symbol <> ''");
                });

            migrationBuilder.InsertData(
                table: "reference_locations",
                columns: new[] { "id", "is_active", "name" },
                values: new object[,]
                {
                    { 1, true, "Laboratorio" },
                    { 2, true, "Bodega" }
                });

            migrationBuilder.InsertData(
                table: "reference_methods",
                columns: new[] { "id", "is_active", "name" },
                values: new object[,]
                {
                    { 1, true, "Azodyes" },
                    { 2, true, "APEOs" },
                    { 3, true, "Disperse dyes" },
                    { 4, true, "Phtalatos" },
                    { 5, true, "PCP" },
                    { 6, true, "OPP" },
                    { 7, true, "Organotin" },
                    { 8, true, "PAHs" },
                    { 9, true, "SCCP/MCCP" },
                    { 10, true, "Retardantes de Flama" },
                    { 11, true, "DMFA/DMFU" },
                    { 12, true, "VOC" },
                    { 13, true, "AEEA" },
                    { 14, true, "Bisphenol" },
                    { 15, true, "Halogenated" },
                    { 16, true, "Tiourea" },
                    { 17, true, "PFC" },
                    { 18, true, "COC" },
                    { 19, true, "Cresoles" },
                    { 20, true, "UV" },
                    { 21, true, "Glicoles" },
                    { 22, true, "Metales" },
                    { 23, true, "AMB" }
                });

            migrationBuilder.InsertData(
                table: "reference_units",
                columns: new[] { "id", "is_active", "name", "symbol" },
                values: new object[,]
                {
                    { 1, true, "Mililitro", "mL" },
                    { 2, true, "Gramo", "g" },
                    { 3, true, "Miligramo", "mg" },
                    { 4, true, "Microgramo", "µg" }
                });

            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('reference_methods', 'id'), (SELECT max(id) FROM reference_methods), true);");
            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('reference_units', 'id'), (SELECT max(id) FROM reference_units), true);");
            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('reference_locations', 'id'), (SELECT max(id) FROM reference_locations), true);");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_location_id",
                table: "reference_materials",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_method_id",
                table: "reference_materials",
                column: "method_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_replaced_by",
                table: "reference_materials",
                column: "replaced_by_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_unit_id",
                table: "reference_materials",
                column: "unit_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_reference_locations_active_name",
                table: "reference_locations",
                columns: new[] { "is_active", "name" });

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_reference_locations_name ON reference_locations (lower(name));");

            migrationBuilder.CreateIndex(
                name: "ix_reference_methods_active_name",
                table: "reference_methods",
                columns: new[] { "is_active", "name" });

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_reference_methods_name ON reference_methods (lower(name));");

            migrationBuilder.CreateIndex(
                name: "ix_reference_units_active_name",
                table: "reference_units",
                columns: new[] { "is_active", "name" });

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_reference_units_name ON reference_units (lower(name));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_reference_units_symbol ON reference_units (lower(symbol));");

            migrationBuilder.AddForeignKey(
                name: "FK_reference_materials_reference_locations_location_id",
                table: "reference_materials",
                column: "location_id",
                principalTable: "reference_locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_materials_reference_materials_replaced_by_materia~",
                table: "reference_materials",
                column: "replaced_by_material_id",
                principalTable: "reference_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_materials_reference_methods_method_id",
                table: "reference_materials",
                column: "method_id",
                principalTable: "reference_methods",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reference_materials_reference_units_unit_id",
                table: "reference_materials",
                column: "unit_id",
                principalTable: "reference_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reference_materials_reference_locations_location_id",
                table: "reference_materials");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_materials_reference_materials_replaced_by_materia~",
                table: "reference_materials");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_materials_reference_methods_method_id",
                table: "reference_materials");

            migrationBuilder.DropForeignKey(
                name: "FK_reference_materials_reference_units_unit_id",
                table: "reference_materials");

            migrationBuilder.DropTable(
                name: "reference_locations");

            migrationBuilder.DropTable(
                name: "reference_methods");

            migrationBuilder.DropTable(
                name: "reference_units");

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_location_id",
                table: "reference_materials");

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_method_id",
                table: "reference_materials");

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_replaced_by",
                table: "reference_materials");

            migrationBuilder.DropIndex(
                name: "ix_reference_materials_unit_id",
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

            migrationBuilder.DropColumn(
                name: "available_quantity",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "method_id",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "replaced_by_material_id",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "unit_id",
                table: "reference_materials");

            migrationBuilder.DropColumn(
                name: "storage_temperature",
                table: "reference_materials");

            migrationBuilder.AddColumn<string>(
                name: "method",
                table: "reference_materials",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "storage_conditions",
                table: "reference_materials",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "storage_location",
                table: "reference_materials",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "reference_materials",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_method",
                table: "reference_materials",
                column: "method");
        }
    }
}
