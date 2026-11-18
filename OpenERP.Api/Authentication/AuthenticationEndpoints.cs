using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpenERP.Api.Authentication;

internal static partial class AuthenticationEndpoints
{
    private const string RefreshCookie = "__Secure-OpenERP.Refresh";

    internal static void AddAccountAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var tokens = new AccessTokens();
        services.AddSingleton(tokens);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AccountStore>();
        services.AddSingleton<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
        services.AddDataProtection().SetApplicationName("OpenERP").PersistKeysToDbContext<AppDbContext>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = tokens.ValidationParameters();
            options.Events.OnMessageReceived = context =>
            {
                // 防伪令牌与匿名身份绑定。公开接口和健康检查无须验证 JWT。
                if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                    context.NoResult();
                return Task.CompletedTask;
            };
            options.Events.OnTokenValidated = ValidateLoginAsync;
        });
        services.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint() is { } endpoint && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                context.Features.Get<AdminRefreshToken>() is null)
            {
                var store = context.RequestServices.GetRequiredService<AccountStore>();
                bool initialized;
                try
                {
                    initialized = await store.IsInitializedAsync(context.RequestAborted);
                }
                catch (Exception exception) when (IsDatabaseFailure(exception))
                {
                    throw new AccountStoreUnavailableException(exception);
                }

                if (!initialized)
                {
                    await InitializationRequired().ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });
        app.UseAuthorization();
        app.UseAntiforgery();

        app.Use(async (context, next) =>
        {
            // 内置中间件记录防伪校验结果，这里为 JSON 和无请求体的端点拒绝无效请求。
            if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
            {
                await Results.Problem("CSRF 校验失败，请刷新页面后重试。", statusCode: 400).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
    }

    internal static void MapAccounts(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .AllowAnonymous();
        auth.MapGet("/status", GetStatusAsync).AllowAnonymous()
            .WithSummary("查询管理员是否已初始化")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var anonymousWrites = auth.MapGroup("").AllowAnonymous()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        anonymousWrites.MapPost("/initialize", InitializeAsync)
            .WithSummary("首次设置管理员密码")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        anonymousWrites.MapPost("/login", LoginAsync);
        anonymousWrites.MapPost("/refresh", RefreshAsync);
        anonymousWrites.MapPost("/logout", LogoutAsync);

        auth.MapGet("/me", (HttpContext context) => TypedResults.Ok(ToProfile(CurrentLogin(context)))).RequireAuthorization();
        auth.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization();
        app.MapPut("/api/account/profile", UpdateProfileAsync);
    }

    private static async Task<Ok<AuthenticationStatusResponse>> GetStatusAsync(AccountStore store, CancellationToken ct) =>
        TypedResults.Ok(new AuthenticationStatusResponse(await store.IsInitializedAsync(ct)));

    private static async Task<Results<NoContent, ProblemHttpResult>> InitializeAsync(
        InitializeRequest request, HttpContext context, AccountStore store,
        TimeProvider clock, ILogger<Program> logger, CancellationToken ct)
    {
        if (!AccountStore.IsValidPassword(request.NewPassword))
            return TypedResults.Problem("新密码长度必须为 6–20 个字符。", statusCode: 400);

        if (!await store.InitializeAsync(request.NewPassword!, ct))
            return TypedResults.Problem("管理员已初始化。", statusCode: 409,
                extensions: new Dictionary<string, object?> { ["code"] = "already_initialized" });

        logger.LogInformation("管理员初始化成功。InitializedAtUtc={InitializedAtUtc:O} RemoteIpAddress={RemoteIpAddress} UserAgent={UserAgent}",
            clock.GetUtcNow(), context.Connection.RemoteIpAddress, context.Request.Headers.UserAgent.ToString());
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, HttpContext context, AccountStore store, AccessTokens tokens, TimeProvider clock, CancellationToken ct)
    {
        if (!await store.IsInitializedAsync(ct))
            return InitializationRequired();

        var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var login = await store.LoginAsync(request.Username, request.Password, HashRefreshToken(refreshToken), ct);
        if (login is null)
            return TypedResults.Problem("用户名或密码错误。", statusCode: 401);

        var response = IssueAccess(tokens, login, clock.GetUtcNow());
        if (response is null)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);

        var cookie = RefreshCookieOptions();
        cookie.Expires = login.ExpiresAt;
        context.Response.Cookies.Append(RefreshCookie, refreshToken, cookie);
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        HttpContext context, AccountStore store, AccessTokens tokens, TimeProvider clock, CancellationToken ct)
    {
        AdminRefreshToken? login = null;
        if (context.Request.Cookies.TryGetValue(RefreshCookie, out var refreshToken))
        {
            login = await store.FindActiveRefreshTokenAsync(HashRefreshToken(refreshToken), ct);
            // 管理员记录缺失时会级联删除凭证，由现有异常处理返回 503。
            if (login is null)
                await store.IsInitializedAsync(ct);
        }

        var response = login is null ? null : IssueAccess(tokens, login, clock.GetUtcNow());
        if (response is null)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);

        return TypedResults.Ok(response);
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context, AccountStore store, CancellationToken ct)
    {
        if (context.Request.Cookies.TryGetValue(RefreshCookie, out var refreshToken))
            await store.RevokeAsync(HashRefreshToken(refreshToken), ct);

        context.Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions());
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext context, AccountStore store, CancellationToken ct)
    {
        if (!AccountStore.IsValidPassword(request.NewPassword))
            return TypedResults.Problem("新密码长度必须为 6–20 个字符。", statusCode: 400);

        var result = await store.ChangePasswordAsync(CurrentLogin(context).Id, request.OldPassword, request.NewPassword!, ct);
        if (result == PasswordChangeResult.SessionExpired)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);
        if (result == PasswordChangeResult.InvalidPassword)
            return TypedResults.Problem("原密码错误。", statusCode: 400);

        context.Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions());
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

        var login = await store.UpdateProfileAsync(CurrentLogin(context).Id, name!, phone, email, ct);
        if (login is null)
            return TypedResults.Problem("登录会话已失效。", statusCode: 401);

        return TypedResults.Ok(ToProfile(login));
    }

    private static async Task ValidateLoginAsync(TokenValidatedContext context)
    {
        var id = context.Principal?.FindFirstValue(AccessTokens.LoginIdClaim);
        var store = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
        AdminRefreshToken? login;
        try
        {
            login = Guid.TryParseExact(id, "D", out var loginId)
                ? await store.FindActiveRefreshTokenAsync(loginId, context.HttpContext.RequestAborted)
                : null;
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            throw new AccountStoreUnavailableException(exception);
        }

        if (login is null)
            context.Fail("登录会话已失效。");
        else
            context.HttpContext.Features.Set(login);
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

    private static AdminRefreshToken CurrentLogin(HttpContext context) => context.Features.Get<AdminRefreshToken>()!;

    private static AccountProfile ToProfile(AdminRefreshToken login) =>
        new(AdminAccount.Username, login.Admin.Name, login.Admin.Phone, login.Admin.Email,
            login.Admin.LastLoginAt, login.ExpiresAt);

    private static TokenResponse? IssueAccess(AccessTokens tokens, AdminRefreshToken login, DateTimeOffset now) =>
        tokens.Issue(login, now) is { } access ? new(access.Value, access.ExpiresAt, ToProfile(login)) : null;

    private static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static CookieOptions RefreshCookieOptions() => new()
    {
        Path = "/api/auth",
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict
    };

    private static ProblemHttpResult InitializationRequired() =>
        TypedResults.Problem("请先设置管理员密码。", statusCode: 409,
            extensions: new Dictionary<string, object?> { ["code"] = "initialization_required" });

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$")]
    private static partial Regex EmailPattern();

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CsrfResponse(string RequestToken);

    /// <summary>管理员密码初始化状态。</summary>
    private sealed record AuthenticationStatusResponse(bool Initialized);

    /// <summary>首次设置管理员密码的请求。</summary>
    private sealed record InitializeRequest(string? NewPassword);

    private sealed record LoginRequest(string? Username, string? Password);
    private sealed record ChangePasswordRequest(string? OldPassword, string? NewPassword);
    private sealed record UpdateProfileRequest(string? Name, string? Phone, string? Email);
    private sealed record AccountProfile(
        string Username, string Name, string? Phone, string? Email, DateTimeOffset? LastLoginAt, DateTimeOffset ExpiresAt);
    private sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, AccountProfile Account);
}
