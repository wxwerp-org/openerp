using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>订单增加买家、订单金额、物流渠道和平台无关的补充信息；商品行增加物流与补充信息。需重新同步订单才会补全。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261130000000_AddOrderAbstractDetails")]
    public partial class AddOrderAbstractDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Buyer", table: "Orders", type: "text", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "TotalAmount", table: "Orders", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
            migrationBuilder.AddColumn<string>(name: "TotalCurrency", table: "Orders", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Logistics", table: "Orders", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Details", table: "Orders", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Logistics", table: "OrderLines", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Details", table: "OrderLines", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Buyer", table: "Orders");
            migrationBuilder.DropColumn(name: "TotalAmount", table: "Orders");
            migrationBuilder.DropColumn(name: "TotalCurrency", table: "Orders");
            migrationBuilder.DropColumn(name: "Logistics", table: "Orders");
            migrationBuilder.DropColumn(name: "Details", table: "Orders");
            migrationBuilder.DropColumn(name: "Logistics", table: "OrderLines");
            migrationBuilder.DropColumn(name: "Details", table: "OrderLines");
        }
    }
}
