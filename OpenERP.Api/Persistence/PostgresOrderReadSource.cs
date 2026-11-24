using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Persistence;

internal sealed class PostgresOrderReadSource(AppDbContext db) : IOrderReadSource
{
    private static readonly string OrderHeadersSql = $$"""
        SELECT o."ErpId", o."Sid", o."Platform",
               parsed.created_at AS "CreatedAt", parsed.created_at::date AS "CreatedDate",
               parsed.dispatched_at AS "DispatchedAt",
               source.created AS "CreatedRaw", source.dispatched AS "DispatchedRaw"
        FROM "Orders" AS o
        CROSS JOIN LATERAL (VALUES (btrim(o."CreatedRaw"), btrim(o."DispatchedRaw"))) AS source(created, dispatched)
        CROSS JOIN LATERAL (VALUES (
            {{Normalize("source.created")}},
            {{Normalize("source.dispatched")}}
        )) AS normalized(created, dispatched)
        CROSS JOIN LATERAL (
            SELECT {{Parse("source.created", "normalized.created")}} AS created_at,
                   {{Parse("source.dispatched", "normalized.dispatched")}} AS dispatched_at
        ) AS parsed
        WHERE o."DeletedAt" IS NULL
        """;

    public IQueryable<OrderHeader> ReadHeaders() =>
        db.Database.SqlQueryRaw<OrderHeader>(OrderHeadersSql, Parameters());

    public Task<T> ReadSnapshotAsync<T>(Func<Task<T>> read, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var result = await read();
            await transaction.CommitAsync(ct);
            return result;
        });

    private static NpgsqlParameter[] Parameters() =>
    [
        new("iso", OrderTimePatterns.IsoTimePattern), new("year_last", OrderTimePatterns.YearLastPattern),
        new("hour_only", OrderTimePatterns.HourOnlyPattern), new("hour_with_minute", @"\1:00"), new("epoch", OrderTimePatterns.EpochSecondsPattern)
    ];

    private static string Normalize(string source) =>
        $"regexp_replace({NormalizeYearLastSql(source)}, @hour_only, @hour_with_minute)";

    // 转换前验证日历日期，保留来源墙钟时间。Epoch 时间使用 UTC 表示。
    private static string Parse(string source, string normalized) => $$"""
        CASE
            WHEN {{normalized}} ~ @iso AND pg_input_is_valid({{normalized}}, 'timestamp without time zone')
                THEN {{normalized}}::timestamp
            WHEN {{source}} ~ @epoch THEN to_timestamp({{source}}::bigint) AT TIME ZONE 'UTC'
        END
        """;

    // 仅接收调用方 SQL 中的固定列引用。年在后时，两个不同的 01–12 分量会产生两个有效日期。
    private static string NormalizeYearLastSql(string column) => $$"""
        CASE WHEN {{column}} ~ @year_last THEN
            CASE
                WHEN substr({{column}}, 1, 2)::int BETWEEN 1 AND 12
                     AND substr({{column}}, 4, 2)::int BETWEEN 1 AND 12
                     AND substr({{column}}, 1, 2) <> substr({{column}}, 4, 2) THEN NULL
                ELSE substr({{column}}, 7, 4) || '-' ||
                     CASE WHEN substr({{column}}, 1, 2)::int > 12
                          THEN substr({{column}}, 4, 2) || '-' || substr({{column}}, 1, 2)
                          ELSE substr({{column}}, 1, 5)
                     END || substr({{column}}, 11)
            END
        ELSE {{column}} END
        """;
}
