namespace OpenERP.Api.Persistence;

/// <summary>订单来源时间的语法，两种数据库接受相同的输入。</summary>
internal static class OrderTimePatterns
{
    internal const string IsoTimePattern = @"^[0-9]{4}-[0-9]{2}-[0-9]{2}([ T]([01][0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9](\.[0-9]{1,6})?)?(Z|[+-]([01][0-9]:[0-5][0-9]))?)?$";
    internal const string YearLastPattern = "^[0-9]{2}-[0-9]{2}-[0-9]{4}($|[ T])";
    internal const string HourOnlyPattern = "([ T][0-9]{2})$";
    internal const string EpochSecondsPattern = "^[0-9]{10}$";
}
