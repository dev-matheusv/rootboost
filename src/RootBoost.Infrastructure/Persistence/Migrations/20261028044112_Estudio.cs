using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Estudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreativeJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Theme = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VoiceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Script = table.Column<string>(type: "jsonb", nullable: true),
                    Lines = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: true),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OutputsJson = table.Column<string>(type: "jsonb", nullable: false),
                    WorkerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ProgressNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativeJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeJobs_BrandKey_CreatedAt",
                table: "CreativeJobs",
                columns: new[] { "BrandKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeJobs_Status_CreatedAt",
                table: "CreativeJobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreativeJobs");
        }
    }
}
