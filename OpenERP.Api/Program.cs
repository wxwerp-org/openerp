using OpenERP.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("openerp");
builder.AddRedisClient("redis");

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/api/ping", (ILogger<Program> logger) =>
{
    logger.LogInformation("OpenERP connectivity check received");
    return Results.Ok(new { message = "pong" });
});

app.Run();
