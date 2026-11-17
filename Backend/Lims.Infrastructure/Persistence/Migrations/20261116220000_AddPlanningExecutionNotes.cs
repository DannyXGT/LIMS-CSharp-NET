using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261116220000_AddPlanningExecutionNotes")]
public sealed class AddPlanningExecutionNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql(PlanningNotesSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Las observaciones auditadas no se eliminan automáticamente.");
}
