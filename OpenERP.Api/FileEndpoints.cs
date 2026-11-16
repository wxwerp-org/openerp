using System.Net;
using Amazon.S3;
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
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return TypedResults.NotFound();
            }
            catch (AmazonS3Exception exception)
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

            var result = await store.UploadAsync(file, name, contentType, ct);
            return TypedResults.Created($"/api/files/{Uri.EscapeDataString(result.Key)}", result);
        })
        .WithMetadata(new RequestSizeLimitAttribute(FileStore.MaxFileSize + 1024 * 1024));

        files.MapGet("/{key}", async Task<Results<PushStreamHttpResult, NotFound>> (
            string key, bool? download, FileStore store, HttpContext context, CancellationToken ct) =>
        {
            if (!FileStore.IsValidKey(key)) return TypedResults.NotFound();
            var response = await store.OpenAsync(key, ct);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.ContentLength = response.ContentLength;
            var type = FileStore.ContentType(key)!;
            var attachment = download == true || type.StartsWith("application/vnd.", StringComparison.Ordinal);
            return TypedResults.Stream(async output =>
            {
                using (response)
                    await response.ResponseStream.CopyToAsync(output, ct);
            }, type, attachment ? key[33..] : null);
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
