using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECARMF.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentInteractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PackageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PackageVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Provenance = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AskedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FeedbackUseful = table.Column<bool>(type: "bit", nullable: true),
                    FeedbackBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FeedbackAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInteractions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentInteractions_TenantId_AgentId_AskedAt",
                table: "AgentInteractions",
                columns: new[] { "TenantId", "AgentId", "AskedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentInteractions");
        }
    }
}
