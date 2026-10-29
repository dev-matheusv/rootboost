using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VozesDeNarracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NarrationVoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReferenceStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReferenceText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Instruct = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConsentBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ConsentText = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SamplesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ChosenSeed = table.Column<int>(type: "integer", nullable: true),
                    WorkerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NarrationVoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NarrationVoices_BrandKey_CreatedAt",
                table: "NarrationVoices",
                columns: new[] { "BrandKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NarrationVoices_Key",
                table: "NarrationVoices",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NarrationVoices_Status_CreatedAt",
                table: "NarrationVoices",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NarrationVoices");
        }
    }
}
