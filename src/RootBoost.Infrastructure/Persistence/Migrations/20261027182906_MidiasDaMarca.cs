using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MidiasDaMarca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_BrandKey_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "BrandKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}
