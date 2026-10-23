using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandingViews",
                columns: table => new
                {
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Lang = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DateUtc = table.Column<DateTime>(type: "date", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingViews", x => new { x.ProductKey, x.Lang, x.DateUtc });
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ShipName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShipLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ShipCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShipState = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShipZip = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShipCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ShipPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ShipLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AmountPaid = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SupplierOrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.PaymentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SupplierOrderId",
                table: "Orders",
                column: "SupplierOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandingViews");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
