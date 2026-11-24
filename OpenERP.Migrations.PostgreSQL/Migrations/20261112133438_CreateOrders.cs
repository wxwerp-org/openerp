using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    ErpId = table.Column<string>(type: "text", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    Sid = table.Column<int>(type: "integer", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedRaw = table.Column<string>(type: "text", nullable: true),
                    CreatedSource = table.Column<string>(type: "text", nullable: true),
                    PaidRaw = table.Column<string>(type: "text", nullable: true),
                    PaidSource = table.Column<string>(type: "text", nullable: true),
                    DispatchedRaw = table.Column<string>(type: "text", nullable: true),
                    DispatchedSource = table.Column<string>(type: "text", nullable: true),
                    RefundedRaw = table.Column<string>(type: "text", nullable: true),
                    RefundedSource = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.ErpId);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    ErpId = table.Column<string>(type: "text", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: true),
                    Oguid = table.Column<string>(type: "text", nullable: true),
                    SkuId = table.Column<string>(type: "text", nullable: true),
                    SkuCode = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Disabled = table.Column<bool>(type: "boolean", nullable: true),
                    Appended = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => new { x.ErpId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_ErpId",
                        column: x => x.ErpId,
                        principalTable: "Orders",
                        principalColumn: "ErpId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformOrders",
                columns: table => new
                {
                    ErpId = table.Column<string>(type: "text", nullable: false),
                    PoNumber = table.Column<string>(type: "text", nullable: false),
                    Sid = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<string>(type: "text", nullable: true),
                    StatusText = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformOrders", x => new { x.ErpId, x.PoNumber });
                    table.ForeignKey(
                        name: "FK_PlatformOrders_Orders_ErpId",
                        column: x => x.ErpId,
                        principalTable: "Orders",
                        principalColumn: "ErpId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "PlatformOrders");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
