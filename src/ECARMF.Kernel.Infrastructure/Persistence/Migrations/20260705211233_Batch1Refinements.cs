using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Batch1Refinements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R2 (CapitalFlow): a RENAME, never drop/create — the executed
            // sweeps in Allocations are audit-relevant history.
            migrationBuilder.RenameTable(
                name: "Allocations",
                newName: "CapitalFlows");

            migrationBuilder.RenameIndex(
                name: "IX_Allocations_TenantId_CreatedAt",
                table: "CapitalFlows",
                newName: "IX_CapitalFlows_TenantId_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "RecommendedAmount",
                table: "CapitalFlows",
                newName: "Amount");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "CapitalFlows",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Outbound");

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "CapitalFlows",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MilestoneReference",
                table: "CapitalFlows",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "SourceCategory",
                table: "Connectors",
                newName: "DomainTag");

            migrationBuilder.RenameColumn(
                name: "IngestionMode",
                table: "Connectors",
                newName: "ArrivalMode");

            migrationBuilder.AddColumn<string>(
                name: "SensitivityTier",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Renewals",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "SubjectId",
                table: "Renewals",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "Renewals",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleState",
                table: "OrgUnits",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Operating");

            // R1 backfill: Universal Dental's renewals recorded their subject
            // in a notes convention ("Entity unit: X" / "Property unit: X");
            // lift that into the new polymorphic subject columns.
            migrationBuilder.Sql(@"
UPDATE Renewals SET
    SubjectType = 'OrganizationalUnit',
    SubjectId = LTRIM(RTRIM(
        CASE WHEN CHARINDEX(';', Notes) > 0
             THEN SUBSTRING(Notes, CHARINDEX(':', Notes) + 1, CHARINDEX(';', Notes) - CHARINDEX(':', Notes) - 1)
             ELSE SUBSTRING(Notes, CHARINDEX(':', Notes) + 1, LEN(Notes))
        END))
WHERE Notes LIKE 'Entity unit:%' OR Notes LIKE 'Property unit:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Direction", table: "CapitalFlows");
            migrationBuilder.DropColumn(name: "SourceId", table: "CapitalFlows");
            migrationBuilder.DropColumn(name: "MilestoneReference", table: "CapitalFlows");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "CapitalFlows",
                newName: "RecommendedAmount");

            migrationBuilder.RenameIndex(
                name: "IX_CapitalFlows_TenantId_CreatedAt",
                table: "CapitalFlows",
                newName: "IX_Allocations_TenantId_CreatedAt");

            migrationBuilder.RenameTable(
                name: "CapitalFlows",
                newName: "Allocations");

            migrationBuilder.DropColumn(
                name: "SensitivityTier",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "Renewals");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "Renewals");

            migrationBuilder.DropColumn(
                name: "LifecycleState",
                table: "OrgUnits");

            migrationBuilder.RenameColumn(
                name: "DomainTag",
                table: "Connectors",
                newName: "SourceCategory");

            migrationBuilder.RenameColumn(
                name: "ArrivalMode",
                table: "Connectors",
                newName: "IngestionMode");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Renewals",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

        }
    }
}
