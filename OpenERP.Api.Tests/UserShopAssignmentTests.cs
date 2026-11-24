using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class UserShopAssignmentTests(AspireIntegrationFixture fixture)
{
    private sealed record ShopKey(string Platform, int Sid);

    [Fact]
    public async Task Administrator_assigns_and_revokes_catalog_shops_independently_of_directory_refresh()
    {
        // Given 两名员工、两家目录店铺。新建员工没有默认归属。
        var ct = TestContext.Current.CancellationToken;
        await fixture.EnsureApiStartedAsync(ct);
        using var admin = fixture.CreateApiClient();
        using var employee = fixture.CreateApiClient();
        var administrator = await AuthenticationIntegrationTests.LoginAsync(admin, ct, fixture.AdminPassword);
        var username = $"li{Guid.NewGuid():N}"[..18];
        const string password = "employee-105";
        var li = await CreateEmployeeAsync(username);
        var colleague = await CreateEmployeeAsync($"u{Guid.NewGuid():N}"[..18]);
        await AuthenticationIntegrationTests.LoginAsync(employee, ct, password, username);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseOpenErpPostgres(await fixture.GetPostgresConnectionStringAsync(ct)).Options;
        await using var db = new AppDbContext(options);
        var first = new ShopKey("TemuPop", 105001);
        var second = new ShopKey("Temu", 105002);
        db.Shops.AddRange(new Shop { Platform = first.Platform, Sid = first.Sid, Name = "一店" },
            new Shop { Platform = second.Platform, Sid = second.Sid, Name = "二店" });
        await db.SaveChangesAsync(ct);
        Assert.Empty(await ReadAsync(li));

        // When 管理员把两店分给小李、把其中一家也分给同事。Then 每次读取保留各自的集合。
        await PutAsync(admin, li, [first, second], HttpStatusCode.NoContent);
        await PutAsync(admin, colleague, [first], HttpStatusCode.NoContent);
        Assert.Equal([second, first], await ReadAsync(li));
        Assert.Equal([first], await ReadAsync(colleague));

        // When 员工试图自行分配或管理员选中不存在的店铺。Then 拒绝整个变更，原分配保留。
        await PutAsync(employee, li, [], HttpStatusCode.Forbidden);
        await PutAsync(admin, li, [new("WrongPlatform", first.Sid)], HttpStatusCode.BadRequest);
        await PutAsync(admin, administrator.GetProperty("id").GetGuid(), [first], HttpStatusCode.Conflict);
        Assert.Equal([second, first], await ReadAsync(li));

        // When 上游移除一店。Then 管理员仍可保留原归属、撤销另一店；同 SID 再出现时不必重新分配。
        await db.Shops.Where(s => s.Sid == first.Sid).ExecuteDeleteAsync(ct);
        await PutAsync(admin, li, [first], HttpStatusCode.NoContent);
        db.Shops.Add(new Shop { Platform = first.Platform, Sid = first.Sid, Name = "恢复的一店" });
        await db.SaveChangesAsync(ct);
        Assert.Equal([first], await ReadAsync(li));
        await PutAsync(admin, li, [], HttpStatusCode.NoContent);
        Assert.Empty(await ReadAsync(li));

        // When 删除仍有分配的员工。Then 软删除同时清理归属。
        using var deleted = await admin.DeleteAsync($"/api/users/{colleague:D}", ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await db.Database.SqlQuery<int>(
            $"""SELECT COUNT(*)::int AS "Value" FROM "UserShopAssignments" WHERE "UserId" = {colleague}""")
            .SingleAsync(ct));
        await db.Shops.Where(s => s.Sid == first.Sid || s.Sid == second.Sid).ExecuteDeleteAsync(ct);

        static string Url(Guid id) => $"/api/users/{id:D}/shops";

        async Task<ShopKey[]> ReadAsync(Guid id) =>
            Assert.IsType<ShopKey[]>(await admin.GetFromJsonAsync<ShopKey[]>(Url(id), ct));

        async Task<Guid> CreateEmployeeAsync(string name)
        {
            using var created = await admin.PostAsJsonAsync("/api/users", new { username = name, name, password }, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }

        async Task PutAsync(HttpClient client, Guid id, ShopKey[] shops, HttpStatusCode expected)
        {
            using var response = await client.PutAsJsonAsync(Url(id), shops, ct);
            Assert.Equal(expected, response.StatusCode);
        }
    }
}
