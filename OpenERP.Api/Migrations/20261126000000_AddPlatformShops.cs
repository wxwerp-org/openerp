using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenERP.Api.Migrations
{
    /// <summary>直连平台 API 的店铺与加密凭据。</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261126000000_AddPlatformShops")]
    public partial class AddPlatformShops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformShops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformShops");
        }
    }
}
