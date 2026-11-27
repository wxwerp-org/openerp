using Microsoft.EntityFrameworkCore;

namespace OpenERP.Api.Persistence;

internal static class SingletonSettings
{
    // 更新同时写入所有字段；两个首次保存若竞争插入，由唯一约束决定谁重试更新。
    internal static async Task SaveAsync<TEntity>(AppDbContext db, TEntity settings,
        Func<CancellationToken, Task<int>> update, CancellationToken ct) where TEntity : Entity
    {
        if (await update(ct) != 0)
            return;

        var entry = db.Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (DatabaseErrors.IsUniqueConstraintViolation(error))
        {
            if (await update(ct) == 0)
                throw;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }
}
