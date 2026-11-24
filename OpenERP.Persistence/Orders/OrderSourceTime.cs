using System.Globalization;

namespace OpenERP.Api.Orders;

internal static class OrderSourceTime
{
    // null 留给待回填的旧行，-1 表示已经解析但缺少可确定时间。
    internal const long UnknownTicks = -1;
    private static readonly string[] WallTimeFormats =
        ["HH", "HH:mm", "HH:mm:ss", "HH:mm:ss.f", "HH:mm:ss.ff", "HH:mm:ss.fff",
            "HH:mm:ss.ffff", "HH:mm:ss.fffff", "HH:mm:ss.ffffff"];

    internal static long ToTicks(string? raw)
    {
        var source = raw.AsSpan().Trim(' ');
        if (source.Length == 10 && source.IndexOfAnyExceptInRange('0', '9') < 0
            && long.TryParse(source, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcTicks;

        if (source.Length < 10 || !TryParseDate(source[..10], out var date))
            return UnknownTicks;
        if (source.Length == 10)
            return date.DayNumber * TimeSpan.TicksPerDay;
        if (source[10] is not (' ' or 'T') || !TryParseWallTime(source[11..], out var time))
            return UnknownTicks;

        return date.DayNumber * TimeSpan.TicksPerDay + time.Ticks;
    }

    internal static string? Format(string? raw, long? ticks)
    {
        if (raw is null || ticks is not >= 0)
            return null;

        var source = raw.Trim(' ');
        var time = new DateTime(ticks.Value);
        if (source.Length == 10 && source.All(char.IsAsciiDigit))
            return time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // 只统一日期顺序和分隔符，保留来源的时间精度及显式偏移。
        var date = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return source.Length > 10 ? date + "T" + source[11..] : date;
    }

    private static bool TryParseDate(ReadOnlySpan<char> source, out DateOnly date)
    {
        var culture = CultureInfo.InvariantCulture;
        if (source[4] == '-')
            return DateOnly.TryParseExact(source, "yyyy-MM-dd", culture, DateTimeStyles.None, out date);

        var monthFirst = DateOnly.TryParseExact(source, "MM-dd-yyyy", culture, DateTimeStyles.None, out date);
        var dayFirst = DateOnly.TryParseExact(source, "dd-MM-yyyy", culture, DateTimeStyles.None, out var alternate);
        if (monthFirst && dayFirst)
            return date == alternate;
        if (dayFirst)
            date = alternate;
        return monthFirst || dayFirst;
    }

    private static bool TryParseWallTime(ReadOnlySpan<char> source, out TimeOnly time)
    {
        time = default;
        // 验证偏移后按来源墙钟时间计算日期与排序；小时精度的时间没有偏移。
        if (source.Length >= 6 && source[^1] == 'Z')
            source = source[..^1];
        else if (source.Length >= 11 && source[^6] is '+' or '-')
        {
            var offset = source[^5..];
            if (offset[2] != ':'
                || !int.TryParse(offset[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours > 15
                || !int.TryParse(offset[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 59)
                return false;
            source = source[..^6];
        }

        return TimeOnly.TryParseExact(source, WallTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
