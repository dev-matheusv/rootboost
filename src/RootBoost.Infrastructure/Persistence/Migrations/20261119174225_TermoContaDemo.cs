using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TermoContaDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DemoFictionalConfirmedAt",
                table: "Brands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemoFictionalConfirmedBy",
                table: "Brands",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "medlycare",
                columns: new[] { "DemoFictionalConfirmedAt", "DemoFictionalConfirmedBy" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "rootboost-loja",
                columns: new[] { "DemoFictionalConfirmedAt", "DemoFictionalConfirmedBy" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "vitta-demo",
                columns: new[] { "DemoFictionalConfirmedAt", "DemoFictionalConfirmedBy" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DemoFictionalConfirmedAt",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "DemoFictionalConfirmedBy",
                table: "Brands");
        }
    }
}
