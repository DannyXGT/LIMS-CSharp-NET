using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261119180000_AddExecutionReportList")]
public sealed class AddExecutionReportList : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql(ReportListSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Se conserva el historial de marcado por ejecución.");
}
