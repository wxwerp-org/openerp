using Microsoft.EntityFrameworkCore;
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
    return await ResetAdminPassword.RunAsync(args);
if (args is ["initialize-compose-credentials", ..])
    return ComposeCredentials.Run(args);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var nativeRoot = builder.Configuration["Native:DataDirectory"];
using var nativeLease = nativeRoot is null ? null : NativeDatabase.Acquire(nativeRoot);
if (nativeRoot is null)
{
    builder.AddNpgsqlDbContext<AppDbContext>("openerp");
    builder.AddRedisDistributedCache("redis");
    builder.AddFileStorage();
    builder.Services.AddSingleton<FileStore>();
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(NativeDatabase.ConnectionString(nativeRoot)));
    builder.Services.AddSingleton(services => new FileStore(
        new DirectoryInfo(Path.Combine(nativeRoot, "objects")), services.GetRequiredService<ILogger<FileStore>>()));
}
builder.Services.AddHybridCache();
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
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (nativeRoot is not null)
        await NativeDatabase.InitializeAsync(db, stopping);
    else if (app.Environment.IsDevelopment())
        await db.MigrateDevelopmentDatabaseAsync(stopping);
    await scope.ServiceProvider.GetRequiredService<AccountStore>().SeedAsync(stopping);
}

app.UseAccountAuthentication();
app.MapDefaultEndpoints();
app.MapAccounts();
app.MapUserManagement();
app.MapAbout();
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

await app.RunAsync();
return 0;
