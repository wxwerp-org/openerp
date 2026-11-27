using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Hybrid;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Erp;

/// <summary>各平台 API 能力展示：读取旺销王开放 API 清单与 Schema，两者都是公开只读接口，不依赖已保存的 AccessToken。</summary>
public static class PlatformAbilityEndpoints
{
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(10) };

    public static void MapPlatformAbilities(this WebApplication app)
    {
        app.MapGet("/api/erp/platform-abilities", async Task<Results<Ok<IReadOnlyList<PlatformAbility>>, ProblemHttpResult>>
            (IErpTokenStore store, IPlatformAbilityClient client, HybridCache cache, CancellationToken cancellationToken) =>
        {
            var configuration = await store.GetConfigurationAsync(cancellationToken);
            if (configuration.IsErr)
                return TypedResults.Problem(statusCode: 503, title: configuration.Error.Message);

            var baseUrl = configuration.Value.BaseUrl;
            var key = $"platform-abilities:{baseUrl}";
            var entry = await cache.GetOrCreateAsync(
                key,
                async token =>
                {
                    var result = await client.GetAbilitiesAsync(baseUrl, token);
                    return result.IsErr
                        ? new AbilityCacheEntry(null, result.Error.Message)
                        : new AbilityCacheEntry(result.Value, null);
                },
                CacheOptions,
                cancellationToken: cancellationToken);

            if (entry.Items is not null)
                return TypedResults.Ok(entry.Items);

            // 失败不留在缓存里，下次打开页面重新请求。
            await cache.RemoveAsync(key, cancellationToken);
            return TypedResults.Problem(statusCode: 502, title: entry.Error);
        }).RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("读取旺销王开放 API 的各平台接口能力清单")
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/erp/platform-abilities/{platform}/schemas/{name}",
            async Task<Results<ContentHttpResult, ProblemHttpResult>>
                (string platform, string name, IErpTokenStore store, IPlatformAbilityClient client, CancellationToken cancellationToken) =>
            {
                var configuration = await store.GetConfigurationAsync(cancellationToken);
                if (configuration.IsErr)
                    return TypedResults.Problem(statusCode: 503, title: configuration.Error.Message);

                var schema = await client.GetSchemaAsync(configuration.Value.BaseUrl, platform, name, cancellationToken);
                return schema.IsErr
                    ? TypedResults.Problem(statusCode: 502, title: schema.Error.Message)
                    : TypedResults.Content(schema.Value.GetRawText(), "application/json");
            }).RequireAuthorization(UserAccount.AdministratorRole)
            .WithSummary("读取某个平台接口的请求、响应 Schema")
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record AbilityCacheEntry(IReadOnlyList<PlatformAbility>? Items, string? Error);
}
