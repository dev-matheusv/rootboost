using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RootBoost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PedidoDeTeste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "Orders");
        }
    }
}
