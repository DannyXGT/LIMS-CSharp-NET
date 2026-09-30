using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateReferenceMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cas_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    catalog_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    method = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    purity_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    lot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    brand = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: false),
                    presentation_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    package_count = table.Column<int>(type: "integer", nullable: false),
                    storage_conditions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_materials", x => x.id);
                    table.CheckConstraint("ck_reference_materials_dates", "expiration_date >= received_date");
                    table.CheckConstraint("ck_reference_materials_package_count", "package_count > 0");
                    table.CheckConstraint("ck_reference_materials_presentation_quantity", "presentation_quantity > 0");
                    table.CheckConstraint("ck_reference_materials_purity", "purity_percent > 0 AND purity_percent <= 100");
                    table.ForeignKey(
                        name: "FK_reference_materials_usuarios_archived_by_user_id",
                        column: x => x.archived_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_materials_usuarios_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_materials_usuarios_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_materials_archived_by_user_id",
                table: "reference_materials",
                column: "archived_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_cas_catalog",
                table: "reference_materials",
#pragma warning disable CA1861 // EF Core migration scaffolding requires an inline column array.
                columns: new[] { "cas_number", "catalog_number" });
#pragma warning restore CA1861

            migrationBuilder.CreateIndex(
                name: "IX_reference_materials_created_by_user_id",
                table: "reference_materials",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_expiration_date",
                table: "reference_materials",
                column: "expiration_date");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_method",
                table: "reference_materials",
                column: "method");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_name",
                table: "reference_materials",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_reference_materials_status",
                table: "reference_materials",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_reference_materials_updated_by_user_id",
                table: "reference_materials",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_materials");
        }
    }
}
