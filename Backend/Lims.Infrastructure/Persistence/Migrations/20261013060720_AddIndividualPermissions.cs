using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration arrays are executed once per migration.

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndividualPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permission_catalog",
                columns: table => new
                {
                    permission_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_catalog", x => x.permission_code);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_sets",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_sets", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_permission_sets_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    actor_user_id = table.Column<int>(type: "integer", nullable: false),
                    before_granted = table.Column<bool>(type: "boolean", nullable: false),
                    after_granted = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_audit", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_permission_audit_permission_catalog_permission_code",
                        column: x => x.permission_code,
                        principalTable: "permission_catalog",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_audit_usuarios_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_audit_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_assignments",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_granted = table.Column<bool>(type: "boolean", nullable: false),
                    actor_user_id = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_assignments", x => new { x.user_id, x.permission_code });
                    table.ForeignKey(
                        name: "FK_user_permission_assignments_permission_catalog_permission_c~",
                        column: x => x.permission_code,
                        principalTable: "permission_catalog",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_assignments_user_permission_sets_user_id",
                        column: x => x.user_id,
                        principalTable: "user_permission_sets",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_assignments_usuarios_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permission_catalog",
                column: "permission_code",
                values: new object[]
                {
                    "intermedia.create",
                    "intermedia.view",
                    "reference_materials.archive",
                    "reference_materials.create",
                    "reference_materials.edit",
                    "reference_materials.replace",
                    "reference_materials.view",
                    "stock.create",
                    "stock.view"
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_assignments_actor_user_id",
                table: "user_permission_assignments",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_assignments_permission_code",
                table: "user_permission_assignments",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_audit_actor_user_id",
                table: "user_permission_audit",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_audit_permission_code",
                table: "user_permission_audit",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_audit_user_id_changed_at",
                table: "user_permission_audit",
                columns: new[] { "user_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_permission_assignments");

            migrationBuilder.DropTable(
                name: "user_permission_audit");

            migrationBuilder.DropTable(
                name: "user_permission_sets");

            migrationBuilder.DropTable(
                name: "permission_catalog");
        }
    }
}
