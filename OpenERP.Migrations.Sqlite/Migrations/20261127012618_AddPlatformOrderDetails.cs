using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddPlatformOrderDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Buyer",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Logistics",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "Orders",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotalCurrency",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Logistics",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specs",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "OrderLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "ErpSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Buyer",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Logistics",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TotalCurrency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Logistics",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Specs",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "ErpSettings");
        }
    }
}
