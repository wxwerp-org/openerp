using OpenERP.Api;
using OpenERP.Api.About;
using OpenERP.Api.Authentication;
using OpenERP.Api.Deployment;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;
using OpenERP.Api.Reports;
using OpenERP.Core.Reports;

if (args is ["reset-admin-password", ..])
    return await RunPasswordResetAsync(args);
if (args is ["initialize-compose-credentials", ..])
    return ComposeCredentials.Run(args);

var builder = NativeLocalHosting.CreateBuilder(args);
NativeLocalHosting.ConfigurePublishedHosting(builder);

builder.AddServiceDefaults();
builder.AddApplicationStorage();
builder.Services.AddOrderFingerprintCache();
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
    await scope.ServiceProvider.GetRequiredService<IApplicationDatabase>().StartAsync(stopping);
    await scope.ServiceProvider.GetRequiredService<AccountStore>().SeedAsync(stopping);
}

NativeLocalHosting.MapApplicationFiles(app);
app.UseAccountAuthentication();
app.MapDefaultEndpoints();
app.MapAccounts();
app.MapUserManagement();
app.MapAbout();
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    await app.Services.GetRequiredService<IFileStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
    app.MapFiles();
}
app.UseWebSockets();
app.MapErp();
app.MapHub<ErpHub>("/api/hubs/erp", options => options.CloseOnAuthenticationExpiration = true);
app.MapOrderReports();
app.MapOrderList();

await app.RunAsync();
return 0;

static async Task<int> RunPasswordResetAsync(string[] args)
{
    if (await ResetAdminPassword.ReadNewPasswordAsync(args) is not { } password)
        return 1;

    try
    {
        var builder = NativeLocalHosting.CreateBuilder([]);
        builder.Logging.AddFilter(Microsoft.EntityFrameworkCore.DbLoggerCategory.Database.Command.Name, LogLevel.Warning);
        builder.AddApplicationStorage(maintenance: true);
        builder.Services.AddAccountStore();
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IApplicationDatabase>().OpenExistingAsync(CancellationToken.None);
        return await ResetAdminPassword.RunAsync(scope.ServiceProvider.GetRequiredService<IAdministratorPasswordReset>(), password);
    }
    catch (Exception error) when (DatabaseErrors.IsUnavailable(error))
    {
        Console.Error.WriteLine("无法重置管理员密码，请检查数据库连接和迁移状态。");
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
    {
        Console.Error.WriteLine(error.Message);
    }
    return 1;
}
