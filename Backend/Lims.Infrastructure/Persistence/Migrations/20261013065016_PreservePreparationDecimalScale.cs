using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreservePreparationDecimalScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // trim_scale removes only trailing zeroes, without rounding any result or
            // changing measured weights, balances, components or ledger movements.
            migrationBuilder.Sql("""
                DO $decimal_guard$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM reference_preparations p
                        CROSS JOIN LATERAL (VALUES (p.calculated_weight), (p.actual_concentration)) v(value)
                        WHERE v.value::text IN ('NaN', 'Infinity', '-Infinity')
                           OR scale(trim_scale(v.value)) > 28
                           OR abs(trim_scale(v.value)) * power(10::numeric, scale(trim_scale(v.value)))
                              > 79228162514264337593543950335::numeric
                    ) THEN
                        RAISE EXCEPTION 'Preparaciones contiene un valor no representable como Decimal. Revise los datos antes de aplicar esta migración.'
                            USING ERRCODE = '22003';
                    END IF;
                END $decimal_guard$;

                ALTER TABLE reference_preparations
                    ALTER COLUMN calculated_weight TYPE numeric USING trim_scale(calculated_weight),
                    ALTER COLUMN actual_concentration TYPE numeric USING trim_scale(actual_concentration);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "calculated_weight",
                table: "reference_preparations",
                type: "numeric(40,28)",
                precision: 40,
                scale: 28,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "actual_concentration",
                table: "reference_preparations",
                type: "numeric(40,28)",
                precision: 40,
                scale: 28,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }
    }
}
