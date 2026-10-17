using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialPreparationRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_commercial_roles",
                columns: table => new
                {
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    registered_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_commercial_roles", x => new { x.material_id, x.kind });
                    table.CheckConstraint("ck_commercial_role_kind", "kind IN ('Stock','Intermedia')");
                    table.ForeignKey(
                        name: "FK_reference_commercial_roles_reference_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "reference_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reference_commercial_roles_usuarios_registered_by_user_id",
                        column: x => x.registered_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_commercial_roles_code",
                table: "reference_commercial_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_commercial_roles_registered_by_user_id",
                table: "reference_commercial_roles",
                column: "registered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reference_commercial_roles_request_id",
                table: "reference_commercial_roles",
                column: "request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_commercial_roles");
        }
    }
}
