using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConhecimentoDoSistema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScreenKnowledge",
                table: "BrandProducts",
                type: "character varying(24000)",
                maxLength: 24000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScreenKnowledgeAt",
                table: "BrandProducts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreenKnowledge",
                table: "BrandProducts");

            migrationBuilder.DropColumn(
                name: "ScreenKnowledgeAt",
                table: "BrandProducts");
        }
    }
}
