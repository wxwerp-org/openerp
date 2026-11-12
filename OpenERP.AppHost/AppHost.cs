var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("openerp");

var redis = builder.AddRedis("redis")
    .WithImageTag("8")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var storage = builder.AddSeaweedFS("seaweedfs")
    .WithImageTag("4.53")
    .WithS3()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder.AddProject<Projects.OpenERP_Api>("api")
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(database)
    .WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

// Token 仅注入后端；未配置时仍可启动并在店铺页提示。
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:wxw-erp-access-token"]))
{
    var erpToken = builder.AddParameter("wxw-erp-access-token", secret: true);
    api.WithEnvironment("Erp__AccessToken", erpToken);
}

var web = builder.AddViteApp("web", "../OpenERP.Web")
    .WithPnpm(installArgs: ["--frozen-lockfile"])
    .WithReference(api)
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("https"))
    .WaitFor(api)
    .WithExternalHttpEndpoints();

#pragma warning disable ASPIRECERTIFICATES001 // Use Aspire's local HTTPS certificate integration for Vite.
web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // Explicitly approved development-only browser diagnostics.
web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
