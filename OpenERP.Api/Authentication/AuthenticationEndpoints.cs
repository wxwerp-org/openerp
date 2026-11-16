using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpenERP.Api.Authentication;

internal static partial class AuthenticationEndpoints
{
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    private const string SessionCookie = "__Host-OpenERP.Session";

    internal static void AddAccountAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AccountStore>();
        services.AddSingleton<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
        services.AddDataProtection().SetApplicationName("OpenERP").PersistKeysToDbContext<AppDbContext>();
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = SessionCookie;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = AccountStore.SessionLifetime;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.HttpContext.Features.Get<AdminSession>() is { Admin.MustChangePassword: true })
                    return PasswordChangeRequired().ExecuteAsync(context.HttpContext);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .RequireAssertion(context => context.Resource is HttpContext http &&
                    http.Features.Get<AdminSession>() is { Admin.MustChangePassword: false })
                .Build());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.SuppressReadingTokenFromFormBody = true;
            options.Cookie.Name = "__Host-OpenERP.Csrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        services.AddProblemDetails();
    }

    internal static void UseAccountAuthentication(this WebApplication app)
    {
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized ||
                context.Response.StatusCode == StatusCodes.Status400BadRequest && IsAccountEndpoint(context))
                await Results.Problem(statusCode: context.Response.StatusCode).ExecuteAsync(context);
        });

        app.Use(async (context, next) =>
        {
            try
            {
                if (IsAccountEndpoint(context))
                    context.Response.Headers.CacheControl = "no-store";
                await next(context);
            }
            catch (BadHttpRequestException exception) when (!context.Response.HasStarted && IsAccountEndpoint(context))
            {
                await Results.Problem("请求内容无效。", statusCode: exception.StatusCode).ExecuteAsync(context);
            }
            catch (Exception exception) when (!context.Response.HasStarted &&
                (exception is AccountStoreUnavailableException ||
                 IsAccountEndpoint(context) && IsDatabaseFailure(exception)))
            {
                app.Logger.LogError(exception, "管理员认证数据库暂时不可用。");
                await Results.Problem("管理员认证暂时不可用，请稍后重试。", statusCode: 503).ExecuteAsync(context);
            }
        });

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            if (context.GetEndpoint() is not null &&
                !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ||
                  HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) &&
                !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
            {
                await Results.Problem("CSRF 校验失败，请刷新页面后重试。", statusCode: 400).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
        app.UseAntiforgery();
    }

    internal static void MapAccounts(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapGet("/me", (HttpContext context) => TypedResults.Ok(ToProfile(CurrentSession(context)))).RequireAuthorization();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        auth.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization();
        app.MapPut("/api/account/profile", UpdateProfileAsync);
    }

    private static async Task<Results<Ok<AccountProfile>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, HttpContext context, AccountStore store, CancellationToken ct)
    {
        if (context.Features.Get<AdminSession>() is { Admin.MustChangePassword: true })
            return PasswordChangeRequired();

        var session = await store.LoginAsync(request.Username, request.Password, ct);
        if (session is null)
            return TypedResults.Problem("用户名或密码错误。", statusCode: 401);

        // CSRF token 与会话绑定，同一管理员的其他设备具有独立 token。
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, AdminAccount.Username),
            new Claim(ClaimTypes.NameIdentifier, session.Id.ToString("D"))
        ], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = session.ExpiresAt - AccountStore.SessionLifetime,
            ExpiresUtc = session.ExpiresAt,
            AllowRefresh = false
        });

        return TypedResults.Ok(ToProfile(session));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context, AccountStore store, CancellationToken ct)
    {
        await store.RevokeAsync(CurrentSession(context).Id, ct);
        await context.SignOutAsync(Scheme);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext context, AccountStore store, CancellationToken ct)
    {
        if (!AccountStore.IsValidPassword(request.NewPassword))
            return TypedResults.Problem("新密码长度必须为 6–20 个字符。", statusCode: 400);

        var result = await store.ChangePasswordAsync(CurrentSession(context).Id, request.OldPassword, request.NewPassword!, ct);
        if (result == PasswordChangeResult.SessionExpired)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);
        if (result == PasswordChangeResult.InvalidPassword)
            return TypedResults.Problem("原密码错误。", statusCode: 400);
        if (result == PasswordChangeResult.MustUseDifferentPassword)
            return TypedResults.Problem("首次登录的新密码须与初始密码不同。", statusCode: 400);

        await context.SignOutAsync(Scheme);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AccountProfile>, ProblemHttpResult, ValidationProblem>> UpdateProfileAsync(
        UpdateProfileRequest request, HttpContext context, AccountStore store, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var phone = TrimOptional(request.Phone);
        var email = TrimOptional(request.Email);
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name) || name.Length > 20)
            errors["name"] = ["姓名必填且不得超过 20 个字符。"];
        if (phone is { Length: > 20 })
            errors["phone"] = ["电话不得超过 20 个字符。"];
        if (email is not null && !EmailPattern().IsMatch(email))
            errors["email"] = ["邮箱格式无效。"];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var session = await store.UpdateProfileAsync(CurrentSession(context).Id, name!, phone, email, ct);
        if (session is null)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);

        return TypedResults.Ok(ToProfile(session));
    }

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.HttpContext.Request.Path is { Value: "/health" or "/alive" })
        {
            context.RejectPrincipal();
            return;
        }

        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var store = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
        AdminSession? session;
        try
        {
            session = Guid.TryParseExact(id, "D", out var sessionId)
                ? await store.FindSessionAsync(sessionId, context.HttpContext.RequestAborted)
                : null;
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            throw new AccountStoreUnavailableException(exception);
        }

        if (session is null)
            context.RejectPrincipal();
        else
            context.HttpContext.Features.Set(session);
    }

    private static bool IsAccountEndpoint(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/auth") || context.Request.Path.StartsWithSegments("/api/account");

    internal static bool IsDatabaseFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException or DbUpdateException)
                return true;
        }

        return false;
    }

    private static AdminSession CurrentSession(HttpContext context) => context.Features.Get<AdminSession>()!;

    private static AccountProfile ToProfile(AdminSession session) =>
        new(AdminAccount.Username, session.Admin.Name, session.Admin.Phone, session.Admin.Email,
            session.Admin.LastLoginAt, session.ExpiresAt, session.Admin.MustChangePassword);

    private static ProblemHttpResult PasswordChangeRequired() =>
        TypedResults.Problem("首次登录须先修改密码。", statusCode: 403,
            extensions: new Dictionary<string, object?> { ["code"] = "password_change_required" });

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$")]
    private static partial Regex EmailPattern();

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class AccountStoreUnavailableException(Exception innerException) : Exception(null, innerException);

    private sealed record CsrfResponse(string RequestToken);
    private sealed record LoginRequest(string? Username, string? Password);
    private sealed record ChangePasswordRequest(string? OldPassword, string? NewPassword);
    private sealed record UpdateProfileRequest(string? Name, string? Phone, string? Email);
    private sealed record AccountProfile(
        string Username, string Name, string? Phone, string? Email, DateTimeOffset? LastLoginAt, DateTimeOffset ExpiresAt,
        bool MustChangePassword);
}
