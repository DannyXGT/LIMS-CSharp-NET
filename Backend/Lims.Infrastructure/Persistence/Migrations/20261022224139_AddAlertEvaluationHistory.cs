using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertEvaluationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    Evaluated = table.Column<int>(type: "integer", nullable: false),
                    Changed = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<int>(type: "integer", nullable: false),
                    Sent = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_evaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_evaluations_StartedAt",
                table: "alert_evaluations",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_evaluations");
        }
    }
}
