using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcervoComDescricao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "MediaAssets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "MediaAssets");
        }
    }
}
