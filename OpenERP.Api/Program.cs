using OpenERP.Api;
using OpenERP.Api.Erp;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("openerp");
builder.AddRedisClient("redis");
builder.AddFileStorage();
builder.Services.AddSingleton<FileStore>();
builder.Services.AddHttpClient<IErpClient, WxwErpClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    .RedactLoggedHeaders(["AccessToken"]);

var app = builder.Build();

app.MapDefaultEndpoints();
// This unauthenticated example is local-development only, like the current app skeleton.
if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<FileStore>().EnsureBucketAsync(app.Lifetime.ApplicationStopping);
    app.MapFiles();
}
app.MapErp();
app.MapGet("/api/ping", (ILogger<Program> logger) =>
{
    logger.LogInformation("OpenERP connectivity check received");
    return Results.Ok(new { message = "pong" });
});

app.Run();
