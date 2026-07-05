using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailOutcome",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailProcessedAt",
                table: "Notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MailSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    UseSsl = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ProtectedPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    MinSeverity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfiguredBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailSettings");

            migrationBuilder.DropColumn(
                name: "EmailOutcome",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailProcessedAt",
                table: "Notifications");
        }
    }
}
