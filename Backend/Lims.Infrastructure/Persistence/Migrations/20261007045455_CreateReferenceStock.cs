using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF-generated migration metadata arrays.

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateReferenceStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "reference_stock_code_sequence");

            migrationBuilder.CreateTable(
                name: "reference_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_lot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_cas_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_catalog_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_brand = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_method = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_expiration_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_total_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    purity_percent_used = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    source_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_concentration = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_concentration = table.Column<decimal>(type: "numeric(40,28)", precision: 40, scale: 28, nullable: false),
                    concentration_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    final_volume = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    final_volume_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    calculated_weight = table.Column<decimal>(type: "numeric(40,28)", precision: 40, scale: 28, nullable: false),
                    actual_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    formula = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    preparation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: false),
                    storage_temperature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    calculated_weight_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actual_weight_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actual_concentration_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prepared_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_preparations", x => x.id);
                    table.UniqueConstraint("ak_reference_preparations_consumption", x => new { x.id, x.source_material_id, x.prepared_by_user_id, x.actual_weight, x.source_unit });
                    table.CheckConstraint("ck_reference_preparations_dates", "expiration_date >= preparation_date AND preparation_date <= source_expiration_date AND updated_at >= created_at");
                    table.CheckConstraint("ck_reference_preparations_identity", "length(trim(code)) > 0 AND length(trim(name)) > 0 AND length(request_fingerprint) = 64");
                    table.CheckConstraint("ck_reference_preparations_kind_status", "kind = 'Stock' AND status = 'Active'");
                    table.CheckConstraint("ck_reference_preparations_purity", "purity_percent_used > 0 AND purity_percent_used <= 100");
                    table.CheckConstraint("ck_reference_preparations_quantities", "target_concentration > 0 AND actual_concentration > 0 AND final_volume > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight");
                    table.CheckConstraint("ck_reference_preparations_units", "concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND final_volume_unit IN ('mL','L') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug')");
                    table.ForeignKey(
                        name: "FK_reference_preparations_reference_materials_source_material_~",
                        column: x => x.source_material_id,
                        principalTable: "reference_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_preparations_usuarios_prepared_by_user_id",
                        column: x => x.prepared_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reference_material_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    balance_before = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actor_user_id = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_material_movements", x => x.id);
                    table.CheckConstraint("ck_reference_material_movements_balance", "quantity > 0 AND balance_before >= quantity AND balance_after >= 0 AND balance_after = balance_before - quantity");
                    table.CheckConstraint("ck_reference_material_movements_kind", "kind = 'StockConsumption'");
                    table.ForeignKey(
                        name: "FK_reference_material_movements_reference_materials_source_mat~",
                        column: x => x.source_material_id,
                        principalTable: "reference_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_material_movements_reference_preparations_prepara~",
                        columns: x => new { x.preparation_id, x.source_material_id, x.actor_user_id, x.quantity, x.unit },
                        principalTable: "reference_preparations",
                        principalColumns: new[] { "id", "source_material_id", "prepared_by_user_id", "actual_weight", "source_unit" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_material_movements_usuarios_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_movements_actor_user_id",
                table: "reference_material_movements",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_material_movements_preparation_id_source_material~",
                table: "reference_material_movements",
                columns: new[] { "preparation_id", "source_material_id", "actor_user_id", "quantity", "unit" });

            migrationBuilder.CreateIndex(
                name: "ix_reference_material_movements_source_time",
                table: "reference_material_movements",
                columns: new[] { "source_material_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_reference_material_movements_preparation",
                table: "reference_material_movements",
                column: "preparation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reference_preparations_actor",
                table: "reference_preparations",
                column: "prepared_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_preparations_date",
                table: "reference_preparations",
                column: "preparation_date");

            migrationBuilder.CreateIndex(
                name: "ix_reference_preparations_source",
                table: "reference_preparations",
                column: "source_material_id");

            migrationBuilder.CreateIndex(
                name: "ux_reference_preparations_code",
                table: "reference_preparations",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_material_movements");

            migrationBuilder.DropTable(
                name: "reference_preparations");

            migrationBuilder.DropSequence(
                name: "reference_stock_code_sequence");
        }
    }
}
