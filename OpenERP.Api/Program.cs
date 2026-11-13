using Microsoft.EntityFrameworkCore;
using OpenERP.Api;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("openerp");
builder.AddRedisClient("redis");
builder.AddFileStorage();
builder.Services.AddSingleton<FileStore>();
builder.Services.AddHttpClient<WxwErpClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    .RedactLoggedHeaders(["AccessToken"]);
builder.Services.AddTransient<IErpClient>(services => services.GetRequiredService<WxwErpClient>());
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<OrderStore>();
    builder.Services.AddSingleton<OrderSyncJobs>();
    builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
}

var app = builder.Build();

app.MapDefaultEndpoints();
// This unauthenticated example is local-development only, like the current app skeleton.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    await app.Services.GetRequiredService<FileStore>().EnsureBucketAsync(app.Lifetime.ApplicationStopping);
    app.MapFiles();
    app.MapOrderSync();
}
app.MapErp();
app.MapGet("/api/ping", (ILogger<Program> logger) =>
{
    logger.LogInformation("OpenERP connectivity check received");
    return Results.Ok(new { message = "pong" });
});

app.Run();
