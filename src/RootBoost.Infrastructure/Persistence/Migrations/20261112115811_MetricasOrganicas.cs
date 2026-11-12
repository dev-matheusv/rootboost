using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetricasOrganicas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganicPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Caption = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                    Permalink = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: false),
                    Likes = table.Column<long>(type: "bigint", nullable: false),
                    Comments = table.Column<long>(type: "bigint", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    Saves = table.Column<long>(type: "bigint", nullable: false),
                    Interactions = table.Column<long>(type: "bigint", nullable: false),
                    AvgWatchMs = table.Column<long>(type: "bigint", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganicPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganicPosts_BrandKey_PostedAt",
                table: "OrganicPosts",
                columns: new[] { "BrandKey", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganicPosts_Channel_ExternalId",
                table: "OrganicPosts",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganicPosts");
        }
    }
}
