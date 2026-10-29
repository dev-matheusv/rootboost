using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarcaDeDemonstracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Key", "CreatedAt", "DefaultLanguage", "FictionalMarkers", "Name", "VoiceJson" },
                values: new object[] { "vitta-demo", new DateTimeOffset(new DateTime(2026, 10, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "pt-BR", null, "Clínica Vitta Demo", "{}" });

            migrationBuilder.Sql("""
                UPDATE "Brands" SET "FictionalMarkers" = 'Clínica Vitta Demonstração, Clínica Vitta Demo, Clínica Modelo, Matheus Demonstração, CPF 999.000.00x'
                WHERE "Key" = 'vitta-demo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "vitta-demo");
        }
    }
}
