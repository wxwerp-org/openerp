using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpenERP.Api.ObjectStorage;

namespace OpenERP.Api;

public static class FileEndpoints
{
    public static void MapFiles(this WebApplication app)
    {
        var files = app.MapGroup("/api/files");
        files.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (ObjectStorageException exception) when (exception.NotFound)
            {
                return TypedResults.NotFound();
            }
            catch (ObjectStorageException exception)
            {
                app.Logger.LogError(exception, "File storage request failed");
                return TypedResults.Problem("文件存储暂时不可用，请稍后重试。", statusCode: 503);
            }
        });

        files.MapGet("/", async (FileStore store, CancellationToken ct) =>
            TypedResults.Ok(await store.ListAsync(ct)));

        files.MapPost("/", async Task<Results<Created<StoredFile>, ValidationProblem>> (
            IFormFile file, FileStore store, CancellationToken ct) =>
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            var contentType = FileStore.ContentType(name);
            if (name.Length is 0 or > 200 || name.Any(char.IsControl) || contentType is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["请选择图片（JPG、PNG、GIF、WebP）、PDF 或 Excel（XLS、XLSX），文件名不超过 200 个字符。"]
                });
            if (file.Length is <= 0 or > FileStore.MaxFileSize)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["文件不能为空，且不能超过 20 MiB。"]
                });

            await using var input = file.OpenReadStream();
            var result = await store.UploadAsync(input, name, contentType, file.Length, ct);
            return TypedResults.Created($"/api/files/{Uri.EscapeDataString(result.Key)}", result);
        })
        .WithMetadata(new RequestSizeLimitAttribute(FileStore.MaxFileSize + 1024 * 1024))
        .DisableAntiforgery();

        files.MapGet("/{key}", async Task<Results<PushStreamHttpResult, NotFound>> (
            string key, bool? download, FileStore store, HttpContext context, CancellationToken ct) =>
        {
            if (await store.OpenAsync(key, ct) is not { } file) return TypedResults.NotFound();
            context.Response.RegisterForDispose(file.Object);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.ContentLength = file.Metadata.Size;
            var type = file.Metadata.ContentType;
            var attachment = download == true || type.StartsWith("application/vnd.", StringComparison.Ordinal);
            return TypedResults.Stream(output => file.Object.Content.CopyToAsync(output, ct),
                type, attachment ? file.Metadata.FileName : null);
        });

        files.MapDelete("/{key}", async Task<Results<NoContent, NotFound>> (
            string key, FileStore store, CancellationToken ct) =>
        {
            if (!FileStore.IsValidKey(key)) return TypedResults.NotFound();
            await store.DeleteAsync(key, ct);
            return TypedResults.NoContent();
        });
    }
}
