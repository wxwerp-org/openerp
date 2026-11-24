namespace OpenERP.Api;

/// <summary>启动独立日用发布配置，不改变 Aspire 开发入口。</summary>
internal static class NativeLocalHosting
{
    private const int DefaultPort = 17083;

    internal static bool IsPublished { get; } = AppContext.TryGetSwitch("OpenERP.NativeLocal", out var enabled) && enabled;

    internal static WebApplicationBuilder CreateBuilder(string[] args)
    {
        if (!IsPublished)
            return WebApplication.CreateBuilder(args);

        return WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });
    }

    internal static void ConfigurePublishedHosting(WebApplicationBuilder builder)
    {
        if (!IsPublished)
            return;

        // 日用实例禁止继承外部监听地址，也拒绝 Kestrel:Endpoints 的显式绑定覆盖。
        builder.Configuration[WebHostDefaults.ServerUrlsKey] = null;
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            throw new InvalidOperationException("日用模式不接受 Kestrel:Endpoints 配置，只允许监听本机 localhost。");
        ConfigureHttpsLoopback(builder);
    }

    internal static void MapApplicationFiles(WebApplication app)
    {
        if (!IsPublished)
            return;

        app.UseStaticFiles();
        app.MapFallback("/api", () => Results.NotFound()).AllowAnonymous();
        app.MapFallback("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
        app.MapFallback("/assets/{**path}", () => Results.NotFound()).AllowAnonymous();
        app.MapFallbackToFile("index.html").AllowAnonymous();
    }

    private static void ConfigureHttpsLoopback(WebApplicationBuilder builder)
    {
        var port = builder.Configuration.GetValue<int?>("Native:Port") ?? DefaultPort;
        if (port is < 1024 or > 65535)
            throw new InvalidOperationException("Native:Port 必须在 1024 到 65535 之间。");

        // HTTPS 使用 Kestrel 的默认配置和当前用户的开发证书。
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, endpoint => endpoint.UseHttps()));
    }
}
