using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CasesAndCaseId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseId",
                table: "Transactions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SkillsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_CaseId",
                table: "Transactions",
                columns: new[] { "TenantId", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_TenantId_CaseId",
                table: "Cases",
                columns: new[] { "TenantId", "CaseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_TenantId_CaseId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "Transactions");
        }
    }
}
