using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>订单商品行保存商品名称、图片、规格与单价，订单列表据此展示商品信息；需重新同步订单才会补全。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261129000000_AddOrderLineProductInfo")]
    public partial class AddOrderLineProductInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Name", table: "OrderLines", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ImageUrl", table: "OrderLines", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Specs", table: "OrderLines", type: "text", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "UnitPrice", table: "OrderLines", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Currency", table: "OrderLines", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Name", table: "OrderLines");
            migrationBuilder.DropColumn(name: "ImageUrl", table: "OrderLines");
            migrationBuilder.DropColumn(name: "Specs", table: "OrderLines");
            migrationBuilder.DropColumn(name: "UnitPrice", table: "OrderLines");
            migrationBuilder.DropColumn(name: "Currency", table: "OrderLines");
        }
    }
}
