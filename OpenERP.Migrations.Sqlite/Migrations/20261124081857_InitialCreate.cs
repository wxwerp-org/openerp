using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenERP.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErpSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    Singleton = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpSettings", x => x.Id);
                    table.CheckConstraint("CK_ErpSettings_Singleton", "\"Singleton\"");
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ErpId = table.Column<string>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Sid = table.Column<int>(type: "INTEGER", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SyncRevision = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncomeCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    IncomeCurrency = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedRaw = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedSource = table.Column<string>(type: "TEXT", nullable: true),
                    PaidRaw = table.Column<string>(type: "TEXT", nullable: true),
                    PaidSource = table.Column<string>(type: "TEXT", nullable: true),
                    DispatchedRaw = table.Column<string>(type: "TEXT", nullable: true),
                    DispatchedSource = table.Column<string>(type: "TEXT", nullable: true),
                    RefundedRaw = table.Column<string>(type: "TEXT", nullable: true),
                    RefundedSource = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.UniqueConstraint("AK_Orders_ErpId", x => x.ErpId);
                });

            migrationBuilder.CreateTable(
                name: "OrderSyncSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShopSids = table.Column<string>(type: "TEXT", nullable: false),
                    Singleton = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSyncSettings", x => x.Id);
                    table.CheckConstraint("CK_OrderSyncSettings_Singleton", "\"Singleton\"");
                });

            migrationBuilder.CreateTable(
                name: "ShopPlatforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sid = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SearchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MemberId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsTokenOk = table.Column<bool>(type: "INTEGER", nullable: true),
                    TokenError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopSyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    LastStartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastCompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Valid = table.Column<int>(type: "INTEGER", nullable: false),
                    Invalid = table.Column<int>(type: "INTEGER", nullable: false),
                    Unknown = table.Column<int>(type: "INTEGER", nullable: false),
                    Singleton = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopSyncStates", x => x.Id);
                    table.CheckConstraint("CK_ShopSyncStates_Singleton", "\"Singleton\"");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastLoginAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_KeepAdministrator", "\"Username\" <> 'admin' OR \"DeletedAt\" IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ErpId = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: true),
                    Oguid = table.Column<string>(type: "TEXT", nullable: true),
                    SkuId = table.Column<string>(type: "TEXT", nullable: true),
                    SkuCode = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: true),
                    Appended = table.Column<bool>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ErpId = table.Column<string>(type: "TEXT", nullable: false),
                    PoNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Sid = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", nullable: true),
                    StatusText = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformOrders_Orders_ErpId",
                        column: x => x.ErpId,
                        principalTable: "Orders",
                        principalColumn: "ErpId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserShopAssignments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sid = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserShopAssignments", x => new { x.UserId, x.Platform, x.Sid });
                    table.ForeignKey(
                        name: "FK_UserShopAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErpSettings_Singleton",
                table: "ErpSettings",
                column: "Singleton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_ErpId_Ordinal",
                table: "OrderLines",
                columns: new[] { "ErpId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAtTicks_ErpId",
                table: "Orders",
                columns: new[] { "CreatedAtTicks", "ErpId" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSyncSettings_Singleton",
                table: "OrderSyncSettings",
                column: "Singleton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrders_ErpId_PoNumber",
                table: "PlatformOrders",
                columns: new[] { "ErpId", "PoNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopPlatforms_Name",
                table: "ShopPlatforms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Platform_IsTokenOk",
                table: "Shops",
                columns: new[] { "Platform", "IsTokenOk" });

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Sid",
                table: "Shops",
                column: "Sid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopSyncStates_Singleton",
                table: "ShopSyncStates",
                column: "Singleton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "ErpSettings");

            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "OrderSyncSettings");

            migrationBuilder.DropTable(
                name: "PlatformOrders");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "ShopPlatforms");

            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropTable(
                name: "ShopSyncStates");

            migrationBuilder.DropTable(
                name: "UserShopAssignments");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
