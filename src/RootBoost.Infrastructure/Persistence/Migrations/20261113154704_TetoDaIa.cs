using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TetoDaIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiBudgets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MonthlyLimitUsd = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    AlertPercent = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBudgets", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiBudgets");
        }
    }
}
