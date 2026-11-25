using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261124150000_AddReportClosureFollowUp")]
public sealed class AddReportClosureFollowUp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql(ReportClosureSchema.Sql);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Se conserva el seguimiento e historial de cierres.");
}
