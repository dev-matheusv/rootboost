using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerfilDaMarca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoiceJson",
                table: "Brands",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "CustomerPhrases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CollectedAt = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPhrases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProofItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VerifiedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewBy = table.Column<DateOnly>(type: "date", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProofItems", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "medlycare",
                column: "VoiceJson",
                value: "{}");

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "rootboost-loja",
                column: "VoiceJson",
                value: "{}");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhrases_BrandKey",
                table: "CustomerPhrases",
                column: "BrandKey");

            migrationBuilder.CreateIndex(
                name: "IX_ProofItems_BrandKey_Active",
                table: "ProofItems",
                columns: new[] { "BrandKey", "Active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPhrases");

            migrationBuilder.DropTable(
                name: "ProofItems");

            migrationBuilder.DropColumn(
                name: "VoiceJson",
                table: "Brands");
        }
    }
}
