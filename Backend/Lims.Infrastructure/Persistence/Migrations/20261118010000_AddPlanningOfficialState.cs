using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261118010000_AddPlanningOfficialState")]
public sealed class AddPlanningOfficialState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql(PlanningOfficialSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("La procedencia oficial y decisiones auditadas no se eliminan automáticamente.");
}
