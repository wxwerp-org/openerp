using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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
            catch (FileNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                app.Logger.LogError(exception, "File storage request failed");
                return TypedResults.Problem("文件存储暂时不可用，请稍后重试。", statusCode: 503);
            }
        });

        files.MapGet("/", async (IFileStore store, CancellationToken ct) =>
            TypedResults.Ok(await store.ListAsync(ct)));

        files.MapPost("/", async Task<Results<Created<StoredFile>, ValidationProblem>> (
            IFormFile file, IFileStore store, CancellationToken ct) =>
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            var contentType = StoredFile.GetContentType(name);
            if (name.Length is 0 or > 200 || name.Any(char.IsControl) || contentType is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["请选择图片（JPG、PNG、GIF、WebP）、PDF 或 Excel（XLS、XLSX），文件名不超过 200 个字符。"]
                });
            if (file.Length is <= 0 or > StoredFile.MaxFileSize)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["文件不能为空，且不能超过 20 MiB。"]
                });

            await using var input = file.OpenReadStream();
            var result = await store.UploadAsync(input, name, contentType, file.Length, ct);
            return TypedResults.Created($"/api/files/{Uri.EscapeDataString(result.Key)}", result);
        })
        .WithMetadata(new RequestSizeLimitAttribute(StoredFile.MaxFileSize + 1024 * 1024))
        .DisableAntiforgery();

        files.MapGet("/{key}", async Task<Results<PushStreamHttpResult, NotFound>> (
            string key, bool? download, IFileStore store, HttpContext context, CancellationToken ct) =>
        {
            var response = await store.OpenAsync(key, ct);
            context.Response.RegisterForDispose(response);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.ContentLength = response.File.Size;
            var type = response.File.ContentType;
            var attachment = download == true || type.StartsWith("application/vnd.", StringComparison.Ordinal);
            return TypedResults.Stream(output => response.Content.CopyToAsync(output, ct),
                type, attachment ? response.File.FileName : null);
        });

        files.MapDelete("/{key}", async Task<Results<NoContent, NotFound>> (
            string key, IFileStore store, CancellationToken ct) =>
        {
            if (!StoredFile.IsValidKey(key)) return TypedResults.NotFound();
            await store.DeleteAsync(key, ct);
            return TypedResults.NoContent();
        });
    }
}
