using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScoresRiskIndexAndAuditArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RiskType",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "AuditArchive",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditArchive", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_TenantId_RiskType_ComputedAt",
                table: "Scores",
                columns: new[] { "TenantId", "RiskType", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditArchive_TenantId_OccurredAt",
                table: "AuditArchive",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditArchive");

            migrationBuilder.DropIndex(
                name: "IX_Scores_TenantId_RiskType_ComputedAt",
                table: "Scores");

            migrationBuilder.AlterColumn<string>(
                name: "RiskType",
                table: "Scores",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
