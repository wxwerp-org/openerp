using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Hosting;

namespace OpenERP.Api;

internal static class ProductionHosting
{
    private const string KnownProxiesKey = "Deployment:KnownProxies";

    internal static bool IsDevelopmentOrProduction(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsProduction();

    internal static WebApplicationBuilder ConfigureProductionHosting(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction())
            return builder;

        var allowedHosts = ParseAllowedHosts(builder.Configuration["AllowedHosts"]);
        var knownProxySettings = builder.Configuration.GetSection(KnownProxiesKey).Get<string[]>() ?? [];
        var knownProxies = knownProxySettings.Select(ParseProxyAddress).ToArray();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OPENERP_RUNTIME_CONFIG"]))
        {
            if (allowedHosts.Length == 0 || allowedHosts.Contains("*", StringComparer.Ordinal))
                throw new InvalidOperationException("容器 Production 必须通过 AllowedHosts 显式配置允许的主机，且不能使用 *。");
            if (knownProxies.Length == 0)
                throw new InvalidOperationException($"容器 Production 必须显式配置至少一个 {KnownProxiesKey} 地址。");
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = knownProxies.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in knownProxies)
                options.KnownProxies.Add(proxy);
        });

        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts.Clear();
            foreach (var host in allowedHosts.Length == 0 ? ["*"] : allowedHosts)
                options.AllowedHosts.Add(host);
        });

        return builder;
    }

    internal static WebApplication UseProductionHosting(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
            return app;

        app.UseForwardedHeaders();
        app.UseHostFiltering();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallback(async context =>
        {
            if (IsReservedPath(context.Request.Path) ||
                !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
            var indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(indexPath, context.RequestAborted);
        }).AllowAnonymous();

        return app;
    }

    private static string[] ParseAllowedHosts(string? value) =>
        value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static IPAddress ParseProxyAddress(string value)
    {
        if (IPAddress.TryParse(value, out var address))
            return address;

        throw new InvalidOperationException($"{KnownProxiesKey} 只能包含 IP 地址。");
    }

    private static bool IsReservedPath(PathString path) =>
        path.StartsWithSegments("/api") ||
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/alive") ||
        path.StartsWithSegments("/metrics") ||
        path.StartsWithSegments("/openapi") ||
        path.StartsWithSegments("/swagger");
}
