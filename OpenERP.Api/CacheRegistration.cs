namespace OpenERP.Api;

internal static class CacheRegistration
{
    internal static void AddApplicationCache(this IHostApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
            builder.AddRedisDistributedCache("redis");
        builder.Services.AddHybridCache();
    }
}
