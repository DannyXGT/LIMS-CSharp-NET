using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261116190000_AddPlanningWorkspace")]
public sealed class AddPlanningWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(PlanningSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("La historia de Planificación no se elimina automáticamente. Restaure un respaldo autorizado.");
}
