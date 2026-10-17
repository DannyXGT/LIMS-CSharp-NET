using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockSolventComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "composition_basis",
                table: "reference_preparations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compound",
                table: "reference_preparations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reference_solvents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_solvents", x => x.id);
                    table.CheckConstraint("ck_reference_solvents_identity", "length(trim(name)) > 0 AND length(trim(abbreviation)) > 0 AND display_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_solvents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solvent_id = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_solvents", x => x.id);
                    table.CheckConstraint("ck_stock_solvents_percent", "percent > 0 AND percent <= 100 AND position >= 0");
                    table.ForeignKey(
                        name: "FK_stock_solvents_reference_preparations_preparation_id",
                        column: x => x.preparation_id,
                        principalTable: "reference_preparations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_solvents_reference_solvents_solvent_id",
                        column: x => x.solvent_id,
                        principalTable: "reference_solvents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "reference_solvents",
                columns: [ "id", "abbreviation", "display_order", "is_active", "name" ],
                values: new object[,]
                {
                    { 1, "THF", 0, true, "Tetrahidrofurano" },
                    { 2, "Hxn", 1, true, "Hexano" },
                    { 3, "ACN", 2, true, "Acetonitrilo" },
                    { 4, "MeOH", 3, true, "Metanol" },
                    { 5, "Tolueno", 4, true, "Tolueno" },
                    { 6, "MTBE", 5, true, "Éter metil tert-butílico" },
                    { 7, "Cyclohexano", 6, true, "Ciclohexano" },
                    { 8, "H2O", 7, true, "Agua" },
                    { 9, "HNO3", 8, true, "Ácido nítrico" },
                    { 10, "HCl", 9, true, "Ácido clorhídrico" },
                    { 11, "Acetona", 10, true, "Acetona" },
                    { 12, "DMSO", 11, true, "Dimetilsulfóxido" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_solvents_abbreviation",
                table: "reference_solvents",
                column: "abbreviation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_solvents_preparation_id_position",
                table: "stock_solvents",
                columns: [ "preparation_id", "position" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_solvents_preparation_id_solvent_id",
                table: "stock_solvents",
                columns: [ "preparation_id", "solvent_id" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_solvents_solvent_id",
                table: "stock_solvents",
                column: "solvent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_solvents");

            migrationBuilder.DropTable(
                name: "reference_solvents");

            migrationBuilder.DropColumn(
                name: "composition_basis",
                table: "reference_preparations");

            migrationBuilder.DropColumn(
                name: "compound",
                table: "reference_preparations");
        }
    }
}
