using OpenERP.Api;
using OpenERP.Api.Authentication;
using OpenERP.Api.Deployment;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Reports;
using OpenERP.Core.Reports;

if (args is ["reset-admin-password", ..])
    return await ResetAdminPassword.RunAsync(args);
if (args is ["initialize-compose-credentials", ..])
    return ComposeCredentials.Run(args);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("openerp");
builder.AddRedisDistributedCache("redis");
builder.Services.AddHybridCache();
builder.AddFileStorage();
builder.Services.AddSingleton<FileStore>();
builder.AddAccountAuthentication();
builder.Services.AddScoped<IOrderSnapshotReportQuery, OrderSnapshotReportQuery>();
builder.Services.AddScoped<OrderListQuery>();
builder.Services.AddScoped<IErpTokenStore, ErpTokenStore>();
builder.Services.AddHttpClient<WxwErpClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    .RedactLoggedHeaders(["AccessToken"]);
builder.Services.AddTransient<IErpClient>(services => services.GetRequiredService<WxwErpClient>());
builder.Services.AddSignalR();
builder.Services.AddSingleton<IShopSyncNotifier, ShopSyncNotifier>();
builder.Services.AddSingleton<ShopSyncJob>();
builder.Services.AddSingleton<IShopSyncJob>(services => services.GetRequiredService<ShopSyncJob>());
builder.Services.AddOptions<ShopSyncOptions>()
    .Bind(builder.Configuration.GetSection("ShopSync"))
    .Validate(options => options.IntervalMinutes is
        >= ShopSyncOptions.MinIntervalMinutes and <= ShopSyncOptions.MaxIntervalMinutes,
        "ShopSync:IntervalMinutes 必须在 1 到 1440 之间。")
    .ValidateOnStart();
if (builder.Environment.IsDevelopment() || builder.Environment.IsProduction())
{
    builder.Services.AddScoped<OrderStore>();
    builder.Services.AddSingleton<OrderSyncJobs>();
    builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
    builder.Services.AddScoped<OrderSyncSettingsStore>();
    builder.Services.AddScoped<IOrderSyncSettingsStore>(services => services.GetRequiredService<OrderSyncSettingsStore>());
    builder.Services.AddOptions<OrderSyncSchedulerOptions>()
        .Bind(builder.Configuration.GetSection("OrderSync"))
        .Validate(options => options.SchedulerIntervalMinutes is
            >= OrderSyncSchedulerOptions.MinIntervalMinutes and <= OrderSyncSchedulerOptions.MaxIntervalMinutes,
            "OrderSync:SchedulerIntervalMinutes 必须在 1 到 1440 之间。")
        .ValidateOnStart();
    builder.Services.AddSingleton<OrderSyncScheduler>();
    builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncScheduler>());
    builder.Services.AddHostedService<ShopSyncScheduler>();
}

await using var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var stopping = app.Lifetime.ApplicationStopping;
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().MigrateDevelopmentDatabaseAsync(stopping);
    await scope.ServiceProvider.GetRequiredService<AccountStore>().SeedAsync(stopping);
}

app.UseAccountAuthentication();
app.MapDefaultEndpoints();
app.MapAccounts();
app.MapUserManagement();
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    await app.Services.GetRequiredService<FileStore>().EnsureBucketAsync(app.Lifetime.ApplicationStopping);
    app.MapFiles();
}
app.UseWebSockets();
app.MapErp();
app.MapHub<ErpHub>("/api/hubs/erp", options => options.CloseOnAuthenticationExpiration = true);
app.MapOrderReports();
app.MapOrderList();
app.MapGet("/api/ping", (ILogger<Program> logger) =>
{
    logger.LogInformation("OpenERP connectivity check received");
    return Results.Ok(new { message = "pong" });
});

await app.RunAsync();
return 0;
