using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowHistoricalStockDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "(kind = 'Stock' OR expiration_date >= preparation_date) AND updated_at >= created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reference_preparations_dates",
                table: "reference_preparations",
                sql: "expiration_date >= preparation_date AND (kind <> 'Stock' OR preparation_date <= source_expiration_date) AND updated_at >= created_at");
        }
    }
}
