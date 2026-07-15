using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RiskTreatments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiskTreatments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RiskKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InherentSeverity = table.Column<int>(type: "int", nullable: false),
                    InherentLikelihood = table.Column<int>(type: "int", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Strategy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MitigationPlan = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResidualSeverity = table.Column<int>(type: "int", nullable: true),
                    ResidualLikelihood = table.Column<int>(type: "int", nullable: true),
                    TargetDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkedActionRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskTreatments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskTreatments_TenantId_RiskKey",
                table: "RiskTreatments",
                columns: new[] { "TenantId", "RiskKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskTreatments");
        }
    }
}
