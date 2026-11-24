using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class NativeAccountStoreTests
{
    [Fact]
    public async Task Sqlite_preserves_seed_time_and_invalidates_old_sessions_after_password_reset()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteAppDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = new DateTimeOffset(2026, 11, 21, 12, 34, 56, TimeSpan.Zero);
        var store = new AccountStore(db, new SqliteAccountWrites(options), new PasswordHasher<UserAccount>(), new FixedTime(now));

        await store.SeedAsync(ct);
        await store.SeedAsync(ct);
        var admin = await db.Users.AsNoTracking().SingleAsync(ct);
        Assert.Equal(now, admin.CreatedAt);
        Assert.True(await store.InitializeAsync("first-password", ct));
        Assert.False(await store.InitializeAsync("second-password", ct));
        var tokenHash = RandomNumberGenerator.GetBytes(32);
        Assert.NotNull(await store.LoginAsync("admin", "first-password", tokenHash, ct));
        Assert.NotNull(await store.FindActiveRefreshTokenAsync(tokenHash, ct));

        Assert.True(await store.ResetPasswordAsync("new-password", ct));
        Assert.Null(await store.FindActiveRefreshTokenAsync(tokenHash, ct));
        Assert.Null(await store.LoginAsync("admin", "first-password", RandomNumberGenerator.GetBytes(32), ct));
        Assert.NotNull(await store.LoginAsync("admin", "new-password", RandomNumberGenerator.GetBytes(32), ct));
    }

    [Theory]
    [InlineData(true, "INTEGER", null)]
    [InlineData(false, "timestamp with time zone", "gen_random_uuid()")]
    public void Provider_models_keep_their_time_type_and_revision_default(bool sqlite, string type, string? defaultSql)
    {
        using AppDbContext db = sqlite
            ? new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=:memory:").Options)
            : new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=model").Options);

        var timestamp = db.Model.FindEntityType(typeof(UserAccount))!.FindProperty(nameof(UserAccount.CreatedAt))!;
        Assert.Equal(type, timestamp.GetColumnType());
        var revision = db.Model.FindEntityType(typeof(OrderRecord))!.FindProperty(nameof(OrderRecord.SyncRevision))!;
        Assert.Equal(defaultSql, revision.GetDefaultValueSql());
        Assert.True(revision.IsConcurrencyToken);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
