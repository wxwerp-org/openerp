using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>ERP 接入增加独立部署地址；为空表示使用公共版。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261125000000_AddErpBaseUrl")]
    public partial class AddErpBaseUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "ErpSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "ErpSettings");
        }
    }
}
