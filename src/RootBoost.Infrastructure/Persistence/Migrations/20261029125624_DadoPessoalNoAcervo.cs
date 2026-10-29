using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DadoPessoalNoAcervo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonalData",
                table: "MediaAssets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unchecked");

            migrationBuilder.AddColumn<string>(
                name: "PersonalDataKinds",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedactedStorageKey",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedactionBoxesJson",
                table: "MediaAssets",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "FictionalMarkers",
                table: "Brands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Privacy",
                table: "Brands",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Blur");

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "medlycare",
                column: "FictionalMarkers",
                value: null);

            migrationBuilder.UpdateData(
                table: "Brands",
                keyColumn: "Key",
                keyValue: "rootboost-loja",
                column: "FictionalMarkers",
                value: null);

            // MedlyCare (pedido do Matheus, 29/10): dado real de paciente não entra em peça nenhuma, nem borrado.
            migrationBuilder.Sql("""
                UPDATE "Brands" SET "Privacy" = 'FictionalOnly',
                  "FictionalMarkers" = 'Clínica Vitta Demonstração, Clínica Modelo, Matheus Demonstração, CPF 999.000.00x'
                WHERE "Key" = 'medlycare';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonalData",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "PersonalDataKinds",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "RedactedStorageKey",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "RedactionBoxesJson",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "FictionalMarkers",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "Privacy",
                table: "Brands");
        }
    }
}
