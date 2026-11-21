using Npgsql;

namespace OpenERP.Api.Orders;

internal static class OrderCreatedTimeSql
{
    private const string IsoTimePattern = @"^[0-9]{4}-[0-9]{2}-[0-9]{2}([ T]([01][0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9](\.[0-9]{1,6})?)?(Z|[+-]([01][0-9]:[0-5][0-9]))?)?$";
    private const string YearLastPattern = "^[0-9]{2}-[0-9]{2}-[0-9]{4}($|[ T])";
    private const string HourOnlyPattern = "([ T][0-9]{2})$";
    private const string EpochSecondsPattern = "^[0-9]{10}$";

    internal static NpgsqlParameter[] Parameters() =>
    [
        new("iso", IsoTimePattern), new("year_last", YearLastPattern),
        new("hour_only", HourOnlyPattern), new("hour_with_minute", @"\1:00"), new("epoch", EpochSecondsPattern)
    ];

    internal static string Normalize(string source) =>
        $"regexp_replace({NormalizeYearLastSql(source)}, @hour_only, @hour_with_minute)";

    // 转换前验证日历日期，保留来源墙钟时间。Epoch 时间使用 UTC 表示。
    internal static string Parse(string source, string normalized) => $$"""
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
