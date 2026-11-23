using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261120120000_AddReportScheduleReview")]
public sealed class AddReportScheduleReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql(ReportScheduleSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Se conserva el historial de digitación y chequeo de horarios.");
}
