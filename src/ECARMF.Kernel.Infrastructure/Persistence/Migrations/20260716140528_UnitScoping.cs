using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitRef",
                table: "Transactions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitId",
                table: "Integrations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_UnitRef",
                table: "Transactions",
                columns: new[] { "TenantId", "UnitRef" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TenantId_UnitRef",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UnitRef",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Integrations");
        }
    }
}
