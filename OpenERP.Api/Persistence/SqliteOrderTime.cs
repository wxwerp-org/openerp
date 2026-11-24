using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OpenERP.Api.Persistence;

/// <summary>SQLite 查询用的来源墙钟时间。与 PostgreSQL 投影共用可接受的输入语法。</summary>
internal static class SqliteOrderTime
{
    private static readonly Regex Iso = Pattern(OrderTimePatterns.IsoTimePattern);
    private static readonly Regex YearLast = Pattern(OrderTimePatterns.YearLastPattern);
    private static readonly Regex HourOnly = Pattern(OrderTimePatterns.HourOnlyPattern);
    private static readonly Regex Epoch = Pattern(OrderTimePatterns.EpochSecondsPattern);
    private static readonly string[] WallTimeFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFF"];

    // PostgreSQL 的 $ 匹配整个输入末尾，.NET 的 $ 也会匹配最后一个换行符之前。
    private static Regex Pattern(string source) =>
        new(source.Replace("$", @"\z", StringComparison.Ordinal), RegexOptions.CultureInvariant);

    internal static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>("openerp_wall_time",
            raw => Parse(raw)?.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture), isDeterministic: true);
    }

    private static DateTime? Parse(string? raw)
    {
        if (raw is null)
            return null;
        var source = raw.Trim(' ');
        if (Epoch.IsMatch(source))
            return DateTimeOffset.FromUnixTimeSeconds(long.Parse(source, CultureInfo.InvariantCulture)).UtcDateTime;

        if (YearLast.IsMatch(source))
        {
            var first = int.Parse(source.AsSpan(0, 2), CultureInfo.InvariantCulture);
            var second = int.Parse(source.AsSpan(3, 2), CultureInfo.InvariantCulture);
            if (first is >= 1 and <= 12 && second is >= 1 and <= 12 && first != second)
                return null;
            var monthDay = first > 12 ? source.Substring(3, 2) + "-" + source[..2] : source[..5];
            source = source.Substring(6, 4) + "-" + monthDay + source[10..];
        }

        source = HourOnly.Replace(source, "${1}:00");
        if (!Iso.IsMatch(source))
            return null;
        // timestamp without time zone 校验偏移的范围，但保留墙钟时间。
        var offset = source.AsSpan(10).IndexOfAny('+', '-', 'Z');
        if (offset >= 0)
        {
            var index = offset + 10;
            if (source[index] != 'Z' && int.Parse(source.AsSpan(index + 1, 2), CultureInfo.InvariantCulture) > 15)
                return null;
            source = source[..index];
        }

        return DateTime.TryParseExact(source.Replace('T', ' '), WallTimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value) ? value : null;
    }
}
