using System.Net;
using System.Net.Security;
using System.Text.Json;
using OpenERP.Api.Tests;

// Playwright 的测试宿主；复用 Aspire 测试夹具，不为业务应用增加验收 HTTP 接口。
var password = Environment.GetEnvironmentVariable("OPENERP_TEST_ADMIN_PASSWORD");
var token = Environment.GetEnvironmentVariable("OPENERP_TEST_ERP_TOKEN");
if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("E2E host requires OPENERP_TEST_ADMIN_PASSWORD and OPENERP_TEST_ERP_TOKEN.");
    return 2;
}

using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(25));
Console.CancelKeyPress += (_, args) => { args.Cancel = true; lifetime.Cancel(); };
await using var fixture = new AspireIntegrationFixture(false, password, token,
    Environment.GetEnvironmentVariable("OPENERP_E2E_ERP_URL"));
await fixture.StartAsync(lifetime.Token);
var web = await fixture.StartWebAsync(lifetime.Token);
var connection = await fixture.GetPostgresConnectionStringAsync(lifetime.Token);

var certificateFile = Path.Combine(Path.GetTempPath(), $"openerp-e2e-ca-{Guid.NewGuid():N}.pem");
try
{
    string? certificate = null;
    using var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, serverCertificate, _, errors) =>
        {
            if (errors != SslPolicyErrors.None || serverCertificate is null) return false;
            certificate = serverCertificate.ExportCertificatePem();
            return true;
        }
    };
    using var http = new HttpClient(handler);
    using var response = await http.GetAsync(new Uri(web, "/api/auth/csrf"), lifetime.Token);
    response.EnsureSuccessStatusCode();
    if (certificate is null) throw new InvalidOperationException("No trusted HTTPS certificate was returned.");
    if (Environment.GetEnvironmentVariable("NODE_EXTRA_CA_CERTS") is { Length: > 0 } existing)
        certificate += "\n" + await File.ReadAllTextAsync(existing, lifetime.Token);
    await File.WriteAllTextAsync(certificateFile, certificate, lifetime.Token);

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    await using var control = builder.Build();
    control.Use(async (context, next) =>
    {
        if (context.Request.Headers.Authorization != $"Bearer {password}")
        {
            context.Response.StatusCode = 401;
            return;
        }
        await next();
    });
    control.MapGet("/database", (int sid, CancellationToken ct) => OrderDatabase.ReadAsync(connection, sid, ct));
    control.MapGet("/verify", (int sid, CancellationToken ct) => OrderDatabase.VerifyLiveAsync(connection, token, sid, ct));
    await control.StartAsync(lifetime.Token);
    // 只把公开端点和公钥证书路径传回父进程；不打印密码、ERP Token 或数据库连接串。
    Console.WriteLine("OPENERP_E2E_READY=" + JsonSerializer.Serialize(new
    {
        baseUrl = web.ToString(), controlUrl = control.Urls.Single(), certificateFile
    }));
    await Console.In.ReadLineAsync(lifetime.Token);
    await control.StopAsync(lifetime.Token);
}
finally
{
    File.Delete(certificateFile);
}
return 0;
