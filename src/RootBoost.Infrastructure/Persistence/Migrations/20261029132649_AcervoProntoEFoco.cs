using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcervoProntoEFoco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Finished",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FocusJson",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Reels que o estúdio já entregou (nome "-narrado") são vídeos prontos: saem dos trechos.
            migrationBuilder.Sql("""
                UPDATE "MediaAssets" SET "Finished" = TRUE WHERE "ContentType" LIKE 'video/%' AND "FileName" LIKE '%-narrado%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Finished",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "FocusJson",
                table: "MediaAssets");
        }
    }
}
