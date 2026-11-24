using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortableReadFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "Shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtTicks",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAtTicks_ErpId",
                table: "Orders",
                columns: new[] { "CreatedAtTicks", "ErpId" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAtTicks_ErpId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "CreatedAtTicks",
                table: "Orders");
        }
    }
}
