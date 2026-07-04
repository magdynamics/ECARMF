using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ReceivedAt",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_TransactionOutcomes_TransactionId",
                table: "TransactionOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgePackages_PackageId_PackageVersion",
                table: "KnowledgePackages");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Transactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TransactionOutcomes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "KnowledgePackages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Rows created before multi-tenancy belong to the 'default' tenant.
            migrationBuilder.Sql("UPDATE [Transactions] SET [TenantId] = N'default' WHERE [TenantId] = N''");
            migrationBuilder.Sql("UPDATE [TransactionOutcomes] SET [TenantId] = N'default' WHERE [TenantId] = N''");
            migrationBuilder.Sql("UPDATE [KnowledgePackages] SET [TenantId] = N'default' WHERE [TenantId] = N''");
            migrationBuilder.Sql("UPDATE [AuditEntries] SET [TenantId] = N'default' WHERE [TenantId] = N''");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_ReceivedAt",
                table: "Transactions",
                columns: new[] { "TenantId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionOutcomes_TenantId_TransactionId",
                table: "TransactionOutcomes",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgePackages_TenantId_PackageId_PackageVersion",
                table: "KnowledgePackages",
                columns: new[] { "TenantId", "PackageId", "PackageVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_CorrelationId",
                table: "AuditEntries",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TenantId_ReceivedAt",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_TransactionOutcomes_TenantId_TransactionId",
                table: "TransactionOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgePackages_TenantId_PackageId_PackageVersion",
                table: "KnowledgePackages");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TransactionOutcomes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "KnowledgePackages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReceivedAt",
                table: "Transactions",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionOutcomes_TransactionId",
                table: "TransactionOutcomes",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgePackages_PackageId_PackageVersion",
                table: "KnowledgePackages",
                columns: new[] { "PackageId", "PackageVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt");
        }
    }
}
