using Lims.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LimsDbContext))]
[Migration("20261119120000_SeparatePlanningIdentity")]
public sealed class SeparatePlanningIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(PlanningIdentitySchema.Sql);
        migrationBuilder.Sql(PlanningIdentitySchema.AccessSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Las identidades y la auditoría de Planificación se conservan.");
}
