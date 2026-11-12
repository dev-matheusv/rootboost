using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MapaDoSistema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemMap",
                table: "BrandProducts",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SystemMapAt",
                table: "BrandProducts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemMapHash",
                table: "BrandProducts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemMap",
                table: "BrandProducts");

            migrationBuilder.DropColumn(
                name: "SystemMapAt",
                table: "BrandProducts");

            migrationBuilder.DropColumn(
                name: "SystemMapHash",
                table: "BrandProducts");
        }
    }
}
