using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>平台凭据改为按授权店铺目录里的店铺编号保存，取代独立录入的 PlatformShops。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261127000000_PlatformCredentials")]
    public partial class PlatformCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformShops");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformCredentials");

            migrationBuilder.CreateTable(
                name: "PlatformShops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AppKey = table.Column<string>(type: "text", nullable: false),
                    ProtectedAppSecret = table.Column<string>(type: "text", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformShops", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformShops_Sid",
                table: "PlatformShops",
                column: "Sid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformShops_Platform_ExternalId",
                table: "PlatformShops",
                columns: new[] { "Platform", "ExternalId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }
    }
}
