using Microsoft.EntityFrameworkCore.Migrations;

namespace OpenERP.Api.Migrations;

public partial class AdoptUuidEntityKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "RefreshTokens" DROP CONSTRAINT "FK_RefreshTokens_AdminAccounts_AdminId";
            ALTER TABLE "AdminAccounts" RENAME TO "Users";
            ALTER TABLE "Users"
                DROP CONSTRAINT "CK_AdminAccounts_Singleton",
                DROP CONSTRAINT "PK_AdminAccounts";
            ALTER TABLE "Users" RENAME COLUMN "Id" TO "LegacyId";
            ALTER TABLE "Users"
                ADD COLUMN "Id" uuid NOT NULL DEFAULT uuidv7(),
                ADD COLUMN "Username" character varying(20) NOT NULL DEFAULT 'admin',
                ADD COLUMN "CreatedAt" timestamp with time zone,
                ADD COLUMN "DeletedAt" timestamp with time zone;
            ALTER TABLE "Users"
                ALTER COLUMN "Id" DROP DEFAULT,
                ALTER COLUMN "Username" DROP DEFAULT,
                ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
                ADD CONSTRAINT "CK_Users_KeepAdministrator" CHECK ("Username" <> 'admin' OR "DeletedAt" IS NULL);
            CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username") WHERE "DeletedAt" IS NULL;

            ALTER TABLE "RefreshTokens"
                ADD COLUMN "UserId" uuid,
                ADD COLUMN "DeletedAt" timestamp with time zone;
            UPDATE "RefreshTokens" AS token
                SET "UserId" = account."Id"
                FROM "Users" AS account
                WHERE token."AdminId" = account."LegacyId";
            UPDATE "RefreshTokens" SET "Id" = uuidv7() WHERE uuid_extract_version("Id") IS DISTINCT FROM 7;
            ALTER TABLE "RefreshTokens"
                DROP COLUMN "AdminId",
                ALTER COLUMN "UserId" SET NOT NULL,
                ADD CONSTRAINT "FK_RefreshTokens_Users_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
            CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
            ALTER TABLE "Users" DROP COLUMN "LegacyId";
            """);

        migrationBuilder.DropForeignKey("FK_OrderLines_Orders_ErpId", "OrderLines");
        migrationBuilder.DropForeignKey("FK_PlatformOrders_Orders_ErpId", "PlatformOrders");
        foreach (var table in new[] { "Orders", "OrderLines", "PlatformOrders", "Shops", "ShopPlatforms" })
            MigratePrimaryKey(migrationBuilder, table);

        migrationBuilder.AddUniqueConstraint("AK_Orders_ErpId", "Orders", "ErpId");
        migrationBuilder.CreateIndex("IX_OrderLines_ErpId_Ordinal", "OrderLines", ["ErpId", "Ordinal"], unique: true);
        migrationBuilder.CreateIndex("IX_PlatformOrders_ErpId_PoNumber", "PlatformOrders", ["ErpId", "PoNumber"], unique: true);
        migrationBuilder.CreateIndex("IX_Shops_Sid", "Shops", "Sid", unique: true);
        migrationBuilder.CreateIndex("IX_ShopPlatforms_Name", "ShopPlatforms", "Name", unique: true);
        migrationBuilder.AddForeignKey("FK_OrderLines_Orders_ErpId", "OrderLines", "ErpId", "Orders", principalColumn: "ErpId",
            onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_PlatformOrders_Orders_ErpId", "PlatformOrders", "ErpId", "Orders", principalColumn: "ErpId",
            onDelete: ReferentialAction.Cascade);

        foreach (var table in new[] { "OrderSyncSettings", "ErpSettings", "ShopSyncStates" })
        {
            migrationBuilder.DropCheckConstraint($"CK_{table}_Singleton", table);
            MigratePrimaryKey(migrationBuilder, table, replaceId: true);
            migrationBuilder.AddColumn<bool>("Singleton", table, nullable: false, defaultValue: true);
            migrationBuilder.AddCheckConstraint($"CK_{table}_Singleton", table, "\"Singleton\"");
            migrationBuilder.CreateIndex($"IX_{table}_Singleton", table, "Singleton", unique: true);
        }
    }

    private static void MigratePrimaryKey(MigrationBuilder migrationBuilder, string table, bool replaceId = false)
    {
        migrationBuilder.DropPrimaryKey($"PK_{table}", table);
        if (replaceId)
            migrationBuilder.DropColumn("Id", table);

        // PostgreSQL 为每条旧记录生成 UUIDv7，后续插入由应用生成 UUIDv7。
        migrationBuilder.AddColumn<Guid>("Id", table, type: "uuid", nullable: false, defaultValueSql: "uuidv7()");
        migrationBuilder.Sql($"""ALTER TABLE "{table}" ALTER COLUMN "Id" DROP DEFAULT""");
        migrationBuilder.AddColumn<DateTimeOffset>("DeletedAt", table, type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddPrimaryKey($"PK_{table}", table, "Id");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("旧版本无法保留用户身份和软删除数据；请从升级前的备份恢复。");
}
