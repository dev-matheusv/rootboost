using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PautaDeConteudos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoVoice",
                table: "CreativeJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ContentIdeas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Request = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Group = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AutoVoice = table.Column<bool>(type: "boolean", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentIdeas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentIdeas_BrandKey_Order",
                table: "ContentIdeas",
                columns: new[] { "BrandKey", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentIdeas_Status",
                table: "ContentIdeas",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentIdeas");

            migrationBuilder.DropColumn(
                name: "AutoVoice",
                table: "CreativeJobs");
        }
    }
}
