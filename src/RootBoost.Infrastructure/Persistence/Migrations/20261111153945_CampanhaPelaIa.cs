using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampanhaPelaIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdCampaignDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreativeJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Objective = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DailyBudgetCents = table.Column<long>(type: "bigint", nullable: false),
                    CountriesCsv = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AgeMin = table.Column<int>(type: "integer", nullable: false),
                    AgeMax = table.Column<int>(type: "integer", nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PrimaryText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Headline = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CallToAction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExternalVideoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalCampaignId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalAdSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalCreativeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalAdId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaignDrafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaignDrafts_BrandKey_CreatedAt",
                table: "AdCampaignDrafts",
                columns: new[] { "BrandKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdCampaignDrafts");
        }
    }
}
