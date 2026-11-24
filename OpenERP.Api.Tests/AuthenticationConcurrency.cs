using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Npgsql;

namespace OpenERP.Api.Tests;

internal sealed record BlockedOperation(string Name, Task Task, Func<string>? Describe = null);

internal static class AuthenticationConcurrency
{
    internal static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ProcessBudget = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan ProcessCleanupBudget = TimeSpan.FromSeconds(10);

    internal static async Task WaitForAdminLocksAsync(
        NpgsqlConnection observer, long expected, CancellationToken ct, string phase,
        IReadOnlyList<BlockedOperation> operations, TimeSpan? budget = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(budget ?? TimeSpan.FromSeconds(30));
        await using var clearSnapshot = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", observer);
        await using var activity = new NpgsqlCommand("""
            SELECT pid, state, wait_event_type, wait_event,
                   wait_event_type = 'Lock' AND matches AS blocked
            FROM (
                SELECT pid, state, wait_event_type, wait_event,
                       query LIKE '%UPDATE "Users"%' AS matches
                FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
            ) sessions
            WHERE matches OR state = 'idle in transaction'
            ORDER BY pid
            """, observer);
        var completion = Task.WhenAny(operations.Select(operation => operation.Task));
        long? observed = null;
        var sessions = new List<string>();
        try
        {
            while (true)
            {
                // 同一个持锁事务会在多次轮询间复用 PostgreSQL 的统计快照。
                await clearSnapshot.ExecuteNonQueryAsync(timeout.Token);
                await using (var reader = await activity.ExecuteReaderAsync(timeout.Token))
                {
                    var currentSessions = new List<string>();
                    long currentCount = 0;
                    while (await reader.ReadAsync(timeout.Token))
                    {
                        if (!reader.IsDBNull(4) && reader.GetBoolean(4))
                            currentCount++;
                        currentSessions.Add($"pid={reader.GetInt32(0)}, state={Field(1)}, wait={Field(2)}/{Field(3)}");
                    }

                    observed = currentCount;
                    sessions = currentSessions;
                    string Field(int index) => reader.IsDBNull(index) ? "-" : reader.GetString(index);
                }

                if (operations.FirstOrDefault(operation => operation.Task.IsCompleted) is { } completed)
                    throw Failure($"{completed.Name} 提前{DescribeCompletion(completed)}");
                if (observed == expected)
                    return;

                var interval = Task.Delay(20, timeout.Token);
                if (await Task.WhenAny(completion, interval) == completion)
                {
                    var completedTask = await completion;
                    var operation = operations.First(operation => operation.Task == completedTask);
                    throw Failure($"{operation.Name} 提前{DescribeCompletion(operation)}");
                }
                await interval;
            }
        }
        catch (OperationCanceledException)
        {
            throw Failure(ct.IsCancellationRequested ? "等待已取消" : "等待锁超时");
        }
        catch (NpgsqlException error)
        {
            var detail = error is PostgresException postgres ? postgres.SqlState : error.GetType().Name;
            throw Failure($"读取 PostgreSQL 活动失败（{detail}）");
        }

        InvalidOperationException Failure(string reason) =>
            new($"{phase}：{reason}。耗时 {stopwatch.ElapsedMilliseconds} ms，期待 {expected} 个管理员锁等待，"
                + $"观察到 {observed?.ToString() ?? "未知"} 个。相关数据库活动："
                + (sessions.Count == 0 ? "无" : string.Join("; ", sessions)));
    }

    private static string DescribeCompletion(BlockedOperation operation)
    {
        if (operation.Task.IsCanceled)
            return "取消";
        if (operation.Task.IsFaulted)
            return $"失败（{operation.Task.Exception!.GetBaseException().GetType().Name}）";
        return operation.Describe is { } describe ? $"结束（{describe()}）" : "结束";
    }

    internal static async Task ReleaseAsync(Func<CancellationToken, Task> release,
        CancellationTokenSource operations, Exception? primaryError, CancellationToken cancellationToken, params Task?[] tasks)
    {
        var failures = new List<Exception>();
        var cleanupElapsed = Stopwatch.StartNew();
        // 事务释放最多占用 10 秒，给后续自然退出、取消与 CLI 回收保留时间。
        using (var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            try
            {
                await release(releaseTimeout.Token);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        var tracked = tasks.OfType<Task>().ToArray();
        var all = Task.WhenAll(tracked);
        if (primaryError is null && failures.Count == 0)
        {
            try
            {
                await all.WaitAsync(ProcessBudget, cancellationToken);
                return;
            }
            catch (Exception error)
            {
                primaryError = error;
                cleanupElapsed.Restart();
            }
        }

        var remaining = CleanupBudget - cleanupElapsed.Elapsed;
        using var cleanup = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        if (failures.Count == 0 && !all.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            // 解锁后先给服务端完成请求的机会，再取消仍未结束的客户端操作。
            using var grace = CancellationTokenSource.CreateLinkedTokenSource(cleanup.Token);
            grace.CancelAfter(TimeSpan.FromSeconds(5));
            await Task.WhenAny(all, Task.Delay(Timeout.InfiniteTimeSpan, grace.Token));
        }

        try
        {
            await operations.CancelAsync().WaitAsync(cleanup.Token);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }

        try
        {
            await all.WaitAsync(cleanup.Token);
        }
        catch
        {
            if (!all.IsCompleted)
                failures.Add(new TimeoutException("释放事务后，等待并发操作退出超时。"));
        }

        foreach (var task in tracked)
        {
            if (task.Exception is { } errors)
                failures.AddRange(errors.InnerExceptions.Where(error => !ReferenceEquals(error, primaryError)));
            if (task is Task<HttpResponseMessage> { IsCompletedSuccessfully: true } response)
                response.Result.Dispose();
        }

        if (failures.Count > 0)
        {
            if (primaryError is not null)
                failures.Insert(0, primaryError);
            throw new AggregateException("管理员锁竞争测试或收尾失败。", failures);
        }
        if (primaryError is not null)
            ExceptionDispatchInfo.Throw(primaryError);
    }
}
