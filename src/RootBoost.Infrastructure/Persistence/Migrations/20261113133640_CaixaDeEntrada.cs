using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaixaDeEntrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContactId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FromBusiness = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HasAttachment = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_BrandKey_ConversationId",
                table: "InboxMessages",
                columns: new[] { "BrandKey", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_BrandKey_SentAt",
                table: "InboxMessages",
                columns: new[] { "BrandKey", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Channel_ExternalId",
                table: "InboxMessages",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");
        }
    }
}
