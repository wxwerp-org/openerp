using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Tests;

internal static class ErpEndpointTestAuth
{
    private const string Scheme = "Test";
    private const string RoleHeader = "X-Test-Role";

    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestRoleHandler>(Scheme, _ => { });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(UserAccount.AdministratorRole, policy => policy.RequireRole(UserAccount.AdministratorRole));
    }

    public static HttpClient CreateClient(WebApplication app, string role)
    {
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        http.DefaultRequestHeaders.Add(RoleHeader, role);
        return http;
    }

    private sealed class TestRoleHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
