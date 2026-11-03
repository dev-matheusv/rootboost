using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MusicaDoReels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MusicAudioId",
                table: "Publications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicTitle",
                table: "Publications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MusicVolume",
                table: "Publications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoVolume",
                table: "Publications",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicAudioId",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "MusicTitle",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "MusicVolume",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "VideoVolume",
                table: "Publications");
        }
    }
}
