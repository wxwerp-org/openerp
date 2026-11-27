using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>订单同步通过旺销王平台接口按店铺 Sid 调用，平台授权由旺销王托管，不再在本地保存平台凭据。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261128000000_DropPlatformCredentials")]
    public partial class DropPlatformCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformCredentials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sid = table.Column<int>(type: "integer", nullable: false),
                    AppKey = table.Column<string>(type: "text", nullable: false),
                    ProtectedAppSecret = table.Column<string>(type: "text", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformCredentials_Sid",
                table: "PlatformCredentials",
                column: "Sid",
                unique: true);
        }
    }
}
